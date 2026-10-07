using System;
using System.Threading;
using System.Threading.Tasks;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Dapper;

namespace Agro360.Infrastructure.Services.Ai;

public interface IAiExecutionService
{
    Task RegisterExecutionAsync(AiExecutionRecord record, CancellationToken ct = default);
}

public record AiExecutionRecord(
    Guid TenantId,
    Guid UserId,
    string Provider,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    TimeSpan Duration,
    string UseCase,
    bool Success);

public sealed class AiExecutionService(DatabaseExecutor database) : IAiExecutionService
{
    public async Task RegisterExecutionAsync(AiExecutionRecord record, CancellationToken ct = default)
    {
        await database.InTenantTransactionAsync(async (conn, tx) =>
        {
            await conn.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.ai_executions
                    (id, tenant_id, user_id, provider, model, prompt_tokens, completion_tokens, duration_ms, use_case, success, occurred_at)
                values
                    (@Id, @TenantId, @UserId, @Provider, @Model, @PromptTokens, @CompletionTokens, @DurationMs, @UseCase, @Success, now());
                """,
                new
                {
                    Id = Guid.CreateVersion7(),
                    record.TenantId,
                    record.UserId,
                    record.Provider,
                    record.Model,
                    record.PromptTokens,
                    record.CompletionTokens,
                    DurationMs = record.Duration.TotalMilliseconds,
                    record.UseCase,
                    record.Success
                },
                tx,
                cancellationToken: ct));
        }, ct);
    }
}
