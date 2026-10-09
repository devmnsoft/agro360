using System;
using System.Threading;
using System.Threading.Tasks;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Dapper;

namespace Agro360.Infrastructure.Services.Ai;

public record AiExecutionRecord(
    Guid Id,
    Guid TenantId,
    Guid UserId,
    string UseCase,
    string? IdempotencyKey,
    string? PayloadHash,
    int AttemptNumber,
    string Status,
    string? Provider,
    string? Model,
    int ReservedTokens,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    string TokenConfidence,
    TimeSpan Duration,
    string? ErrorMessage,
    DateTimeOffset OccurredAt,
    string? ResultPayload = null);

public interface IAiExecutionService
{
    Task<Guid> CreateReservationAsync(
        Guid tenantId,
        Guid userId,
        string useCase,
        int estimatedTokens,
        string? idempotencyKey,
        string? payloadHash = null,
        Guid? quotaId = null,
        CancellationToken ct = default);

    Task CompleteExecutionAsync(
        Guid executionId,
        string provider,
        string model,
        int promptTokens,
        int completionTokens,
        TimeSpan duration,
        string tokenConfidence,
        bool success,
        string? errorMessage,
        string? resultPayload = null,
        CancellationToken ct = default);

    Task ReleaseExecutionAsync(Guid executionId, string reason, CancellationToken ct = default);

    Task<AiExecutionRecord?> GetExecutionByIdempotencyAsync(Guid tenantId, string useCase, string idempotencyKey, CancellationToken ct = default);

    Task RegisterExecutionAsync(
        Guid tenantId,
        Guid userId,
        string provider,
        string model,
        int promptTokens,
        int completionTokens,
        TimeSpan duration,
        string useCase,
        bool success,
        CancellationToken ct = default);

    Task<int> CleanupAbandonedReservationsAsync(TimeSpan olderThan, CancellationToken ct = default);
}

public sealed class AiExecutionService(DatabaseExecutor database) : IAiExecutionService
{
    public async Task<Guid> CreateReservationAsync(
        Guid tenantId,
        Guid userId,
        string useCase,
        int estimatedTokens,
        string? idempotencyKey,
        string? payloadHash = null,
        Guid? quotaId = null,
        CancellationToken ct = default)
    {
        return await database.InTenantTransactionAsync(async (conn, tx) =>
        {
            var id = Guid.CreateVersion7();
            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.ai_executions
                    (id, tenant_id, user_id, use_case, quota_id, idempotency_key, payload_hash, attempt_number, status,
                     reserved_tokens, prompt_tokens, completion_tokens, total_tokens, token_confidence, duration_ms, occurred_at, created_at, updated_at)
                values
                    (@Id, @TenantId, @UserId, @UseCase, @QuotaId, @IdempotencyKey, @PayloadHash,
                     (select coalesce(max(e.attempt_number), 0) + 1
                        from agro360.ai_executions e
                       where e.tenant_id = @TenantId and e.use_case = @UseCase and e.idempotency_key = @IdempotencyKey),
                     'RESERVED',
                     @EstimatedTokens, 0, 0, 0, 'ESTIMATED', 0, now(), now(), now())
                """,
                new
                {
                    Id = id,
                    TenantId = tenantId,
                    UserId = userId,
                    UseCase = useCase,
                    QuotaId = quotaId,
                    IdempotencyKey = idempotencyKey,
                    PayloadHash = payloadHash,
                    EstimatedTokens = estimatedTokens
                },
                tx,
                cancellationToken: ct));

            return id;
        }, ct);
    }

    public async Task CompleteExecutionAsync(
        Guid executionId,
        string provider,
        string model,
        int promptTokens,
        int completionTokens,
        TimeSpan duration,
        string tokenConfidence,
        bool success,
        string? errorMessage,
        string? resultPayload = null,
        CancellationToken ct = default)
    {
        await database.InTenantTransactionAsync(async (conn, tx) =>
        {
            var status = success ? "COMPLETED" : "FAILED";
            var totalTokens = promptTokens + completionTokens;

            await conn.ExecuteAsync(new CommandDefinition(
                """
                update agro360.ai_executions
                set status = @Status,
                    provider = @Provider,
                    model = @Model,
                    prompt_tokens = @PromptTokens,
                    completion_tokens = @CompletionTokens,
                    total_tokens = @TotalTokens,
                    token_confidence = @TokenConfidence,
                    duration_ms = @DurationMs,
                    error_message = @ErrorMessage,
                    result_payload = coalesce(@ResultPayload::jsonb, result_payload),
                    updated_at = now()
                where id = @ExecutionId and status in ('RESERVED', 'IN_PROGRESS')
                """,
                new
                {
                    ExecutionId = executionId,
                    Status = status,
                    Provider = provider,
                    Model = model,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    TotalTokens = totalTokens,
                    TokenConfidence = tokenConfidence,
                    DurationMs = duration.TotalMilliseconds,
                    ErrorMessage = errorMessage,
                    ResultPayload = resultPayload
                },
                tx,
                cancellationToken: ct));
        }, ct);
    }

    public async Task ReleaseExecutionAsync(Guid executionId, string reason, CancellationToken ct = default)
    {
        await database.InTenantTransactionAsync(async (conn, tx) =>
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                update agro360.ai_executions
                set status = 'RELEASED',
                    error_message = coalesce(error_message, @Reason),
                    updated_at = now()
                where id = @ExecutionId and status in ('RESERVED', 'IN_PROGRESS')
                """,
                new { ExecutionId = executionId, Reason = reason },
                tx,
                cancellationToken: ct));
        }, ct);
    }

    public async Task<AiExecutionRecord?> GetExecutionByIdempotencyAsync(Guid tenantId, string useCase, string idempotencyKey, CancellationToken ct = default)
    {
        return await database.InTenantTransactionAsync(async (conn, tx) =>
        {
            var row = await conn.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition(
                """
                select id, tenant_id as tenantid, user_id as userid, use_case as usecase,
                       idempotency_key as idempotencykey, payload_hash as payloadhash, attempt_number as attemptnumber,
                       status, provider, model, reserved_tokens as reservedtokens,
                       prompt_tokens as prompttokens, completion_tokens as completiontokens,
                       total_tokens as totaltokens, token_confidence as tokenconfidence,
                       duration_ms as durationms, error_message as errormessage, occurred_at as occurredat,
                       (result_payload)::text as resultpayload
                from agro360.ai_executions
                where tenant_id = @TenantId and use_case = @UseCase and idempotency_key = @IdempotencyKey
                order by occurred_at desc
                limit 1
                """,
                new { TenantId = tenantId, UseCase = useCase, IdempotencyKey = idempotencyKey },
                tx,
                cancellationToken: ct));

            if (row == null) return null;

            return new AiExecutionRecord(
                Id: (Guid)row.id,
                TenantId: (Guid)row.tenantid,
                UserId: (Guid)row.userid,
                UseCase: (string)row.usecase,
                IdempotencyKey: (string?)row.idempotencykey,
                PayloadHash: (string?)row.payloadhash,
                AttemptNumber: (int)row.attemptnumber,
                Status: (string)row.status,
                Provider: (string?)row.provider,
                Model: (string?)row.model,
                ReservedTokens: (int)row.reservedtokens,
                PromptTokens: (int)row.prompttokens,
                CompletionTokens: (int)row.completiontokens,
                TotalTokens: (int)row.totaltokens,
                TokenConfidence: (string)row.tokenconfidence,
                Duration: TimeSpan.FromMilliseconds((double)(decimal)row.durationms),
                ErrorMessage: (string?)row.errormessage,
                OccurredAt: (DateTimeOffset)row.occurredat,
                ResultPayload: (string?)row.resultpayload);
        }, ct);
    }

    public async Task RegisterExecutionAsync(
        Guid tenantId,
        Guid userId,
        string provider,
        string model,
        int promptTokens,
        int completionTokens,
        TimeSpan duration,
        string useCase,
        bool success,
        CancellationToken ct = default)
    {
        await database.InTenantTransactionAsync(async (conn, tx) =>
        {
            var id = Guid.CreateVersion7();
            var totalTokens = promptTokens + completionTokens;
            var status = success ? "COMPLETED" : "FAILED";

            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.ai_executions
                    (id, tenant_id, user_id, use_case, attempt_number, status, provider, model,
                     reserved_tokens, prompt_tokens, completion_tokens, total_tokens, token_confidence, duration_ms, occurred_at, created_at, updated_at)
                values
                    (@Id, @TenantId, @UserId, @UseCase, 1, @Status, @Provider, @Model,
                     @TotalTokens, @PromptTokens, @CompletionTokens, @TotalTokens, 'EXACT', @DurationMs, now(), now(), now())
                """,
                new
                {
                    Id = id,
                    TenantId = tenantId,
                    UserId = userId,
                    UseCase = useCase,
                    Status = status,
                    Provider = provider,
                    Model = model,
                    PromptTokens = promptTokens,
                    CompletionTokens = completionTokens,
                    TotalTokens = totalTokens,
                    DurationMs = duration.TotalMilliseconds
                },
                tx,
                cancellationToken: ct));
        }, ct);
    }

    public async Task<int> CleanupAbandonedReservationsAsync(TimeSpan olderThan, CancellationToken ct = default)
    {
        return await database.InTenantTransactionAsync(async (conn, tx) =>
        {
            var cutoff = DateTimeOffset.UtcNow.Subtract(olderThan);

            var stale = (await conn.QueryAsync<dynamic>(new CommandDefinition(
                """
                select id, tenant_id as tenantid, use_case as usecase, reserved_tokens as reservedtokens,
                       quota_id as quotaid
                from agro360.ai_executions
                where status in ('RESERVED', 'IN_PROGRESS')
                  and created_at < @Cutoff
                for update
                """,
                new { Cutoff = cutoff },
                tx,
                cancellationToken: ct))).ToList();

            if (stale.Count == 0) return 0;

            foreach (var s in stale)
            {
                var execId = (Guid)s.id;
                var tenantId = (Guid)s.tenantid;
                var useCase = (string)s.usecase;
                int reserved = (int)s.reservedtokens;
                object? quotaRaw = s.quotaid;

                // Baixa vinculada à cota que originou a reserva (ai_executions.quota_id). Sem greatest():
                // inconsistência de saldo deve estourar o check >= 0 em vez de ser mascarada.
                if (quotaRaw is Guid boundQuotaId)
                {
                    await conn.ExecuteAsync(new CommandDefinition(
                        """
                        update agro360.tenant_ai_quotas
                        set reserved_tokens = reserved_tokens - @ReservedTokens,
                            updated_at = now()
                        where id = @QuotaId
                        """,
                        new { QuotaId = boundQuotaId, ReservedTokens = reserved },
                        tx,
                        cancellationToken: ct));
                }
                else
                {
                    // Fallback para reservas anteriores ao vínculo por quota_id: compensa na cota ativa do período
                    // corrente com clamp honesto (o saldo pode não conter esta reserva).
                    await conn.ExecuteAsync(new CommandDefinition(
                        """
                        update agro360.tenant_ai_quotas
                        set reserved_tokens = reserved_tokens - least(reserved_tokens, @ReservedTokens),
                            updated_at = now()
                        where tenant_id = @TenantId and use_case = @UseCase and active = true
                          and current_date between period_start and period_end
                        """,
                        new { TenantId = tenantId, UseCase = useCase, ReservedTokens = reserved },
                        tx,
                        cancellationToken: ct));
                }

                // Marca execução como liberada
                await conn.ExecuteAsync(new CommandDefinition(
                    """
                    update agro360.ai_executions
                    set status = 'RELEASED',
                        error_message = 'Reserva abandonada liberada por tempo limite excedido',
                        updated_at = now()
                    where id = @ExecutionId
                    """,
                    new { ExecutionId = execId },
                    tx,
                    cancellationToken: ct));
            }

            return stale.Count;
        }, ct);
    }
}
