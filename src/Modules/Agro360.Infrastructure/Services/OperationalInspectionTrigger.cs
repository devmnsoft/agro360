using System.Security.Cryptography;
using System.Text;
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
    IInspectionService inspectionService,
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
        var key = string.IsNullOrWhiteSpace(request.CustomIdempotencyKey)
            ? $"EVT:{processUpper}:{request.OriginId:N}"
            : request.CustomIdempotencyKey.Trim();
        var hash = request.RequestHash ?? ComputeHash(processUpper, originType, request.OriginId, request.ProductId, request.LotId, request.UnitId);

        Guid intentId = Guid.Empty;
        OperationalInspectionEventResult? existingTerminal = null;

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
                if (existing.Status is "STARTED" or "AMBIGUOUS" or "PENDING_MODEL" or "SKIPPED_NO_ACTOR")
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

        if (existingTerminal is not null)
            return existingTerminal;

        // Se não houver operador autenticado no contexto
        if (tenant.UserId == Guid.Empty)
        {
            const string noActorMsg = "Nenhum operador/usuário autenticado para executar a inspeção automática.";
            await UpdateIntentStatusAsync(intentId, "SKIPPED_NO_ACTOR", null, noActorMsg, ct);
            return new OperationalInspectionEventResult(
                intentId,
                processUpper,
                originType,
                request.OriginId,
                "SKIPPED_NO_ACTOR",
                null,
                noActorMsg);
        }

        // Tenta acionar StartRunAsync no InspectionService
        try
        {
            var startCommand = new InspectionStartRunCommand(
                ModelVersionId: null,
                ProcessCode: processUpper,
                OriginType: originType,
                OriginId: request.OriginId,
                ProductId: request.ProductId,
                LotId: request.LotId,
                UnitId: request.UnitId,
                InspectorId: tenant.UserId,
                SelectionMode: "AUTOMATIC",
                IdempotencyKey: key);

            var runId = await inspectionService.StartRunAsync(startCommand, ct);
            const string startedMsg = "Inspeção iniciada automaticamente a partir do evento de origem.";
            await UpdateIntentStatusAsync(intentId, "STARTED", runId, startedMsg, ct);
            return new OperationalInspectionEventResult(
                intentId,
                processUpper,
                originType,
                request.OriginId,
                "STARTED",
                runId,
                startedMsg);
        }
        catch (ConflictException cex) when (cex.Code == "inspection.ambiguous_model")
        {
            await UpdateIntentStatusAsync(intentId, "AMBIGUOUS", null, cex.Message, ct);
            return new OperationalInspectionEventResult(
                intentId,
                processUpper,
                originType,
                request.OriginId,
                "AMBIGUOUS",
                null,
                cex.Message);
        }
        catch (InvalidOperationException iex)
        {
            await UpdateIntentStatusAsync(intentId, "PENDING_MODEL", null, iex.Message, ct);
            return new OperationalInspectionEventResult(
                intentId,
                processUpper,
                originType,
                request.OriginId,
                "PENDING_MODEL",
                null,
                iex.Message);
        }
        catch (Exception ex)
        {
            InfrastructureLogMessages.OperationalInspectionTriggerFailed(logger, processUpper, request.OriginId, ex);
            var errorMsg = $"Falha técnica: {ex.Message}";
            await UpdateIntentStatusAsync(intentId, "PENDING", null, errorMsg, ct);
            return new OperationalInspectionEventResult(
                intentId,
                processUpper,
                originType,
                request.OriginId,
                "PENDING",
                null,
                errorMsg);
        }
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
                       i.created_at CreatedAt, i.updated_at UpdatedAt
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
                new DateTimeOffset(DateTime.SpecifyKind(r.UpdatedAt, DateTimeKind.Utc))
            )).ToArray();
        }, ct);

    private Task UpdateIntentStatusAsync(Guid id, string status, Guid? runId, string? notes, CancellationToken ct) =>
        db.InTenantTransactionAsync(async (c, t) =>
        {
            await c.ExecuteAsync(new CommandDefinition("""
                update agro360.quality_inspection_event_intents
                set status = @Status,
                    run_id = coalesce(@RunId, run_id),
                    notes = @Notes,
                    updated_at = now(),
                    updated_by = @UserId
                where tenant_id = @TenantId and id = @Id
                """, new
            {
                tenant.TenantId,
                Id = id,
                Status = status,
                RunId = runId,
                Notes = notes,
                UserId = tenant.UserId == Guid.Empty ? (Guid?)null : tenant.UserId
            }, t, cancellationToken: ct));
        }, ct);

    private static string ComputeHash(string process, string originType, Guid originId, Guid? productId, Guid? lotId, Guid? unitId)
    {
        var raw = $"{process}|{originType}|{originId:N}|{productId?.ToString("N")}|{lotId?.ToString("N")}|{unitId?.ToString("N")}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
}
