using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Dapper;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Agro360.Infrastructure.Services;

/// <summary>
/// Varredura de recuperação de intents de inspeção (executada pelo worker por tenant ativo):
/// (1) adota origens confirmadas que não têm intent — o processo caiu entre o commit da origem
///     e a criação do intent; (2) claima intents vencidos com lease para processamento concorrente.
/// Usa transações com tenant explícito (sem ITenantContext de requisição).
/// </summary>
public sealed class InspectionIntentRecoveryService(
    DatabaseExecutor db,
    IOptions<InspectionIntentsOptions> options) : IInspectionIntentRecoveryService
{
    private static readonly string ClaimedBy = BuildClaimedBy();

    private static string BuildClaimedBy()
    {
        var host = Environment.MachineName.Length > 64 ? Environment.MachineName[^64..] : Environment.MachineName;
        return $"{host}:{Environment.ProcessId}";
    }

    public Task<int> AdoptMissingOriginIntentsForTenantAsync(Guid tenantId, CancellationToken ct) =>
        db.InTenantTransactionAsync<int>(tenantId, async (c, t) =>
        {
            var windowDays = Math.Clamp(options.Value.OriginSweepWindowDays, 1, 30);
            var adopted = 0;
            adopted += await AdoptFromCandidatesAsync(c, t, tenantId, "HARVEST_RECEIPT", "production_receipts", windowDays, CandidatesSql["production_receipts"]!, ct);
            adopted += await AdoptFromCandidatesAsync(c, t, tenantId, "PURCHASE_RECEIPT", "procurement_receipts", windowDays, CandidatesSql["procurement_receipts"]!, ct);
            adopted += await AdoptFromCandidatesAsync(c, t, tenantId, "PRODUCTION", "production_batches", windowDays, CandidatesSql["production_batches"]!, ct);
            adopted += await AdoptFromCandidatesAsync(c, t, tenantId, "RETURN", "fulfillment_return_receipts", windowDays, CandidatesSql["fulfillment_return_receipts"]!, ct);
            return adopted;
        }, ct);

    private static async Task<int> AdoptFromCandidatesAsync(
        NpgsqlConnection c,
        NpgsqlTransaction t,
        Guid tenantId,
        string processCode,
        string originType,
        int windowDays,
        string candidatesSql,
        CancellationToken ct)
    {
        var candidates = (await c.QueryAsync<OriginCandidateRow>(new CommandDefinition(
            candidatesSql, new { TenantId = tenantId, WindowDays = windowDays }, t, cancellationToken: ct))).ToArray();

        var adopted = 0;
        foreach (var origin in candidates)
        {
            var key = InspectionEventKeys.BuildKey(processCode, origin.OriginId);
            var hash = InspectionEventKeys.ComputeHash(processCode, originType, origin.OriginId, origin.ProductId, origin.LotId, origin.UnitId);
            var inserted = await c.ExecuteAsync(new CommandDefinition("""
                insert into agro360.quality_inspection_event_intents(
                    id, tenant_id, process_code, origin_type, origin_id, product_id, lot_id, unit_id,
                    actor_id, idempotency_key, request_hash, status, notes, created_by, updated_by)
                values(
                    @Id, @TenantId, @ProcessCode, @OriginType, @OriginId, @ProductId, @LotId, @UnitId,
                    @ActorId, @Key, @Hash, 'PENDING', @Notes, @ActorId, @ActorId)
                on conflict (tenant_id, idempotency_key) where deleted_at is null do nothing
                """, new
                {
                    Id = Guid.CreateVersion7(),
                    TenantId = tenantId,
                    ProcessCode = processCode,
                    OriginType = originType,
                    OriginId = origin.OriginId,
                    origin.ProductId,
                    origin.LotId,
                    origin.UnitId,
                    ActorId = origin.ActorId,
                    Key = key,
                    Hash = hash,
                    Notes = "Adotado pela varredura de recuperação de origens pendentes de inspeção."
                }, t, cancellationToken: ct));
            adopted += inserted;
        }
        return adopted;
    }

    public Task<IReadOnlyList<InspectionIntentWorkItem>> ClaimDueIntentsForTenantAsync(
        Guid tenantId, int batchSize, CancellationToken ct) =>
        db.InTenantTransactionAsync<IReadOnlyList<InspectionIntentWorkItem>>(tenantId, async (c, t) =>
        {
            var items = (await c.QueryAsync<ClaimRow>(new CommandDefinition("""
                select id IntentId, actor_id ActorId, status Status, attempts Attempts
                from agro360.quality_inspection_event_intents
                where tenant_id = @TenantId and deleted_at is null and dead_lettered_at is null
                  and status in ('PENDING', 'PENDING_MODEL')
                  and next_attempt_at <= now()
                  and (lease_expires_at is null or lease_expires_at < now())
                order by next_attempt_at
                limit @BatchSize
                for update skip locked
                """, new { TenantId = tenantId, BatchSize = batchSize }, t, cancellationToken: ct))).ToArray();

            foreach (var item in items)
            {
                await c.ExecuteAsync(new CommandDefinition("""
                    update agro360.quality_inspection_event_intents
                    set claimed_at = now(), claimed_by = @ClaimedBy,
                        lease_expires_at = now() + make_interval(secs => @LeaseSeconds)
                    where tenant_id = @TenantId and id = @IntentId
                    """, new { TenantId = tenantId, ClaimedBy, IntentId = item.IntentId, LeaseSeconds = options.Value.LeaseSeconds }, t, cancellationToken: ct));
            }

            return items.Select(i => new InspectionIntentWorkItem(tenantId, i.IntentId, i.ActorId, i.Status, i.Attempts)).ToArray();
        }, ct);

    public Task MarkProcessingFailureAsync(Guid tenantId, Guid intentId, Exception failure, CancellationToken ct) =>
        db.InTenantTransactionAsync(tenantId, async (c, t) =>
        {
            // Mesmas semânticas de backoff/dead-letter do outbox canônico: exponencial limitado a 1h,
            // dead-letter ao exceder o máximo configurado (com motivo + ação operacional de re-disparo).
            await c.ExecuteAsync(new CommandDefinition("""
                update agro360.quality_inspection_event_intents
                set status = case when attempts + 1 >= @MaximumAttempts then 'DEAD_LETTERED' else status end,
                    attempts = attempts + 1,
                    last_error = left(@Error, 2000),
                    error_code = case when attempts + 1 >= @MaximumAttempts then 'inspection.creation_failed' else error_code end,
                    next_attempt_at = now() + make_interval(secs => least(3600, power(2, least(attempts, 10))::int)),
                    dead_lettered_at = case when attempts + 1 >= @MaximumAttempts then now() else dead_lettered_at end,
                    claimed_at = null, claimed_by = null, lease_expires_at = null,
                    updated_at = now()
                where tenant_id = @TenantId and id = @IntentId and deleted_at is null
                  and status in ('PENDING', 'PENDING_MODEL')
                """, new
                {
                    TenantId = tenantId,
                    IntentId = intentId,
                    Error = $"{failure.GetType().Name}: {failure.Message}",
                    MaximumAttempts = options.Value.MaximumAttempts
                }, t, cancellationToken: ct));
        }, ct);

    public Task MarkSkippedNoActorAsync(Guid tenantId, Guid intentId, CancellationToken ct) =>
        db.InTenantTransactionAsync(tenantId, async (c, t) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                update agro360.quality_inspection_event_intents
                set status = 'SKIPPED_NO_ACTOR',
                    notes = 'Nenhum operador vinculado ao evento para executar a inspeção automática.',
                    claimed_at = null, claimed_by = null, lease_expires_at = null,
                    updated_at = now()
                where tenant_id = @TenantId and id = @IntentId and deleted_at is null
                  and status in ('PENDING', 'PENDING_MODEL')
                """, new { TenantId = tenantId, IntentId = intentId }, t, cancellationToken: ct));
        }, ct);

    /// <summary>
    /// Candidatos por tipo de origem (janela configurável; chave única por origem evita duplas).
    /// Tipos sem coluna de qualidade (compras/retornos) espelham o disparo original incondicional.
    /// </summary>
    private static readonly Dictionary<string, string> CandidatesSql = new()
    {
        ["production_receipts"] = """
            select q.id OriginId, q.product_id ProductId, null::uuid LotId, q.warehouse_id UnitId, q.created_by ActorId
            from agro360.production_receipts q
            where q.tenant_id = @TenantId
              and q.quality_status = 'AWAITING_INSPECTION'
              and q.received_at >= now() - (@WindowDays || ' days')::interval
              and not exists (select 1 from agro360.quality_inspection_event_intents i
                               where i.tenant_id = q.tenant_id and i.deleted_at is null
                                 and i.idempotency_key = 'EVT:HARVEST_RECEIPT:' || replace(q.id::text, '-', ''))
            limit 200
            """,
        ["procurement_receipts"] = """
            select r.id OriginId, fi.catalog_item_id ProductId, null::uuid LotId, r.warehouse_id UnitId, r.created_by ActorId
            from agro360.procurement_receipts r
            left join lateral (
                select oi.catalog_item_id
                from agro360.procurement_receipt_items ri
                join agro360.procurement_purchase_order_items oi
                  on oi.tenant_id = ri.tenant_id and oi.id = ri.purchase_order_item_id
                where ri.tenant_id = r.tenant_id and ri.receipt_id = r.id
                order by ri.created_at
                limit 1
            ) fi on true
            where r.tenant_id = @TenantId and r.deleted_at is null
              and r.received_at >= now() - (@WindowDays || ' days')::interval
              and not exists (select 1 from agro360.quality_inspection_event_intents i
                               where i.tenant_id = r.tenant_id and i.deleted_at is null
                                 and i.idempotency_key = 'EVT:PURCHASE_RECEIPT:' || replace(r.id::text, '-', ''))
            limit 200
            """,
        ["production_batches"] = """
            select b.id OriginId, b.product_id ProductId, null::uuid LotId, null::uuid UnitId, b.created_by ActorId
            from agro360.production_batches b
            where b.tenant_id = @TenantId and b.deleted_at is null
              and b.quality_status = 'PENDING'
              and b.manufactured_at >= now() - (@WindowDays || ' days')::interval
              and not exists (select 1 from agro360.quality_inspection_event_intents i
                               where i.tenant_id = b.tenant_id and i.deleted_at is null
                                 and i.idempotency_key = 'EVT:PRODUCTION:' || replace(b.id::text, '-', ''))
            limit 200
            """,
        ["fulfillment_return_receipts"] = """
            select x.id OriginId, null::uuid ProductId, null::uuid LotId, x.warehouse_id UnitId, x.created_by ActorId
            from agro360.fulfillment_return_receipts x
            where x.tenant_id = @TenantId
              and x.received_at >= now() - (@WindowDays || ' days')::interval
              and not exists (select 1 from agro360.quality_inspection_event_intents i
                               where i.tenant_id = x.tenant_id and i.deleted_at is null
                                 and i.idempotency_key = 'EVT:RETURN:' || replace(x.id::text, '-', ''))
            limit 200
            """
    };

    private sealed record OriginCandidateRow(Guid OriginId, Guid? ProductId, Guid? LotId, Guid? UnitId, Guid? ActorId);

    private sealed class ClaimRow
    {
        public Guid IntentId { get; set; }
        public Guid? ActorId { get; set; }
        public string Status { get; set; } = string.Empty;
        public int Attempts { get; set; }
    }
}
