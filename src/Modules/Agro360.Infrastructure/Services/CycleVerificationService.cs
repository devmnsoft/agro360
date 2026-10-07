using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Agro360.Infrastructure.Services;

public interface ICycleVerificationService
{
    Task<VerificationResult> RunFullCycleAsync(CancellationToken ct = default);
}

public sealed record VerificationStep(string Name, bool Success, string Detail);
public sealed record VerificationResult(bool OverallSuccess, IReadOnlyList<VerificationStep> Steps);

public sealed class CycleVerificationService(
    DatabaseExecutor db,
    ITenantContext tenant,
    IFieldOperationsService fieldOps,
    ISeasonCostService seasonCost,
    IIntelligenceService intelligence,
    IHostEnvironment environment,
    ILogger<CycleVerificationService> logger) : ICycleVerificationService
{
    public async Task<VerificationResult> RunFullCycleAsync(CancellationToken ct = default)
    {
        var steps = new List<VerificationStep>();
        bool overall = true;

        if (environment.IsProduction())
        {
            throw new InvalidOperationException("A verificação de ciclo automatizada não pode ser executada em ambiente de Produção.");
        }

        if (!tenant.IsAvailable || tenant.TenantId == Guid.Empty)
        {
            throw new InvalidOperationException("Contexto de tenant ausente para verificação.");
        }

        try
        {
            // 1. Pré-condições determinísticas para o tenant de teste
            var farm = await db.InTenantTransactionAsync(async (c, t) =>
                await c.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition(
                    "select id, name from agro360.geo_farms where tenant_id=@TenantId and deleted_at is null order by code asc limit 1",
                    new { tenant.TenantId }, t, cancellationToken: ct)), ct);

            if (farm == null)
            {
                steps.Add(new VerificationStep("Fixture Farm", false, "Nenhuma fazenda encontrada no tenant de teste."));
                return new VerificationResult(false, steps);
            }
            Guid farmId = (Guid)farm.id;
            steps.Add(new VerificationStep("Fixture Farm", true, $"Fazenda determinística: {farm.name} ({farmId})"));

            var season = await db.InTenantTransactionAsync(async (c, t) =>
                await c.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition(
                    "select id, name from agro360.agriculture_seasons where tenant_id=@TenantId and status not in (4,5) order by name asc limit 1",
                    new { tenant.TenantId }, t, cancellationToken: ct)), ct);

            if (season == null)
            {
                steps.Add(new VerificationStep("Fixture Season", false, "Nenhuma safra ativa encontrada no tenant de teste."));
                return new VerificationResult(false, steps);
            }
            Guid seasonId = (Guid)season.id;
            steps.Add(new VerificationStep("Fixture Season", true, $"Safra determinística: {season.name} ({seasonId})"));

            var product = await db.InTenantTransactionAsync(async (c, t) =>
                await c.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition(
                    "select id, name, sku from agro360.inventory_products where tenant_id=@TenantId and deleted_at is null order by name asc limit 1",
                    new { tenant.TenantId }, t, cancellationToken: ct)), ct);

            if (product == null)
            {
                steps.Add(new VerificationStep("Fixture Product", false, "Nenhum insumo/produto encontrado no tenant de teste."));
                return new VerificationResult(false, steps);
            }
            Guid productId = (Guid)product.id;
            steps.Add(new VerificationStep("Fixture Product", true, $"Produto determinístico: {product.name} ({productId})"));

            // 2. Criação da Ordem de Serviço de Campo
            var orderId = Guid.CreateVersion7();
            await db.InTenantTransactionAsync(async (c, t) =>
            {
                await c.ExecuteAsync(new CommandDefinition(
                    """
                    insert into agro360.agriculture_records(id, tenant_id, module, status, data, version, created_at, created_by)
                    values(@Id, @TenantId, 'work-orders', 'IN_PROGRESS', @Data, 1, now(), @UserId)
                    """,
                    new
                    {
                        Id = orderId,
                        tenant.TenantId,
                        Data = JsonSerializer.Serialize(new
                        {
                            propertyId = farmId,
                            name = "Verificação de Ciclo E2E",
                            plannedArea = 10.0m,
                            responsibleId = tenant.UserId
                        }),
                        tenant.UserId
                    },
                    t,
                    cancellationToken: ct));
            }, ct);
            steps.Add(new VerificationStep("Create Work Order", true, $"Ordem de campo criada: {orderId}"));

            // 3. Adição de Material à Ordem
            await fieldOps.AddMaterialAsync(orderId, new FieldMaterialCommand(productId, null, "L", 10, 50.00m), ct);
            steps.Add(new VerificationStep("Add Material", true, "Material adicionado com sucesso à ordem"));

            var materialId = await db.InTenantTransactionAsync(async (c, t) =>
                await c.QuerySingleAsync<Guid>(new CommandDefinition(
                    "select id from agro360.field_work_order_materials where tenant_id=@TenantId and work_order_id=@Id",
                    new { tenant.TenantId, Id = orderId }, t, cancellationToken: ct)), ct);

            // Simula entrega prévia de material para custódia da equipe antes de consumir
            await db.InTenantTransactionAsync(async (c, t) =>
            {
                await c.ExecuteAsync(new CommandDefinition(
                    "update agro360.field_work_order_materials set delivered_quantity = 10 where tenant_id=@TenantId and id=@Id",
                    new { tenant.TenantId, Id = materialId }, t, cancellationToken: ct));
            }, ct);

            // 4. Apontamento de Consumo de Insumo -> Post de Custo de Material
            await fieldOps.ApplyMaterialEventAsync(orderId, materialId, new FieldMaterialEventCommand("CONSUME", 10, "Consumo verificação", null, Guid.NewGuid().ToString()), ct);

            var costExists = await db.InTenantTransactionAsync(async (c, t) =>
                await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "select exists(select 1 from agro360.cost_management_entries where tenant_id=@TenantId and source_key=@Key and source_type='MATERIAL')",
                    new { tenant.TenantId, Key = materialId }, t, cancellationToken: ct)), ct);

            steps.Add(new VerificationStep("Material Cost Posting", costExists, costExists ? "Entrada contábil de custo de material gerada com sucesso" : "Falha na geração do custo de material"));
            if (!costExists) overall = false;

            // 5. Registro de Horas e Apontamento de Campo
            await fieldOps.AddWorkLogAsync(orderId, new FieldWorkLogCommand(
                OperatorId: tenant.UserId,
                EquipmentId: null,
                Stage: "Aplicação",
                StartsAt: DateTimeOffset.UtcNow.AddHours(-2),
                EndsAt: DateTimeOffset.UtcNow,
                PerformedQuantity: 10,
                Unit: "ha",
                PhysicalAreaHa: 10,
                InitialMeter: 100,
                FinalMeter: 120,
                InterruptionMinutes: 0,
                InterruptionReason: null,
                Notes: "Apontamento verificação de ciclo",
                EvidenceReference: "REF-CYCLE-001",
                IdempotencyKey: Guid.NewGuid().ToString()), ct);
            steps.Add(new VerificationStep("Add Field Work Log", true, "Apontamento operacional registrado"));

            // 6. Revisão e Conclusão da Ordem -> Post de Custo de Atividade
            await db.InTenantTransactionAsync(async (c, t) =>
            {
                await c.ExecuteAsync(new CommandDefinition(
                    "update agro360.agriculture_records set status='AWAITING_REVIEW' where tenant_id=@TenantId and id=@Id",
                    new { tenant.TenantId, Id = orderId }, t, cancellationToken: ct));
            }, ct);

            await fieldOps.ReviewAsync(orderId, new FieldReviewCommand(1, "Conferência de verificação de ciclo aprovada"), ct);

            var activityCostExists = await db.InTenantTransactionAsync(async (c, t) =>
                await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "select exists(select 1 from agro360.cost_management_entries where tenant_id=@TenantId and source_key=@Key and source_type='ACTIVITY')",
                    new { tenant.TenantId, Key = orderId }, t, cancellationToken: ct)), ct);

            steps.Add(new VerificationStep("Activity Cost Posting", activityCostExists, activityCostExists ? "Entrada contábil de atividade gerada" : "Falha no custo de atividade"));
            if (!activityCostExists) overall = false;

            // 7. Apropriação de Custo da Safra
            var entryId = await db.InTenantTransactionAsync(async (c, t) =>
                await c.QuerySingleOrDefaultAsync<Guid?>(new CommandDefinition(
                    "select id from agro360.cost_management_entries where tenant_id=@TenantId and source_key=@Key and allocated_amount < recognized_amount limit 1",
                    new { tenant.TenantId, Key = orderId }, t, cancellationToken: ct)), ct);

            if (entryId.HasValue)
            {
                var allocKey = Guid.NewGuid().ToString();
                await seasonCost.ConfirmAsync(new ConfirmCostAllocationCommand(
                    new CostAllocationPreviewCommand(entryId.Value, 50.00m, "DIRECT", 1, new List<CostDestinationCommand> { new(seasonId, farmId, Guid.Empty, Guid.Empty, 50.00m, "ha", 100) }),
                    allocKey, "Apropriação automática da verificação"), ct);

                var allocated = await db.InTenantTransactionAsync(async (c, t) =>
                    await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                        "select exists(select 1 from agro360.cost_allocations where tenant_id=@TenantId and entry_id=@Id and status='CONFIRMED')",
                        new { tenant.TenantId, Id = entryId.Value }, t, cancellationToken: ct)), ct);

                steps.Add(new VerificationStep("Season Cost Appropriation", allocated, allocated ? "Custo apropriado com sucesso na safra" : "Falha na apropriação"));
                if (!allocated) overall = false;
            }

            // 8. Validação de Indicadores
            var indicators = await intelligence.GetIndicatorsAsync(new IntelligenceFilter(DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(-1)), DateOnly.FromDateTime(DateTime.UtcNow), farmId, seasonId, null), ct);
            var costPerHa = indicators.FirstOrDefault(x => x.Code == "cost-per-hectare");

            steps.Add(new VerificationStep("Intelligence Indicator", costPerHa != null, costPerHa != null ? $"Indicador de custo por hectare calculado: {costPerHa.Value} {costPerHa.Unit}" : "Indicador não encontrado"));
        }
        catch (Exception ex)
        {
            InfrastructureLogMessages.CycleVerificationFailed(logger, ex);
            steps.Add(new VerificationStep("Verification Execution Exception", false, ex.Message));
            overall = false;
        }

        return new VerificationResult(overall, steps);
    }
}
