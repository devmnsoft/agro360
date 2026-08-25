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
                    (select count(*) from geo.farms f
                        where f.tenant_id = @TenantId and f.deleted_at is null
                          and (@FarmId is null or f.id = @FarmId)) as Farms,
                    (select coalesce(sum(f.total_area_ha), 0) from geo.farms f
                        where f.tenant_id = @TenantId and f.deleted_at is null
                          and (@FarmId is null or f.id = @FarmId)) as TotalAreaHa,
                    (select count(*) from agriculture.seasons s
                        where s.tenant_id = @TenantId and s.status = 2 and s.deleted_at is null
                          and (@FarmId is null or s.farm_id = @FarmId)) as ActiveSeasons,
                    (select count(*) from livestock.animals a
                        where a.tenant_id = @TenantId and a.status = 1 and a.deleted_at is null
                          and (@FarmId is null or a.farm_id = @FarmId)) as ActiveAnimals,
                    (select coalesce(sum(b.available * b.average_cost), 0)
                        from inventory.stock_balances b
                        join inventory.warehouses w on w.id = b.warehouse_id and w.tenant_id = b.tenant_id
                        where b.tenant_id = @TenantId
                          and (@FarmId is null or w.farm_id = @FarmId)) as InventoryValue,
                    (select coalesce(sum(r.amount - r.paid_amount), 0) from finance.receivables r
                        where r.tenant_id = @TenantId and r.status in ('OPEN', 'OVERDUE')
                          and (@FarmId is null or r.farm_id = @FarmId)) as Receivables,
                    (select coalesce(sum(c.amount), 0) from cost.entries c
                        where c.tenant_id = @TenantId
                          and (@FarmId is null or c.farm_id = @FarmId)) as OperationalCosts,
                    (select count(*) from notification.alerts n
                        where n.tenant_id = @TenantId and n.status = 'OPEN' and n.severity in ('HIGH', 'CRITICAL')
                          and (@FarmId is null or n.farm_id = @FarmId)) as CriticalAlerts;
                """,
                new { tenantContext.TenantId, FarmId = farmId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var operations = (await connection.QueryAsync<RecentOperation>(new CommandDefinition(
                """
                select * from (
                    select o.id, 'AGRICULTURE' as Module, o.operation_type as Type,
                           concat(o.operation_type, ' · ', f.name) as Description,
                           c.amount as Amount, o.executed_at as OccurredAt, o.status
                    from agriculture.field_operations o
                    join geo.fields f on f.id = o.field_id and f.tenant_id = o.tenant_id
                    left join cost.entries c on c.source_id = o.id and c.tenant_id = o.tenant_id
                    where o.tenant_id = @TenantId and (@FarmId is null or o.farm_id = @FarmId)

                    union all

                    select e.id, 'LIVESTOCK', e.event_type,
                           concat(e.event_type, ' · animal ', a.tag), e.cost_amount,
                           e.created_at, 'COMPLETED'
                    from livestock.animal_events e
                    join livestock.animals a on a.id = e.animal_id and a.tenant_id = e.tenant_id
                    where e.tenant_id = @TenantId and (@FarmId is null or a.farm_id = @FarmId)

                    union all

                    select s.id, 'COMMERCIAL', 'SALE', concat('Venda · ', s.buyer_name),
                           s.total_amount, s.created_at, s.status
                    from commercial.sales s
                    where s.tenant_id = @TenantId and (@FarmId is null or s.farm_id = @FarmId)
                ) recent
                order by OccurredAt desc
                limit 12;
                """,
                new { tenantContext.TenantId, FarmId = farmId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

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
}
