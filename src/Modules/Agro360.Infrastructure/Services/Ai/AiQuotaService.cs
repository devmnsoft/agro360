using System;
using System.Threading;
using System.Threading.Tasks;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;

namespace Agro360.Infrastructure.Services.Ai;

public record AiReservation(
    Guid ExecutionId,
    Guid TenantId,
    string UseCase,
    int ReservedTokens,
    string? IdempotencyKey,
    bool IsReplay = false,
    AiExecutionRecord? ReplayRecord = null);

public interface IAiQuotaService
{
    Task<AiReservation> ReserveQuotaAsync(
        Guid tenantId,
        Guid userId,
        string useCase,
        int estimatedTokens,
        string? idempotencyKey = null,
        string? payloadHash = null,
        CancellationToken ct = default);

    Task ReconcileAndCompleteAsync(
        Guid executionId,
        string provider,
        string model,
        int promptTokens,
        int completionTokens,
        TimeSpan duration,
        string tokenConfidence = "EXACT",
        bool success = true,
        string? errorMessage = null,
        string? resultPayload = null,
        CancellationToken ct = default);

    Task ReleaseReservationAsync(Guid executionId, string reason, CancellationToken ct = default);

    Task<bool> HasQuotaAsync(Guid tenantId, string useCase, CancellationToken ct = default);

    Task DeductQuotaAsync(Guid tenantId, int tokens, string useCase, CancellationToken ct = default);

    Task<int> CleanupAbandonedReservationsAsync(TimeSpan olderThan, CancellationToken ct = default);
}

public sealed class AiQuotaService(
    DatabaseExecutor database,
    IAiExecutionService executionService) : IAiQuotaService
{
    private sealed record QuotaRow(Guid Id, long MaxTokens, long ReservedTokens, long ConsumedTokens);
    private sealed record ExecutionRow(Guid Id, Guid TenantId, string UseCase, int ReservedTokens, string Status, Guid? QuotaId);

    public async Task<AiReservation> ReserveQuotaAsync(
        Guid tenantId,
        Guid userId,
        string useCase,
        int estimatedTokens,
        string? idempotencyKey = null,
        string? payloadHash = null,
        CancellationToken ct = default)
    {
        if (estimatedTokens <= 0) estimatedTokens = 1000;

        return await database.InTenantTransactionAsync(async (conn, tx) =>
        {
            // 1. Verificar idempotência se houver chave
            if (!string.IsNullOrWhiteSpace(idempotencyKey))
            {
                var existing = await executionService.GetExecutionByIdempotencyAsync(tenantId, useCase, idempotencyKey, ct);
                if (existing != null)
                {
                    if (!string.IsNullOrEmpty(existing.PayloadHash) &&
                        !string.IsNullOrEmpty(payloadHash) &&
                        !string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
                    {
                        throw new DomainException(
                            "A chave de idempotência fornecida já foi utilizada com uma consulta ou parâmetros diferentes.",
                            "ai.idempotency.payload_mismatch");
                    }

                    if (existing.Status == "COMPLETED")
                    {
                        return new AiReservation(
                            ExecutionId: existing.Id,
                            TenantId: tenantId,
                            UseCase: useCase,
                            ReservedTokens: 0,
                            IdempotencyKey: idempotencyKey,
                            IsReplay: true,
                            ReplayRecord: existing);
                    }
                }
            }

            // 2. Lock pessimista da quota vigente da competência
            var quota = await conn.QuerySingleOrDefaultAsync<QuotaRow>(new CommandDefinition(
                """
                select id as Id, max_tokens as MaxTokens, reserved_tokens as ReservedTokens, consumed_tokens as ConsumedTokens
                from agro360.tenant_ai_quotas
                where tenant_id = @TenantId
                  and use_case = @UseCase
                  and active = true
                  and current_date between period_start and period_end
                order by period_end desc
                limit 1
                for update
                """,
                new { TenantId = tenantId, UseCase = useCase },
                tx,
                cancellationToken: ct));

            if (quota == null)
            {
                throw new DomainException(
                    $"Nenhuma cota de IA ativa ou contratada encontrada para o caso de uso '{useCase}'.",
                    "ai.quota.missing_or_inactive");
            }

            if (quota.ConsumedTokens + quota.ReservedTokens + estimatedTokens > quota.MaxTokens)
            {
                throw new DomainException(
                    $"Limite de cota de IA excedido para este período. Disponível: {Math.Max(0, quota.MaxTokens - (quota.ConsumedTokens + quota.ReservedTokens))} tokens.",
                    "ai.quota.exceeded");
            }

            // 3. Atualizar reserva na cota
            await conn.ExecuteAsync(new CommandDefinition(
                """
                update agro360.tenant_ai_quotas
                set reserved_tokens = reserved_tokens + @EstimatedTokens,
                    updated_at = now()
                where id = @QuotaId
                """,
                new { QuotaId = quota.Id, EstimatedTokens = estimatedTokens },
                tx,
                cancellationToken: ct));

            // 4. Criar registro de execução no status RESERVED, vinculado à cota reservada (quota_id): a
            // baixa/reconciliação futuros atingem exatamente o período que autorizou esta reserva.
            var executionId = await executionService.CreateReservationAsync(
                tenantId,
                userId,
                useCase,
                estimatedTokens,
                idempotencyKey,
                payloadHash,
                quota.Id,
                ct);

            return new AiReservation(
                ExecutionId: executionId,
                TenantId: tenantId,
                UseCase: useCase,
                ReservedTokens: estimatedTokens,
                IdempotencyKey: idempotencyKey,
                IsReplay: false);
        }, ct);
    }

    public async Task ReconcileAndCompleteAsync(
        Guid executionId,
        string provider,
        string model,
        int promptTokens,
        int completionTokens,
        TimeSpan duration,
        string tokenConfidence = "EXACT",
        bool success = true,
        string? errorMessage = null,
        string? resultPayload = null,
        CancellationToken ct = default)
    {
        await database.InTenantTransactionAsync(async (conn, tx) =>
        {
            var exec = await conn.QuerySingleOrDefaultAsync<ExecutionRow>(new CommandDefinition(
                """
                select id as Id, tenant_id as TenantId, use_case as UseCase, reserved_tokens as ReservedTokens, status as Status,
                       quota_id as QuotaId
                from agro360.ai_executions
                where id = @ExecutionId
                for update
                """,
                new { ExecutionId = executionId },
                tx,
                cancellationToken: ct));

            if (exec == null || exec.Status is "COMPLETED" or "RELEASED") return;

            int actualTokens = promptTokens + completionTokens;

            // Débito vinculado à cota que originou a reserva (ai_executions.quota_id): a baixa do período exato
            // não vaza para a cota da competência corrente. Sem greatest(): o guard de status garante passe
            // único e inconsistência de saldo deve estourar o check >= 0 em vez de ser mascarada.
            if (exec.QuotaId.HasValue)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    update agro360.tenant_ai_quotas
                    set reserved_tokens = reserved_tokens - @ReservedTokens,
                        consumed_tokens = consumed_tokens + @ActualTokens,
                        updated_at = now()
                    where id = @QuotaId
                    """,
                    new { QuotaId = exec.QuotaId.Value, ReservedTokens = exec.ReservedTokens, ActualTokens = actualTokens },
                    tx,
                    cancellationToken: ct));
            }
            else
            {
                // Fallback para reservas anteriores ao vínculo por quota_id: compensa a cota ativa do período
                // corrente com clamp honesto (o saldo pode não conter esta reserva).
                var legacyQuota = await conn.QuerySingleOrDefaultAsync<Guid>(new CommandDefinition(
                    """
                    select id
                    from agro360.tenant_ai_quotas
                    where tenant_id = @TenantId
                      and use_case = @UseCase
                      and active = true
                      and current_date between period_start and period_end
                    order by period_end desc
                    limit 1
                    for update
                    """,
                    new { exec.TenantId, exec.UseCase },
                    tx,
                    cancellationToken: ct));

                if (legacyQuota != Guid.Empty)
                {
                    await conn.ExecuteAsync(new CommandDefinition(
                        """
                        update agro360.tenant_ai_quotas
                        set reserved_tokens = reserved_tokens - least(reserved_tokens, @ReservedTokens),
                            consumed_tokens = consumed_tokens + @ActualTokens,
                            updated_at = now()
                        where id = @QuotaId
                        """,
                        new { QuotaId = legacyQuota, ReservedTokens = exec.ReservedTokens, ActualTokens = actualTokens },
                        tx,
                        cancellationToken: ct));
                }
            }

            // Finalizar execução
            await executionService.CompleteExecutionAsync(
                executionId,
                provider,
                model,
                promptTokens,
                completionTokens,
                duration,
                tokenConfidence,
                success,
                errorMessage,
                resultPayload,
                ct);
        }, ct);
    }

    public async Task ReleaseReservationAsync(Guid executionId, string reason, CancellationToken ct = default)
    {
        await database.InTenantTransactionAsync(async (conn, tx) =>
        {
            var exec = await conn.QuerySingleOrDefaultAsync<ExecutionRow>(new CommandDefinition(
                """
                select id as Id, tenant_id as TenantId, use_case as UseCase, reserved_tokens as ReservedTokens, status as Status,
                       quota_id as QuotaId
                from agro360.ai_executions
                where id = @ExecutionId
                for update
                """,
                new { ExecutionId = executionId },
                tx,
                cancellationToken: ct));

            if (exec == null || exec.Status is "COMPLETED" or "RELEASED") return;

            // Liberação vinculada à cota reservada (quota_id); fallback legado com clamp honesto.
            if (exec.QuotaId.HasValue)
            {
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    update agro360.tenant_ai_quotas
                    set reserved_tokens = reserved_tokens - @ReservedTokens,
                        updated_at = now()
                    where id = @QuotaId
                    """,
                    new { QuotaId = exec.QuotaId.Value, ReservedTokens = exec.ReservedTokens },
                    tx,
                    cancellationToken: ct));
            }
            else
            {
                var legacyQuota = await conn.QuerySingleOrDefaultAsync<Guid>(new CommandDefinition(
                    """
                    select id
                    from agro360.tenant_ai_quotas
                    where tenant_id = @TenantId
                      and use_case = @UseCase
                      and active = true
                      and current_date between period_start and period_end
                    order by period_end desc
                    limit 1
                    for update
                    """,
                    new { exec.TenantId, exec.UseCase },
                    tx,
                    cancellationToken: ct));

                if (legacyQuota != Guid.Empty)
                {
                    await conn.ExecuteAsync(new CommandDefinition(
                        """
                        update agro360.tenant_ai_quotas
                        set reserved_tokens = reserved_tokens - least(reserved_tokens, @ReservedTokens),
                            updated_at = now()
                        where id = @QuotaId
                        """,
                        new { QuotaId = legacyQuota, ReservedTokens = exec.ReservedTokens },
                        tx,
                        cancellationToken: ct));
                }
            }

            await executionService.ReleaseExecutionAsync(executionId, reason, ct);
        }, ct);
    }

    public async Task<bool> HasQuotaAsync(Guid tenantId, string useCase, CancellationToken ct = default)
    {
        return await database.InTenantTransactionAsync(async (conn, tx) =>
        {
            var quota = await conn.QuerySingleOrDefaultAsync<QuotaRow>(new CommandDefinition(
                """
                select id as Id, max_tokens as MaxTokens, reserved_tokens as ReservedTokens, consumed_tokens as ConsumedTokens
                from agro360.tenant_ai_quotas
                where tenant_id = @TenantId
                  and use_case = @UseCase
                  and active = true
                  and current_date between period_start and period_end
                order by period_end desc
                limit 1
                """,
                new { TenantId = tenantId, UseCase = useCase },
                tx,
                cancellationToken: ct));

            if (quota == null) return false;

            return (quota.ReservedTokens + quota.ConsumedTokens) < quota.MaxTokens;
        }, ct);
    }

    public async Task DeductQuotaAsync(Guid tenantId, int tokens, string useCase, CancellationToken ct = default)
    {
        if (tokens <= 0) return;

        await database.InTenantTransactionAsync(async (conn, tx) =>
        {
            var quota = await conn.QuerySingleOrDefaultAsync<QuotaRow>(new CommandDefinition(
                """
                select id as Id, max_tokens as MaxTokens, reserved_tokens as ReservedTokens, consumed_tokens as ConsumedTokens
                from agro360.tenant_ai_quotas
                where tenant_id = @TenantId
                  and use_case = @UseCase
                  and active = true
                  and current_date between period_start and period_end
                order by period_end desc
                limit 1
                for update
                """,
                new { TenantId = tenantId, UseCase = useCase },
                tx,
                cancellationToken: ct));

            if (quota == null)
            {
                throw new DomainException(
                    $"Nenhuma cota de IA ativa ou contratada encontrada para o caso de uso '{useCase}'.",
                    "ai.quota.missing_or_inactive");
            }

            await conn.ExecuteAsync(new CommandDefinition(
                """
                update agro360.tenant_ai_quotas
                set consumed_tokens = consumed_tokens + @Tokens,
                    updated_at = now()
                where id = @QuotaId
                """,
                new { QuotaId = quota.Id, Tokens = tokens },
                tx,
                cancellationToken: ct));
        }, ct);
    }

    public Task<int> CleanupAbandonedReservationsAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        return executionService.CleanupAbandonedReservationsAsync(olderThan, ct);
    }
}

