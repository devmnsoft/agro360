using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;

namespace Agro360.Infrastructure.Services;

public interface IPostingService
{
    Task PostMaterialCostAsync(Guid tenantId, Guid farmId, Guid materialId, decimal quantity, decimal unitCost, string sourceDocument, string description, DateTimeOffset competenceDate, CancellationToken ct);
    Task PostActivityCostAsync(Guid tenantId, Guid farmId, Guid orderId, string description, decimal amount, DateTimeOffset competenceDate, CancellationToken ct);
}

public sealed class PostingService(DatabaseExecutor database, ITenantContext tenant) : IPostingService
{
    public async Task PostMaterialCostAsync(Guid tenantId, Guid farmId, Guid materialId, decimal quantity, decimal unitCost, string sourceDocument, string description, DateTimeOffset competenceDate, CancellationToken ct)
    {
        var recognizedAmount = quantity * unitCost;
        if (recognizedAmount == 0) return;

        await database.InTenantTransactionAsync(async (conn, tx) =>
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                insert into agro360.cost_management_entries(
                    id, tenant_id, farm_id, category, competence_date,
                    recognized_amount, currency, source_type, source_key,
                    source_document, description, status, created_by
                ) values(
                    @Id, @TenantId, @FarmId, 'MATERIAL', @CompetenceDate,
                    @Amount, 'BRL', 'MATERIAL', @SourceKey,
                    @SourceDocument, @Description, 'OPEN', @UserId
                )
                """, new
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                FarmId = farmId,
                CompetenceDate = competenceDate,
                Amount = recognizedAmount,
                SourceKey = materialId,
                SourceDocument = sourceDocument,
                Description = description,
                UserId = tenant.UserId
            }, tx, cancellationToken: ct));
        }, ct);
    }

    public async Task PostActivityCostAsync(Guid tenantId, Guid farmId, Guid orderId, string description, decimal amount, DateTimeOffset competenceDate, CancellationToken ct)
    {
        if (amount == 0) return;

        await database.InTenantTransactionAsync(async (conn, tx) =>
        {
            await conn.ExecuteAsync(new CommandDefinition("""
                insert into agro360.cost_management_entries(
                    id, tenant_id, farm_id, category, competence_date,
                    recognized_amount, currency, source_type, source_key,
                    source_document, description, status, created_by
                ) values(
                    @Id, @TenantId, @FarmId, 'ACTIVITY', @CompetenceDate,
                    @Amount, 'BRL', 'ACTIVITY', @SourceKey,
                    null, @Description, 'OPEN', @UserId
                )
                """, new
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                FarmId = farmId,
                CompetenceDate = competenceDate,
                Amount = amount,
                SourceKey = orderId,
                Description = description,
                UserId = tenant.UserId
            }, tx, cancellationToken: ct));
        }, ct);
    }
}
