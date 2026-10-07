using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Agro360.Infrastructure.Services;

public interface ICycleVerificationService
{
    Task<VerificationResult> RunFullCycleAsync(CancellationToken ct = default);
}

public record VerificationStep(string Name, bool Success, string Detail);
public record VerificationResult(bool OverallSuccess, List<VerificationStep> Steps);

public sealed class CycleVerificationService(
    DatabaseExecutor db,
    ITenantContext tenant,
    IFieldOperationsService fieldOps,
    ISeasonCostService seasonCost,
    IIntelligenceService intelligence,
    ILogger<CycleVerificationService> logger) : ICycleVerificationService
{
    public async Task<VerificationResult> RunFullCycleAsync(CancellationToken ct = default)
    {
        var steps = new List<VerificationStep>();
        bool overall = true;

        try
        {
            // --- SETUP: Get some basic IDs for the test ---
            var farmId = tenant.FarmId ?? (await db.InTenantTransactionAsync(async (c, t) =>
                await c.QuerySingleAsync<Guid>("select id from agro360.geo_farms where tenant_id=@TenantId limit 1", new { tenant.TenantId }, t), ct));

            var seasonId = await db.InTenantTransactionAsync(async (c, t) =>
                await c.QuerySingleAsync<Guid>("select id from agro360.agriculture_seasons where tenant_id=@TenantId and status not in (4,5) limit 1", new { tenant.TenantId }, t), ct);

            var productId = await db.InTenantTransactionAsync(async (c, t) =>
                await c.QuerySingleAsync<Guid>("select id from agro360.inventory_products where tenant_id=@TenantId limit 1", new { tenant.TenantId }, t), ct);

            // 1. Field Operation: Create Work Order
            var orderId = Guid.CreateVersion7();
            await db.InTenantTransactionAsync(async (c, t) => {
                await c.ExecuteAsync(new CommandDefinition("""
                    insert into agro360.agriculture_records(id, tenant_id, module, status, data, created_at, created_by)
                    values(@Id, @TenantId, 'work-orders', 'IN_PROGRESS', @Data, now(), @UserId)
                    """, new { Id = orderId, tenant.TenantId, Data = System.Text.Json.JsonSerializer.Serialize(new { propertyId = farmId, name = "Cycle Test Order" }), tenant.UserId }, t));
            }, ct);
            steps.Add(new VerificationStep("Create Work Order", true, $"Created order {orderId}"));

            // 2. Add Material to Order
            await fieldOps.AddMaterialAsync(orderId, new FieldMaterialCommand(productId, Guid.Empty, "Unit", 10, 100.00m), ct);
            steps.Add(new VerificationStep("Add Material", true, "Added material to order"));

            // 3. Consume Material -> triggers Posting
            var materialId = await db.InTenantTransactionAsync(async (c, t) =>
                await c.QuerySingleAsync<Guid>("select id from agro360.field_work_order_materials where work_order_id=@Id", new { Id = orderId }, t), ct);

            await fieldOps.ApplyMaterialEventAsync(orderId, materialId, new FieldMaterialEventCommand("CONSUME", 5, "Test consumption", null, Guid.NewGuid().ToString()), ct);

            var costExists = await db.InTenantTransactionAsync(async (c, t) =>
                await c.ExecuteScalarAsync<bool>("select exists(select 1 from agro360.cost_management_entries where source_key=@Key and source_type='MATERIAL')", new { Key = materialId }, t), ct);

            steps.Add(new VerificationStep("Material Consumption Posting", costExists, costExists ? "Cost entry created for material" : "Cost entry NOT created"));
            if (!costExists) overall = false;

            // 4. Add Work Log
            await fieldOps.AddWorkLogAsync(orderId, new FieldWorkLogCommand(
                Guid.NewGuid(), Guid.Empty, "Stage 1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(2), 10, "Unit", 1, 0, 0, 0, "", "Test Log", "Ref1", Guid.NewGuid().ToString()), ct);
            steps.Add(new VerificationStep("Add Work Log", true, "Added work log"));

            // 5. Review Order -> triggers Posting
            await fieldOps.ReviewAsync(orderId, new FieldReviewCommand(1, "All good"), ct);

            var activityCostExists = await db.InTenantTransactionAsync(async (c, t) =>
                await c.ExecuteScalarAsync<bool>("select exists(select 1 from agro360.cost_management_entries where source_key=@Key and source_type='ACTIVITY')", new { Key = orderId }, t), ct);

            steps.Add(new VerificationStep("Activity Completion Posting", activityCostExists, activityCostExists ? "Cost entry created for activity" : "Cost entry NOT created"));
            if (!activityCostExists) overall = false;

            // 6. Cost Appropriation
            var entryId = await db.InTenantTransactionAsync(async (c, t) =>
                await c.QuerySingleAsync<Guid>("select id from agro360.cost_management_entries where source_key=@Key limit 1", new { Key = orderId }, t), ct);

            await seasonCost.ConfirmAsync(new ConfirmCostAllocationCommand(
                new CostAllocationPreviewCommand(entryId, 100.00m, "DIRECT", 1, new List<CostDestinationCommand> { new(seasonId, farmId, Guid.Empty, Guid.Empty, 100.00m, "Unit", 100) }),
                Guid.NewGuid().ToString(), "Verification allocation"), ct);

            var allocated = await db.InTenantTransactionAsync(async (c, t) =>
                await c.ExecuteScalarAsync<bool>("select exists(select 1 from agro360.cost_allocations where entry_id=@Id)", new { Id = entryId }, t), ct);

            steps.Add(new VerificationStep("Cost Appropriation", allocated, allocated ? "Cost appropriated to season" : "Allocation failed"));
            if (!allocated) overall = false;

            // 7. Indicator check
            var indicators = await intelligence.GetIndicatorsAsync(new IntelligenceFilter(DateOnly.FromDateTime(DateTime.UtcNow), null, farmId, null, null), ct);
            var costPerHa = indicators.FirstOrDefault(x => x.Code == "cost-per-hectare");

            steps.Add(new VerificationStep("Intelligence Indicator", costPerHa != null, costPerHa != null ? $"Value: {costPerHa.Value} {costPerHa.Unit}" : "Indicator not found"));

        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Cycle verification failed");
            steps.Add(new VerificationStep("Error", false, ex.Message));
            overall = false;
        }

        return new VerificationResult(overall, steps);
    }
}
