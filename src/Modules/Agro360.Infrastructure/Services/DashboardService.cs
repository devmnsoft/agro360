using Agro360.Application.Abstractions;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Dapper;

namespace Agro360.Infrastructure.Services;

public sealed class DashboardService(
    DatabaseExecutor database,
    ITenantContext tenantContext,
    IClock clock) : IDashboardService
{
    public Task<CommandCenterResult> GetCommandCenterAsync(Guid? farmId, CancellationToken cancellationToken) =>
        database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            var row = await connection.QuerySingleAsync<DashboardRow>(new CommandDefinition(
                """
                select
                    (select count(*) from agro360.geo_farms f
                        where f.tenant_id = @TenantId and f.deleted_at is null
                          and (@FarmId is null or f.id = @FarmId)) as Farms,
                    (select coalesce(sum(f.total_area_ha), 0) from agro360.geo_farms f
                        where f.tenant_id = @TenantId and f.deleted_at is null
                          and (@FarmId is null or f.id = @FarmId)) as TotalAreaHa,
                    (select count(*) from agro360.agriculture_seasons s
                        where s.tenant_id = @TenantId and s.status = 2 and s.deleted_at is null
                          and (@FarmId is null or s.farm_id = @FarmId)) as ActiveSeasons,
                    (select count(*) from agro360.livestock_animals a
                        where a.tenant_id = @TenantId and a.status = 1 and a.deleted_at is null
                          and (@FarmId is null or a.farm_id = @FarmId)) as ActiveAnimals,
                    (select coalesce(sum(b.available * b.average_cost), 0)
                        from agro360.inventory_stock_balances b
                        join agro360.inventory_warehouses w on w.id = b.warehouse_id and w.tenant_id = b.tenant_id
                        where b.tenant_id = @TenantId
                          and (@FarmId is null or w.farm_id = @FarmId)) as InventoryValue,
                    (select coalesce(sum(r.amount - r.paid_amount), 0) from agro360.finance_receivables r
                        where r.tenant_id = @TenantId and r.status in ('OPEN', 'OVERDUE')
                          and (@FarmId is null or r.farm_id = @FarmId)) as Receivables,
                    (select coalesce(sum(c.amount), 0) from agro360.cost_entries c
                        where c.tenant_id = @TenantId
                          and (@FarmId is null or c.farm_id = @FarmId)) as OperationalCosts,
                    (select count(*) from agro360.notification_alerts n
                        where n.tenant_id = @TenantId and n.status = 'OPEN' and n.severity in ('HIGH', 'CRITICAL')
                          and (@FarmId is null or n.farm_id = @FarmId)) as CriticalAlerts;
                """,
                new { tenantContext.TenantId, FarmId = farmId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var operationRows = await connection.QueryAsync<RecentOperationRow>(new CommandDefinition(
                """
                select
                    recent."Id" as "Id",
                    recent."ModuleName" as "ModuleName",
                    recent."OperationType" as "OperationType",
                    recent."Description" as "Description",
                    recent."Amount" as "Amount",
                    recent."OccurredAt" as "OccurredAt",
                    recent."Status" as "Status"
                from (
                    select
                           o.id as "Id",
                           'AGRICULTURE'::text as "ModuleName",
                           coalesce(o.operation_type, 'OPERATION')::text as "OperationType",
                           concat(coalesce(o.operation_type, 'Operação'), ' · ', coalesce(f.name, 'Talhão'))::text as "Description",
                           (select sum(c.amount) from agro360.cost_entries c
                            where c.source_id = o.id and c.tenant_id = o.tenant_id) as "Amount",
                           o.executed_at as "OccurredAt",
                           coalesce(o.status, 'UNKNOWN')::text as "Status"
                    from agro360.agriculture_field_operations o
                    join agro360.geo_fields f on f.id = o.field_id and f.tenant_id = o.tenant_id
                    where o.tenant_id = @TenantId and (@FarmId is null or o.farm_id = @FarmId)

                    union all

                    select
                           e.id as "Id",
                           'LIVESTOCK'::text as "ModuleName",
                           coalesce(e.event_type, 'EVENT')::text as "OperationType",
                           concat(coalesce(e.event_type, 'Evento'), ' · animal ', coalesce(a.tag, 'sem identificação'))::text as "Description",
                           e.cost_amount as "Amount",
                           e.created_at as "OccurredAt",
                           'COMPLETED'::text as "Status"
                    from agro360.livestock_animal_events e
                    join agro360.livestock_animals a on a.id = e.animal_id and a.tenant_id = e.tenant_id
                    where e.tenant_id = @TenantId and (@FarmId is null or a.farm_id = @FarmId)

                    union all

                    select
                           s.id as "Id",
                           'COMMERCIAL'::text as "ModuleName",
                           'SALE'::text as "OperationType",
                           concat('Venda · ', coalesce(s.buyer_name, 'Cliente'))::text as "Description",
                           s.total_amount as "Amount",
                           s.created_at as "OccurredAt",
                           coalesce(s.status, 'UNKNOWN')::text as "Status"
                    from agro360.commercial_sales s
                    where s.tenant_id = @TenantId and (@FarmId is null or s.farm_id = @FarmId)
                ) recent
                order by recent."OccurredAt" desc
                limit 12;
                """,
                new { tenantContext.TenantId, FarmId = farmId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            var operations = operationRows.Select(MapRecentOperation).ToArray();

            var estimatedMargin = row.Receivables - row.OperationalCosts;
            var kpis = new DashboardKpis(
                row.Farms,
                row.TotalAreaHa,
                row.ActiveSeasons,
                row.ActiveAnimals,
                row.InventoryValue,
                row.Receivables,
                row.OperationalCosts,
                estimatedMargin,
                row.CriticalAlerts,
                clock.UtcNow);
            return new CommandCenterResult(kpis, operations);
        }, cancellationToken);

    private sealed class DashboardRow
    {
        public int Farms { get; init; }

        public decimal TotalAreaHa { get; init; }

        public int ActiveSeasons { get; init; }

        public int ActiveAnimals { get; init; }

        public decimal InventoryValue { get; init; }

        public decimal Receivables { get; init; }

        public decimal OperationalCosts { get; init; }

        public int CriticalAlerts { get; init; }
    }

    internal static RecentOperation MapRecentOperation(RecentOperationRow row)
    {
        var occurredAtUtc = row.OccurredAt.Kind switch
        {
            DateTimeKind.Utc => row.OccurredAt,
            DateTimeKind.Local => row.OccurredAt.ToUniversalTime(),
            _ => DateTime.SpecifyKind(row.OccurredAt, DateTimeKind.Utc)
        };

        return new RecentOperation(
            row.Id,
            row.ModuleName,
            row.OperationType,
            row.Description,
            row.Amount,
            new DateTimeOffset(occurredAtUtc),
            row.Status);
    }

    internal sealed class RecentOperationRow
    {
        public Guid Id { get; set; }

        public string ModuleName { get; set; } = string.Empty;

        public string OperationType { get; set; } = string.Empty;

        public string Description { get; set; } = string.Empty;

        public decimal? Amount { get; set; }

        public DateTime OccurredAt { get; set; }

        public string Status { get; set; } = string.Empty;
    }
}
