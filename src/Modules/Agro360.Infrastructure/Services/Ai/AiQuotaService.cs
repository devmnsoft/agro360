using System;
using System.Threading;
using System.Threading.Tasks;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Dapper;

namespace Agro360.Infrastructure.Services.Ai;

public interface IAiQuotaService
{
    Task<bool> HasQuotaAsync(Guid tenantId, string useCase, CancellationToken ct = default);
    Task DeductQuotaAsync(Guid tenantId, int tokens, string useCase, CancellationToken ct = default);
}

public sealed class AiQuotaService(DatabaseExecutor database) : IAiQuotaService
{
    public async Task<bool> HasQuotaAsync(Guid tenantId, string useCase, CancellationToken ct = default)
    {
        return await database.InTenantTransactionAsync(async (conn, tx) =>
        {
            var limit = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                """
                select coalesce(sum(q.max_tokens), 0)
                from agro360.tenant_ai_quotas q
                where q.tenant_id = @TenantId and q.use_case = @UseCase and q.active = true
                """,
                new { TenantId = tenantId, UseCase = useCase },
                tx,
                cancellationToken: ct));

            var consumed = await conn.ExecuteScalarAsync<long>(new CommandDefinition(
                """
                select coalesce(sum(prompt_tokens + completion_tokens), 0)
                from agro360.ai_executions
                where tenant_id = @TenantId and use_case = @UseCase
                """,
                new { TenantId = tenantId, UseCase = useCase },
                tx,
                cancellationToken: ct));

            return consumed < limit;
        }, ct);
    }

    public async Task DeductQuotaAsync(Guid tenantId, int tokens, string useCase, CancellationToken ct = default)
    {
        // A dedução ocorre via registro na tabela ai_executions.
        // Este método serve para validações adicionais de teto hard-limit se necessário.
    }
}
