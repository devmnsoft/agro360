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
    DateTimeOffset OccurredAt);

public interface IAiExecutionService
{
    Task<Guid> CreateReservationAsync(
        Guid tenantId,
        Guid userId,
        string useCase,
        int estimatedTokens,
        string? idempotencyKey,
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
}

public sealed class AiExecutionService(DatabaseExecutor database) : IAiExecutionService
{
    public async Task<Guid> CreateReservationAsync(
        Guid tenantId,
        Guid userId,
        string useCase,
        int estimatedTokens,
        string? idempotencyKey,
        CancellationToken ct = default)
    {
        return await database.InTenantTransactionAsync(async (conn, tx) =>
        {
            var id = Guid.CreateVersion7();
            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.ai_executions
                    (id, tenant_id, user_id, use_case, idempotency_key, attempt_number, status,
                     reserved_tokens, prompt_tokens, completion_tokens, total_tokens, token_confidence, duration_ms, occurred_at, created_at, updated_at)
                values
                    (@Id, @TenantId, @UserId, @UseCase, @IdempotencyKey, 1, 'RESERVED',
                     @EstimatedTokens, 0, 0, 0, 'ESTIMATED', 0, now(), now(), now())
                """,
                new
                {
                    Id = id,
                    TenantId = tenantId,
                    UserId = userId,
                    UseCase = useCase,
                    IdempotencyKey = idempotencyKey,
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
                    ErrorMessage = errorMessage
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
                       idempotency_key as idempotencykey, attempt_number as attemptnumber,
                       status, provider, model, reserved_tokens as reservedtokens,
                       prompt_tokens as prompttokens, completion_tokens as completiontokens,
                       total_tokens as totaltokens, token_confidence as tokenconfidence,
                       duration_ms as durationms, error_message as errormessage, occurred_at as occurredat
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
                OccurredAt: (DateTimeOffset)row.occurredat);
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
}
