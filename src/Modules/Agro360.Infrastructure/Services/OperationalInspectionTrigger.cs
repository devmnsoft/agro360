using Agro360.Application.Contracts;
using Agro360.Domain.Compliance;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Agro360.Infrastructure.Services;

public sealed class OperationalInspectionTrigger(
    DatabaseExecutor db,
    ITenantContext tenant,
    IInspectionIntentProcessor processor,
    ILogger<OperationalInspectionTrigger> logger) : IOperationalInspectionTrigger
{
    public async Task<OperationalInspectionEventResult> TryStartFromOriginAsync(
        OperationalInspectionEventRequest request,
        CancellationToken ct)
    {
        try
        {
            InspectionModelRules.EnsureValidProcessCode(request.ProcessCode);
        }
        catch (Exception ex)
        {
            InfrastructureLogMessages.InvalidInspectionProcessCode(logger, request.ProcessCode);
            return new OperationalInspectionEventResult(
                Guid.Empty,
                request.ProcessCode,
                request.OriginType,
                request.OriginId,
                "PENDING",
                null,
                ex.Message);
        }

        var processUpper = request.ProcessCode.Trim().ToUpperInvariant();
        var originType = Guard.Required(request.OriginType, nameof(request.OriginType), 60);
        var key = InspectionEventKeys.BuildKey(request.ProcessCode, request.OriginId, request.CustomIdempotencyKey);
        var hash = request.RequestHash ?? InspectionEventKeys.ComputeHash(processUpper, originType, request.OriginId, request.ProductId, request.LotId, request.UnitId);

        Guid intentId = Guid.Empty;
        OperationalInspectionEventResult? existingTerminal = null;

        try
        {
            await db.InTenantTransactionAsync(async (c, t) =>
            {
                var existing = await c.QuerySingleOrDefaultAsync<IntentRow>(new CommandDefinition("""
                    select id, process_code ProcessCode, origin_type OriginType, origin_id OriginId,
                           status, run_id RunId, request_hash RequestHash, notes
                    from agro360.quality_inspection_event_intents
                    where tenant_id = @TenantId and idempotency_key = @Key and deleted_at is null
                    for update
                    """, new { tenant.TenantId, Key = key }, t, cancellationToken: ct));

                if (existing is not null)
                {
                    if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
                        throw new ConflictException("A chave idempotente de evento já foi utilizada com outro conteúdo.", "inspection.idempotency_conflict");

                    intentId = existing.Id;

                    // Se já estiver em estado terminal, devolver sem reprocessar
                    if (existing.Status is "STARTED" or "AMBIGUOUS" or "SKIPPED_NO_ACTOR" or "RESOLVED" or "DEAD_LETTERED")
                    {
                        existingTerminal = new OperationalInspectionEventResult(
                            existing.Id,
                            existing.ProcessCode,
                            existing.OriginType,
                            existing.OriginId,
                            existing.Status,
                            existing.RunId,
                            existing.Notes);
                    }
                    return;
                }

                intentId = Guid.CreateVersion7();
                await c.ExecuteAsync(new CommandDefinition("""
                    insert into agro360.quality_inspection_event_intents(
                        id, tenant_id, process_code, origin_type, origin_id, product_id, lot_id, unit_id,
                        actor_id, idempotency_key, request_hash, status, notes,
                        created_by, updated_by)
                    values(
                        @Id, @TenantId, @ProcessCode, @OriginType, @OriginId, @ProductId, @LotId, @UnitId,
                        @ActorId, @Key, @Hash, 'PENDING', @Notes,
                        @UserId, @UserId)
                    """, new
                {
                    Id = intentId,
                    tenant.TenantId,
                    ProcessCode = processUpper,
                    OriginType = originType,
                    request.OriginId,
                    request.ProductId,
                    request.LotId,
                    request.UnitId,
                    ActorId = tenant.UserId == Guid.Empty ? (Guid?)null : tenant.UserId,
                    Key = key,
                    Hash = hash,
                    Notes = request.Notes,
                    UserId = tenant.UserId == Guid.Empty ? (Guid?)null : tenant.UserId
                }, t, cancellationToken: ct));
            }, ct);
        }
        catch (ConflictException cex) when (cex.Code == "persistence.unique_violation")
        {
            // Race na inserção concorrente do mesmo evento: releia a linha do vencedor e trate
            // como replay silencioso (mesma origem), sem erro visível ao cliente.
            var winner = await db.InTenantTransactionAsync<IntentRow?>(async (c, t) =>
                await c.QuerySingleOrDefaultAsync<IntentRow>(new CommandDefinition("""
                    select id, process_code ProcessCode, origin_type OriginType, origin_id OriginId,
                           status, run_id RunId, request_hash RequestHash, notes
                    from agro360.quality_inspection_event_intents
                    where tenant_id = @TenantId and idempotency_key = @Key and deleted_at is null
                    """, new { tenant.TenantId, Key = key }, t, cancellationToken: ct)), ct);

            if (winner is null)
                throw;
            if (!string.Equals(winner.RequestHash, hash, StringComparison.Ordinal))
                throw new ConflictException("A chave idempotente de evento já foi utilizada com outro conteúdo.", "inspection.idempotency_conflict");

            intentId = winner.Id;
            if (winner.Status is "STARTED" or "AMBIGUOUS" or "SKIPPED_NO_ACTOR" or "RESOLVED" or "DEAD_LETTERED")
            {
                existingTerminal = new OperationalInspectionEventResult(
                    winner.Id,
                    winner.ProcessCode,
                    winner.OriginType,
                    winner.OriginId,
                    winner.Status,
                    winner.RunId,
                    winner.Notes);
            }
        }

        if (existingTerminal is not null)
            return existingTerminal;

        // O processamento efetivo (criação do run, revalidação da origem, contagem de tentativas,
        // backoff e estados) vive na máquina de estados única — compartilhada com o worker de
        // recuperação e com o re-disparo manual.
        return await processor.ProcessIntentAsync(intentId, ct);
    }

    public Task<IReadOnlyList<InspectionEventIntentListItem>> ListEventIntentsAsync(
        string? process,
        string? status,
        int? limit,
        CancellationToken ct) =>
        db.InTenantTransactionAsync<IReadOnlyList<InspectionEventIntentListItem>>(async (c, t) =>
        {
            var max = Math.Clamp(limit ?? 50, 1, 200);
            var rows = await c.QueryAsync<EventIntentListRow>(new CommandDefinition("""
                select i.id, i.process_code ProcessCode, i.origin_type OriginType, i.origin_id OriginId,
                       i.status, i.run_id RunId, r.number RunNumber, i.notes,
                       i.created_at CreatedAt, i.updated_at UpdatedAt,
                       i.attempts Attempts, i.last_error LastError, i.error_code ErrorCode,
                       i.next_attempt_at NextAttemptAt
                from agro360.quality_inspection_event_intents i
                left join agro360.quality_inspection_runs r on r.tenant_id = i.tenant_id and r.id = i.run_id
                where i.tenant_id = @TenantId and i.deleted_at is null
                  and (@Process is null or i.process_code = @Process)
                  and (@Status is null or i.status = @Status)
                order by i.created_at desc
                limit @Limit
                """, new
            {
                tenant.TenantId,
                Process = NullIfEmpty(process),
                Status = NullIfEmpty(status),
                Limit = max
            }, t, cancellationToken: ct));
            return rows.Select(r => new InspectionEventIntentListItem(
                r.Id,
                r.ProcessCode,
                r.OriginType,
                r.OriginId,
                r.Status,
                r.RunId,
                r.RunNumber,
                r.Notes,
                new DateTimeOffset(DateTime.SpecifyKind(r.CreatedAt, DateTimeKind.Utc)),
                new DateTimeOffset(DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc)),
                r.Attempts,
                r.LastError,
                r.ErrorCode,
                new DateTimeOffset(DateTime.SpecifyKind(r.NextAttemptAt, DateTimeKind.Utc)),
                DescribeSituation(r.Status, r.LastError, r.Attempts),
                r.Status is "PENDING" or "PENDING_MODEL" or "AMBIGUOUS" or "DEAD_LETTERED"
            )).ToArray();
        }, ct);

    public async Task<OperationalInspectionEventResult> RedispatchEventIntentAsync(
        Guid intentId,
        InspectionEventIntentRedispatchCommand command,
        CancellationToken ct)
    {
        await db.InTenantTransactionAsync(async (c, t) =>
        {
            var row = await c.QuerySingleOrDefaultAsync<RedispatchRow>(new CommandDefinition("""
                select id Id, status Status, attempts Attempts, actor_id ActorId,
                       process_code ProcessCode, origin_type OriginType, origin_id OriginId
                from agro360.quality_inspection_event_intents
                where tenant_id = @TenantId and id = @Id and deleted_at is null
                for update
                """, new { tenant.TenantId, Id = intentId }, t, cancellationToken: ct))
                ?? throw new NotFoundException("Intent de inspeção", intentId);

            if (row.Status is not ("PENDING" or "PENDING_MODEL" or "AMBIGUOUS" or "DEAD_LETTERED"))
                throw new DomainException(
                    $"O intent no estado {row.Status} não admite re-disparo manual.",
                    "inspection.intent_not_redispatchable");

            // Re-disparo exige justificativa (auditoria). O ator efetivo vem da identidade autenticada;
            // delegar a outro ator só é válido com ActorId explícito (usuário ativo do tenant, validado acima).
            if (string.IsNullOrWhiteSpace(command.Reason))
                throw new DomainException("Informe o motivo do re-disparo; ele fica registrado na auditoria.", "inspection.redispatch_reason_required");

            if (command.ActorId.HasValue)
            {
                var active = await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
                    select exists(select 1 from agro360.identity_users u
                                   where u.tenant_id = @TenantId and u.id = @ActorId
                                     and u.deleted_at is null and u.status = 'ACTIVE')
                    """, new { tenant.TenantId, ActorId = command.ActorId.Value }, t, cancellationToken: ct));
                if (!active)
                    throw new DomainException("O ator informado não é um usuário ativo do tenant.", "inspection.redispatch_actor_invalid");
            }

            await c.ExecuteAsync(new CommandDefinition("""
                update agro360.quality_inspection_event_intents
                set status = 'PENDING',
                    attempts = 0, last_error = null, error_code = null, dead_lettered_at = null,
                    next_attempt_at = now(),
                    claimed_at = null, claimed_by = null, lease_expires_at = null,
                    actor_id = coalesce(@ActorId, @UserId, actor_id),
                    notes = case when @Reason is null then notes else 'Re-disparo manual: ' || @Reason end,
                    updated_at = now(), updated_by = @UserId, row_version = row_version + 1
                where tenant_id = @TenantId and id = @Id
                """, new
                {
                    tenant.TenantId,
                    Id = intentId,
                    ActorId = command.ActorId,
                    Reason = command.Reason,
                    UserId = tenant.UserId
                }, t, cancellationToken: ct));

            await c.WriteAuditAsync(t, tenant, "INSPECTION_INTENT_REDISPATCH", "quality_inspection_event_intent", intentId,
                new { row.Status, row.Attempts, ActorBefore = row.ActorId },
                new { Reason = command.Reason.Trim(), ActorAfter = command.ActorId ?? (tenant.UserId == Guid.Empty ? row.ActorId : tenant.UserId) }, ct);

            return row;
        }, ct);

        // Processamento imediato (melhor esforço): o worker retomaria igualmente; o comando devolve o estado final.
        return await processor.ProcessIntentAsync(intentId, ct);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Situação derivada para orientação operacional: distingue falta de modelo (configuração a publicar),
    /// falha transitória (o worker de recuperação retoma com backoff) e falha permanente (exige re-disparo manual
    /// com justificativa). Não substitui o estado canônico; apenas orienta o operador na interface.</summary>
    private static string DescribeSituation(string status, string? lastError, int attempts) => status switch
    {
        "PENDING_MODEL" => "MODEL_MISSING",
        "DEAD_LETTERED" => "PERMANENT",
        "AMBIGUOUS" => "AMBIGUOUS",
        "STARTED" => "PROCESSING",
        "RESOLVED" => "RESOLVED",
        "SKIPPED_NO_ACTOR" => "NO_ACTOR",
        "PENDING" when (lastError != null || attempts > 0) => "TRANSIENT",
        "PENDING" => "WAITING",
        _ => "UNKNOWN"
    };

    private sealed class EventIntentListRow
    {
        public Guid Id { get; set; }
        public string ProcessCode { get; set; } = string.Empty;
        public string OriginType { get; set; } = string.Empty;
        public Guid OriginId { get; set; }
        public string Status { get; set; } = string.Empty;
        public Guid? RunId { get; set; }
        public string? RunNumber { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public int Attempts { get; set; }
        public string? LastError { get; set; }
        public string? ErrorCode { get; set; }
        public DateTime NextAttemptAt { get; set; }
    }

    private sealed record IntentRow(
        Guid Id,
        string ProcessCode,
        string OriginType,
        Guid OriginId,
        string Status,
        Guid? RunId,
        string RequestHash,
        string? Notes);

    private sealed record RedispatchRow(
        Guid Id,
        string Status,
        int Attempts,
        Guid? ActorId,
        string ProcessCode,
        string OriginType,
        Guid OriginId);
}
