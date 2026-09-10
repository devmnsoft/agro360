using System.Globalization;
using System.Text;
using Agro360.Application.Contracts;
using Agro360.Domain.Fleet;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Npgsql;

namespace Agro360.Infrastructure.Services;

public sealed class FleetOperationsService(DatabaseExecutor db, ITenantContext tenant) : IFleetOperationsService
{
    public Task<dynamic?> AssetDetailAsync(Guid id, CancellationToken ct) => Tx(async (c, t) =>
    {
        var asset = await c.QuerySingleOrDefaultAsync(new CommandDefinition(
            """
            select a.id, a.internal_code as "internalCode", a.name, a.status, a.cadastral_status as "cadastralStatus",
                   a.ownership, a.brand, a.model, a.year, a.plate, a.serial_number as "serialNumber",
                   a.odometer, a.hour_meter as "hourMeter", a.fuel_capacity as "fuelCapacity", a.energy_source as "energySource",
                   a.commissioned_on as "commissionedOn", a.notes, t.name as "typeName", t.kind as "typeKind",
                   p.name as "propertyName",
                   a.created_at as "createdAt", cu.name as "createdByName",
                   a.updated_at as "updatedAt", uu.name as "updatedByName",
                   a.deleted_at as "deletedAt", du.name as "deletedByName", a.deletion_reason as "deletionReason",
                   exists(select 1 from agro360.fleet_operational_blocks b where b.tenant_id=a.tenant_id and b.asset_id=a.id and b.status='ACTIVE') as "hasActiveBlock"
            from agro360.fleet_assets a
            left join agro360.fleet_asset_types t on t.id=a.asset_type_id and t.tenant_id=a.tenant_id
            left join agro360.geo_farms p on p.id=a.property_id and p.tenant_id=a.tenant_id
            left join agro360.identity_users cu on cu.tenant_id=a.tenant_id and cu.id=a.created_by
            left join agro360.identity_users uu on uu.tenant_id=a.tenant_id and uu.id=a.updated_by
            left join agro360.identity_users du on du.tenant_id=a.tenant_id and du.id=a.deleted_by
            where a.tenant_id=@TenantId and a.id=@Id
            """, new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        if (asset is null) return null;
        var meters = await c.QueryAsync(new CommandDefinition(
            "select meter_kind as \"meterKind\", unit, enabled from agro360.fleet_asset_meters where tenant_id=@TenantId and asset_id=@Id order by meter_kind",
            new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        var blocks = await c.QueryAsync(new CommandDefinition(
            "select id, kind, reason, dispensable, started_at as \"startedAt\", status from agro360.fleet_operational_blocks where tenant_id=@TenantId and asset_id=@Id and status='ACTIVE'",
            new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        var events = await c.QueryAsync(new CommandDefinition(
            "select event_type as \"eventType\", description, created_at as \"createdAt\" from agro360.fleet_asset_events where tenant_id=@TenantId and asset_id=@Id order by created_at desc limit 50",
            new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        var audit = await c.QueryAsync(new CommandDefinition(
            """
            select action, occurred_at as "occurredAt", u.name as "actorName", before_data as "beforeData", after_data as "afterData"
            from agro360.audit_logs l
            left join agro360.identity_users u on u.tenant_id=l.tenant_id and u.id=l.user_id
            where l.tenant_id=@TenantId and l.entity_type='FleetAsset' and l.entity_id=@Id
            order by l.occurred_at desc limit 50
            """, new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        return (object)new { asset, meters, blocks, events, audit };
    }, ct);

    public Task<IReadOnlyList<dynamic>> ListReadingsAsync(Guid assetId, string? meterKind, CancellationToken ct) =>
        List("""
            select id, meter_kind as "meterKind", occurred_at as "occurredAt", physical_value as "physicalValue",
                   operational_accumulated as "operationalAccumulated", unit, origin, is_reset as "isReset", justification
            from agro360.fleet_meter_readings
            where tenant_id=@TenantId and asset_id=@AssetId and (@MeterKind is null or meter_kind=@MeterKind)
            order by occurred_at desc limit 200
            """, ct, new { AssetId = assetId, MeterKind = meterKind });

    public Task<Guid> RecordReadingAsync(MeterReadingCommand command, bool meterOverride, CancellationToken ct)
    {
        var kind = Guard.Required(command.MeterKind, nameof(command.MeterKind), 20).ToUpperInvariant();
        if (!FleetRules.MeterKinds.Contains(kind)) throw new DomainException("Tipo de medidor inválido.", "fleet.meter_kind_invalid");
        var unit = Guard.Required(command.Unit, nameof(command.Unit), 16);
        return Tx(async (c, t) =>
        {
            if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                var replay = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                    "select id from agro360.fleet_meter_readings where tenant_id=@TenantId and idempotency_key=@Key",
                    new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
                if (replay is Guid found) return found;
            }
            await EnsureAssetAsync(c, t, command.AssetId, ct);
            var previous = await c.QuerySingleOrDefaultAsync<ReadingNeighbor>(new CommandDefinition(
                """
                select physical_value as PhysicalValue, occurred_at as OccurredAt, operational_accumulated as Accumulated
                from agro360.fleet_meter_readings
                where tenant_id=@TenantId and asset_id=@Asset and meter_kind=@Kind and occurred_at <= @At
                order by occurred_at desc limit 1
                """, new { tenant.TenantId, Asset = command.AssetId, Kind = kind, At = command.OccurredAt }, t, cancellationToken: ct));
            var next = await c.QuerySingleOrDefaultAsync<ReadingNeighbor>(new CommandDefinition(
                """
                select physical_value as PhysicalValue, occurred_at as OccurredAt
                from agro360.fleet_meter_readings
                where tenant_id=@TenantId and asset_id=@Asset and meter_kind=@Kind and occurred_at >= @At
                order by occurred_at limit 1
                """, new { tenant.TenantId, Asset = command.AssetId, Kind = kind, At = command.OccurredAt }, t, cancellationToken: ct));
            FleetRules.ValidateReading(command.PhysicalValue, command.OccurredAt, previous?.PhysicalValue, previous?.OccurredAt, next?.PhysicalValue, next?.OccurredAt, command.IsReset);
            if (command.IsReset && string.IsNullOrWhiteSpace(command.Justification))
                throw new DomainException("Reinicialização do medidor exige justificativa.", "fleet.reset_reason");
            if (!command.IsReset && previous is not null && command.PhysicalValue < previous.PhysicalValue)
                FleetRules.ValidateMeterChange(previous.PhysicalValue, command.PhysicalValue, command.Justification, meterOverride);

            var delta = command.IsReset ? 0 : command.PhysicalValue - (previous?.PhysicalValue ?? command.PhysicalValue);
            if (delta < 0) delta = 0;
            var accumulated = (previous?.Accumulated ?? 0) + (command.IsReset ? 0 : delta);
            if (command.IsReset) accumulated = (previous?.Accumulated ?? 0);
            var id = Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.fleet_meter_readings
                    (id,tenant_id,asset_id,meter_kind,occurred_at,physical_value,operational_accumulated,unit,origin,is_reset,
                     responsible_id,justification,idempotency_key,created_by)
                values (@Id,@TenantId,@AssetId,@Kind,@At,@Value,@Accum,@Unit,@Origin,@Reset,@Responsible,@Justification,@IdempotencyKey,@UserId);
                insert into agro360.fleet_asset_meters(id,tenant_id,asset_id,meter_kind,unit,enabled,created_by)
                values (gen_random_uuid(),@TenantId,@AssetId,@Kind,@Unit,true,@UserId)
                on conflict (tenant_id,asset_id,meter_kind) do update set enabled=true, unit=excluded.unit;
                update agro360.fleet_assets
                set odometer=case when @Kind='ODOMETER' and @At >= coalesce((select max(occurred_at) from agro360.fleet_meter_readings r where r.tenant_id=@TenantId and r.asset_id=@AssetId and r.meter_kind='ODOMETER' and r.id<>@Id),@At) then @Value else odometer end,
                    hour_meter=case when @Kind in ('HOUR_METER','ENGINE_HOURS') and @At >= coalesce((select max(occurred_at) from agro360.fleet_meter_readings r where r.tenant_id=@TenantId and r.asset_id=@AssetId and r.meter_kind=@Kind and r.id<>@Id),@At) then @Value else hour_meter end,
                    updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@AssetId;
                insert into agro360.fleet_asset_events(id,tenant_id,asset_id,event_type,description,reference_id,created_by,updated_by)
                values (gen_random_uuid(),@TenantId,@AssetId,case when @Reset then 'METER_RESET' else 'METER_READING' end,
                        concat(@Kind,'=',@Value,' ',@Unit),@Id,@UserId,@UserId)
                """,
                new
                {
                    Id = id,
                    tenant.TenantId,
                    command.AssetId,
                    Kind = kind,
                    At = command.OccurredAt,
                    Value = command.PhysicalValue,
                    Accum = accumulated,
                    Unit = unit,
                    Origin = string.IsNullOrWhiteSpace(command.Origin) ? "MANUAL" : command.Origin.ToUpperInvariant(),
                    Reset = command.IsReset,
                    Responsible = command.ResponsibleId,
                    command.Justification,
                    command.IdempotencyKey,
                    tenant.UserId
                }, t, cancellationToken: ct));
            await Audit(c, t, "meter-reading", "Asset", command.AssetId, command, ct);
            return id;
        }, ct);
    }

    public Task<IReadOnlyList<dynamic>> ListPlansAsync(Guid? assetId, CancellationToken ct) =>
        List("""
            select p.id, p.asset_id as "assetId", a.name as "assetName", p.maintenance_type as "maintenanceType",
                   p.description, p.periodicity, p.control_unit as "controlUnit", p.due_policy as "duePolicy",
                   p.next_execution_at as "nextExecutionAt", p.next_meter as "nextMeter", p.status, p.estimated_cost as "estimatedCost",
                   p.version_no as "versionNo"
            from agro360.fleet_maintenance_plans p
            join agro360.fleet_assets a on a.id=p.asset_id and a.tenant_id=p.tenant_id
            where p.tenant_id=@TenantId and p.deleted_at is null and (@AssetId is null or p.asset_id=@AssetId)
            order by p.next_execution_at nulls last
            """, ct, new { AssetId = assetId });

    public Task EvaluatePlansAsync(CancellationToken ct) => Tx(async (c, t) =>
    {
        var plans = await c.QueryAsync(new CommandDefinition(
            """
            select p.id, p.asset_id as AssetId, p.maintenance_type as Type, p.description, p.next_execution_at as NextAt,
                   p.next_meter as NextMeter, p.due_policy as DuePolicy, a.hour_meter as HourMeter, a.odometer as Odometer,
                   exists(select 1 from agro360.fleet_meter_readings r where r.tenant_id=p.tenant_id and r.asset_id=p.asset_id) as HasReading
            from agro360.fleet_maintenance_plans p
            join agro360.fleet_assets a on a.id=p.asset_id and a.tenant_id=p.tenant_id
            where p.tenant_id=@TenantId and p.status='ACTIVE' and p.deleted_at is null
            """, new { tenant.TenantId }, t, cancellationToken: ct));
        foreach (var plan in plans)
        {
            var state = FleetRules.PlanDueState((DateTimeOffset?)plan.NextAt, (decimal?)plan.NextMeter,
                (decimal?)plan.HourMeter ?? (decimal?)plan.Odometer, (bool)plan.HasReading);
            if (state is not ("OVERDUE" or "DUE_SOON")) continue;
            var existsOpen = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                select exists(select 1 from agro360.fleet_work_orders
                             where tenant_id=@TenantId and asset_id=@Asset and maintenance_plan_id=@Plan
                               and deleted_at is null and status not in ('COMPLETED','CANCELLED'))
                """, new { tenant.TenantId, Asset = (Guid)plan.AssetId, Plan = (Guid)plan.id }, t, cancellationToken: ct));
            if (existsOpen) continue;
            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.operations_operational_alerts
                    (id,tenant_id,dedup_key,title,description,severity,module,origin_type,origin_id,created_by)
                values (gen_random_uuid(),@TenantId,@Dedup,@Title,@Description,
                        case when @State='OVERDUE' then 'HIGH' else 'ATTENTION' end,'FLEET','MAINTENANCE_PLAN',@Plan,@UserId)
                on conflict do nothing
                """,
                new
                {
                    tenant.TenantId,
                    Dedup = $"FLEET-PLAN:{plan.id}:{state}",
                    Title = state == "OVERDUE" ? "Manutenção vencida" : "Manutenção próxima",
                    Description = (string)plan.description,
                    State = state,
                    Plan = (Guid)plan.id,
                    tenant.UserId
                }, t, cancellationToken: ct));
        }
    }, ct);

    public Task<IReadOnlyList<dynamic>> ListRequestsAsync(string? status, CancellationToken ct) =>
        List("""
            select r.id, r.asset_id as "assetId", a.name as "assetName", r.defect_class as "defectClass",
                   r.severity, r.problem_description as "problemDescription", r.status, r.blocks_asset as "blocksAsset",
                   r.created_at as "createdAt"
            from agro360.fleet_maintenance_requests r
            join agro360.fleet_assets a on a.id=r.asset_id and a.tenant_id=r.tenant_id
            where r.tenant_id=@TenantId and r.deleted_at is null and (@Status is null or r.status=@Status)
            order by r.created_at desc
            """, ct, new { Status = status });

    public Task<Guid> CreateRequestAsync(Guid assetId, string defectClass, string severity, string problem, bool blocksAsset, CancellationToken ct)
    {
        var text = Guard.Required(problem, nameof(problem), 4000);
        return Tx(async (c, t) =>
        {
            await EnsureAssetAsync(c, t, assetId, ct);
            var id = Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.fleet_maintenance_requests
                    (id,tenant_id,asset_id,defect_class,severity,problem_description,status,blocks_asset,created_by,updated_by)
                values (@Id,@TenantId,@AssetId,@Class,@Severity,@Problem,'OPEN',@Blocks,@UserId,@UserId);
                insert into agro360.fleet_work_orders
                    (id,tenant_id,asset_id,maintenance_request_id,type,priority,description,status,blocks_asset,opened_at,created_by,updated_by)
                values (@Order,@TenantId,@AssetId,@Id,'CORRECTIVE',@Severity,@Problem,'OPEN',@Blocks,now(),@UserId,@UserId);
                insert into agro360.fleet_operational_blocks
                    (id,tenant_id,asset_id,kind,reason,dispensable,work_order_id,status,created_by)
                select gen_random_uuid(),@TenantId,@AssetId,'MAINTENANCE',@Problem,false,@Order,'ACTIVE',@UserId
                where @Blocks;
                update agro360.fleet_assets set status=case when @Blocks then 'MAINTENANCE' else status end, updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@AssetId and @Blocks
                """,
                new
                {
                    Id = id,
                    Order = Guid.CreateVersion7(),
                    tenant.TenantId,
                    AssetId = assetId,
                    Class = Guard.Required(defectClass, nameof(defectClass), 80),
                    Severity = Guard.Required(severity, nameof(severity), 16).ToUpperInvariant(),
                    Problem = text,
                    Blocks = blocksAsset,
                    tenant.UserId
                }, t, cancellationToken: ct));
            await Audit(c, t, "request", "MaintenanceRequest", id, new { assetId, problem }, ct);
            return id;
        }, ct);
    }

    public Task<dynamic?> WorkOrderDetailAsync(Guid id, CancellationToken ct) => Tx(async (c, t) =>
    {
        var order = await c.QuerySingleOrDefaultAsync(new CommandDefinition(
            """
            select w.*, a.name as "assetName", a.internal_code as "assetCode"
            from agro360.fleet_work_orders w
            join agro360.fleet_assets a on a.id=w.asset_id and a.tenant_id=w.tenant_id
            where w.tenant_id=@TenantId and w.id=@Id and w.deleted_at is null
            """, new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        if (order is null) return null;
        var parts = await c.QueryAsync(new CommandDefinition(
            "select * from agro360.fleet_work_order_parts where tenant_id=@TenantId and work_order_id=@Id and deleted_at is null",
            new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        var inspections = await c.QueryAsync(new CommandDefinition(
            "select result, blocking_failures as \"blockingFailures\", notes, occurred_at as \"occurredAt\" from agro360.fleet_work_order_inspections where tenant_id=@TenantId and work_order_id=@Id order by occurred_at desc",
            new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        var costs = await c.QueryAsync(new CommandDefinition(
            "select cost_type as \"costType\", value from agro360.fleet_work_order_costs where tenant_id=@TenantId and work_order_id=@Id and deleted_at is null",
            new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        return (object)new { order, parts, inspections, costs };
    }, ct);

    public Task ReservePartAsync(WorkOrderPartCommand command, CancellationToken ct)
    {
        LivestockPositive(command.Quantity);
        return Tx(async (c, t) =>
        {
            await EnsureOpenOrderAsync(c, t, command.WorkOrderId, ct);
            if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                var replay = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                    "select id from agro360.fleet_work_order_parts where tenant_id=@TenantId and idempotency_key=@Key",
                    new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
                if (replay is not null) return;
            }
            var balance = await LockStockAsync(c, t, command.WarehouseId, command.ProductId, command.Unit, ct);
            if (balance.Available - balance.Reserved < command.Quantity)
                throw new ConflictException("Estoque insuficiente para reserva.", "agro360.inventory_insufficient_stock");
            await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.inventory_stock_balances
                set reserved=reserved+@Quantity, version=version+1, updated_at=now()
                where id=@Id and tenant_id=@TenantId and version=@Version;
                insert into agro360.fleet_work_order_parts
                    (id,tenant_id,work_order_id,product_id,description,quantity,unit_cost,warehouse_id,reserved_quantity,unit,idempotency_key,created_by,updated_by)
                values (gen_random_uuid(),@TenantId,@Order,@ProductId,@Description,@Quantity,@UnitCost,@Warehouse,@Quantity,@Unit,@IdempotencyKey,@UserId,@UserId);
                update agro360.fleet_work_orders set status='WAITING_PART', updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Order and status in ('OPEN','PLANNED','IN_PROGRESS')
                """,
                new
                {
                    command.Quantity,
                    Id = balance.Id,
                    tenant.TenantId,
                    balance.Version,
                    Order = command.WorkOrderId,
                    command.ProductId,
                    command.Description,
                    command.UnitCost,
                    Warehouse = command.WarehouseId,
                    command.Unit,
                    command.IdempotencyKey,
                    tenant.UserId
                }, t, cancellationToken: ct));
            await Audit(c, t, "reserve-part", "WorkOrder", command.WorkOrderId, command, ct);
        }, ct);
    }

    public Task ConsumePartAsync(Guid partLineId, decimal quantity, string? idempotencyKey, CancellationToken ct)
    {
        LivestockPositive(quantity);
        return Tx(async (c, t) =>
        {
            var line = await c.QuerySingleOrDefaultAsync<PartLine>(new CommandDefinition(
                """
                select id, work_order_id as WorkOrderId, product_id as ProductId, warehouse_id as WarehouseId, unit,
                       reserved_quantity as ReservedQuantity, consumed_quantity as ConsumedQuantity, unit_cost as UnitCost
                from agro360.fleet_work_order_parts
                where tenant_id=@TenantId and id=@Id and deleted_at is null for update
                """, new { tenant.TenantId, Id = partLineId }, t, cancellationToken: ct))
                ?? throw new NotFoundException("Peça da OS", partLineId);
            if (line.WarehouseId is null || line.ProductId is null)
                throw new DomainException("Peça sem depósito/produto não consome estoque.", "fleet.part_stock_missing");
            var balance = await LockStockAsync(c, t, line.WarehouseId.Value, line.ProductId.Value, line.Unit ?? "unit", ct);
            if (balance.Available < quantity)
                throw new ConflictException("Estoque insuficiente para consumo.", "agro360.inventory_insufficient_stock");
            if (!string.IsNullOrWhiteSpace(balance.QualityStatus) && balance.QualityStatus is "BLOCKED" or "PENDING" or "REJECTED" or "QUARANTINE")
                throw new ConflictException("Material bloqueado ou em quarentena não pode ser consumido.", "agro360.inventory_lot_blocked");
            var movementId = Guid.CreateVersion7();
            var newBalance = balance.Available - quantity;
            var reservedRelease = Math.Min(line.ReservedQuantity, quantity);
            await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.inventory_stock_balances
                set available=@Balance, reserved=greatest(reserved-@Release,0), version=version+1, updated_at=now()
                where id=@Id and tenant_id=@TenantId and version=@Version;
                insert into agro360.inventory_stock_movements
                    (id,tenant_id,warehouse_id,product_id,movement_type,quantity,unit,unit_cost,total_cost,reference_type,reference_id,
                     balance_after,average_cost_after,balance_version,occurred_at,created_by,idempotency_key)
                values (@Movement,@TenantId,@Warehouse,@Product,'CONSUMPTION',@Quantity,@Unit,@Cost,@Total,'WORK_ORDER_PART',@Line,
                        @Balance,@Cost,@NewVersion,now(),@UserId,@IdempotencyKey);
                update agro360.fleet_work_order_parts
                set consumed_quantity=consumed_quantity+@Quantity, reserved_quantity=greatest(reserved_quantity-@Release,0),
                    stock_movement_id=@Movement, updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Line;
                insert into agro360.fleet_work_order_costs(id,tenant_id,work_order_id,cost_type,value,created_by,updated_by)
                values (gen_random_uuid(),@TenantId,@Order,'PARTS',@Total,@UserId,@UserId);
                insert into agro360.fleet_operational_costs(id,tenant_id,asset_id,cost_type,value,occurred_on,origin_type,origin_id,status,created_by,updated_by)
                select gen_random_uuid(),@TenantId,w.asset_id,'PARTS',@Total,current_date,'WORK_ORDER_PART',@Line,'ACTIVE',@UserId,@UserId
                from agro360.fleet_work_orders w where w.tenant_id=@TenantId and w.id=@Order
                on conflict (tenant_id,origin_type,origin_id) do nothing
                """,
                new
                {
                    Balance = newBalance,
                    Release = reservedRelease,
                    Id = balance.Id,
                    tenant.TenantId,
                    balance.Version,
                    Movement = movementId,
                    Warehouse = line.WarehouseId,
                    Product = line.ProductId,
                    Quantity = quantity,
                    Unit = line.Unit ?? "unit",
                    Cost = balance.AverageCost,
                    Total = decimal.Round(quantity * balance.AverageCost, 2, MidpointRounding.AwayFromZero),
                    Line = partLineId,
                    NewVersion = balance.Version + 1,
                    tenant.UserId,
                    IdempotencyKey = string.IsNullOrWhiteSpace(idempotencyKey) ? null : $"fleet-part:{idempotencyKey}",
                    Order = line.WorkOrderId
                }, t, cancellationToken: ct));
            await Audit(c, t, "consume-part", "WorkOrder", line.WorkOrderId, new { partLineId, quantity }, ct);
        }, ct);
    }

    public Task ReturnPartAsync(Guid partLineId, PartReturnCommand command, CancellationToken ct)
    {
        if (command.ReturnedQuantity < 0 || command.LostQuantity < 0)
            throw new DomainException("Devolução e perda não podem ser negativas.", "fleet.return_negative");
        return Tx(async (c, t) =>
        {
            var line = await c.QuerySingleOrDefaultAsync<PartLine>(new CommandDefinition(
                """
                select id, work_order_id as WorkOrderId, product_id as ProductId, warehouse_id as WarehouseId, unit,
                       consumed_quantity as ConsumedQuantity, returned_quantity as ReturnedQuantity, lost_quantity as LostQuantity
                from agro360.fleet_work_order_parts where tenant_id=@TenantId and id=@Id for update
                """, new { tenant.TenantId, Id = partLineId }, t, cancellationToken: ct))
                ?? throw new NotFoundException("Peça da OS", partLineId);
            if (command.ReturnedQuantity + command.LostQuantity > line.ConsumedQuantity - line.ReturnedQuantity - line.LostQuantity)
                throw new DomainException("Devolução/perda não pode exceder o consumido líquido.", "fleet.return_excess");
            if (command.ReturnedQuantity > 0 && command.Reusable && line is { WarehouseId: Guid wh, ProductId: Guid product })
            {
                var balance = await LockStockAsync(c, t, wh, product, line.Unit ?? "unit", ct);
                await c.ExecuteAsync(new CommandDefinition(
                    """
                    update agro360.inventory_stock_balances set available=available+@Qty, version=version+1, updated_at=now()
                    where id=@Id and tenant_id=@TenantId and version=@Version;
                    insert into agro360.inventory_stock_movements
                        (id,tenant_id,warehouse_id,product_id,movement_type,quantity,unit,unit_cost,total_cost,reference_type,reference_id,
                         balance_after,average_cost_after,balance_version,occurred_at,created_by)
                    values (gen_random_uuid(),@TenantId,@Warehouse,@Product,'ADJUSTMENT_IN',@Qty,@Unit,@Cost,@Total,'WORK_ORDER_RETURN',@Line,
                            @Balance,@Cost,@NewVersion,now(),@UserId)
                    """,
                    new
                    {
                        Qty = command.ReturnedQuantity,
                        Id = balance.Id,
                        tenant.TenantId,
                        balance.Version,
                        Warehouse = wh,
                        Product = product,
                        Unit = line.Unit ?? "unit",
                        Cost = balance.AverageCost,
                        Total = command.ReturnedQuantity * balance.AverageCost,
                        Line = partLineId,
                        Balance = balance.Available + command.ReturnedQuantity,
                        NewVersion = balance.Version + 1,
                        tenant.UserId
                    }, t, cancellationToken: ct));
            }
            await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.fleet_work_order_parts
                set returned_quantity=returned_quantity+@Returned, lost_quantity=lost_quantity+@Lost, reusable=@Reusable,
                    updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Id;
                insert into agro360.fleet_removed_parts(id,tenant_id,work_order_id,product_id,description,quantity,condition,created_by)
                select gen_random_uuid(),@TenantId,work_order_id,product_id,description,@Returned,
                       case when @Reusable then 'REUSABLE' else 'SCRAP' end,@UserId
                from agro360.fleet_work_order_parts where tenant_id=@TenantId and id=@Id and @Returned>0
                """,
                new
                {
                    Returned = command.ReturnedQuantity,
                    Lost = command.LostQuantity,
                    command.Reusable,
                    tenant.UserId,
                    tenant.TenantId,
                    Id = partLineId
                }, t, cancellationToken: ct));
            await Audit(c, t, "return-part", "WorkOrder", line.WorkOrderId, command, ct);
        }, ct);
    }

    public Task LogTimeAsync(Guid workOrderId, TimeLogCommand command, CancellationToken ct) => Tx(async (c, t) =>
    {
        await EnsureOpenOrderAsync(c, t, workOrderId, ct);
        if (command.EndedAt is { } end && end < command.StartedAt)
            throw new DomainException("Fim do apontamento deve ser posterior ao início.", "fleet.time_range");
        var overlap = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists(
              select 1 from agro360.fleet_work_order_time_logs
              where tenant_id=@TenantId and technician_id=@Tech and ended_at is null
                 or (tenant_id=@TenantId and technician_id=@Tech and ended_at is not null
                     and started_at < coalesce(@Ended,@Started) and ended_at > @Started))
            """, new { tenant.TenantId, Tech = command.TechnicianId, Ended = command.EndedAt, Started = command.StartedAt }, t, cancellationToken: ct));
        if (overlap)
            throw new ConflictException("Apontamento sobreposto para o mesmo técnico.", "fleet.time_overlap");
        var minutes = command.EndedAt is { } finished
            ? (int)Math.Round((finished - command.StartedAt).TotalMinutes)
            : (int?)null;
        await c.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.fleet_work_order_time_logs
                (id,tenant_id,work_order_id,technician_id,started_at,ended_at,minutes_effective,notes,created_by)
            values (gen_random_uuid(),@TenantId,@Order,@Tech,@Started,@Ended,@Minutes,@Notes,@UserId)
            """,
            new
            {
                tenant.TenantId,
                Order = workOrderId,
                Tech = command.TechnicianId,
                Started = command.StartedAt,
                Ended = command.EndedAt,
                Minutes = minutes,
                command.Notes,
                tenant.UserId
            }, t, cancellationToken: ct));
    }, ct);

    public Task InspectAsync(Guid workOrderId, InspectionCommand command, CancellationToken ct)
    {
        var result = Guard.Required(command.Result, nameof(command.Result), 20).ToUpperInvariant();
        if (result is not ("APPROVED" or "REJECTED" or "PENDING"))
            throw new DomainException("Resultado de inspeção inválido.", "fleet.inspection_result");
        return Tx(async (c, t) =>
        {
            var order = await EnsureOpenOrderAsync(c, t, workOrderId, ct);
            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.fleet_work_order_inspections
                    (id,tenant_id,work_order_id,result,checklist,blocking_failures,inspector_id,notes,created_by)
                values (gen_random_uuid(),@TenantId,@Order,@Result,cast(@Checklist as jsonb),@Failures,@UserId,@Notes,@UserId);
                update agro360.fleet_work_orders
                set status=case when @Result='APPROVED' then 'COMPLETED' when @Result='REJECTED' then 'IN_PROGRESS' else 'INSPECTION' end,
                    inspection_result=@Result, completed_at=case when @Result='APPROVED' then now() else completed_at end,
                    updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Order;
                insert into agro360.fleet_operational_blocks
                    (id,tenant_id,asset_id,kind,reason,dispensable,work_order_id,status,created_by)
                select gen_random_uuid(),@TenantId,@Asset,'INSPECTION','Inspeção reprovada — item impeditivo',false,@Order,'ACTIVE',@UserId
                where @Result='REJECTED' and @Failures>0;
                update agro360.fleet_operational_blocks
                set status='RELEASED', ended_at=now()
                where tenant_id=@TenantId and work_order_id=@Order and status='ACTIVE' and @Result='APPROVED';
                update agro360.fleet_assets
                set status=case
                    when @Result='APPROVED' and not exists(select 1 from agro360.fleet_operational_blocks b where b.tenant_id=@TenantId and b.asset_id=@Asset and b.status='ACTIVE')
                    then 'AVAILABLE' else status end,
                    updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Asset
                """,
                new
                {
                    tenant.TenantId,
                    Order = workOrderId,
                    Result = result,
                    Checklist = System.Text.Json.JsonSerializer.Serialize(command.Checklist),
                    Failures = command.BlockingFailures,
                    tenant.UserId,
                    command.Notes,
                    Asset = order.AssetId
                }, t, cancellationToken: ct));
            await Audit(c, t, "inspect", "WorkOrder", workOrderId, command, ct);
        }, ct);
    }

    public Task ReleaseAssetAsync(Guid assetId, string reason, CancellationToken ct)
    {
        var text = Guard.Required(reason, nameof(reason), 500);
        return Tx(async (c, t) =>
        {
            var active = await c.QueryAsync(new CommandDefinition(
                "select id, kind, reason, dispensable from agro360.fleet_operational_blocks where tenant_id=@TenantId and asset_id=@Asset and status='ACTIVE'",
                new { tenant.TenantId, Asset = assetId }, t, cancellationToken: ct));
            var blocking = active.Where(x => !(bool)x.dispensable).ToArray();
            if (blocking.Length > 0)
                throw new ConflictException($"Há impedimento não dispensável: {blocking[0].reason}", "fleet.block_active");
            await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.fleet_operational_blocks set status='RELEASED', ended_at=now()
                where tenant_id=@TenantId and asset_id=@Asset and status='ACTIVE';
                update agro360.fleet_assets set status='AVAILABLE', updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Asset;
                insert into agro360.fleet_asset_events(id,tenant_id,asset_id,event_type,description,created_by,updated_by)
                values (gen_random_uuid(),@TenantId,@Asset,'RELEASED',@Reason,@UserId,@UserId)
                """,
                new { tenant.TenantId, Asset = assetId, tenant.UserId, Reason = text }, t, cancellationToken: ct));
        }, ct);
    }

    public Task<IReadOnlyList<dynamic>> AvailabilityAsync(Guid assetId, DateTimeOffset from, DateTimeOffset until, CancellationToken ct)
    {
        if (until <= from) throw new DomainException("Período de disponibilidade inválido.", "fleet.availability_window");
        return List("""
            select 'RESERVATION' as kind, starts_at as "startsAt", ends_at as "endsAt", purpose as detail, status
            from agro360.fleet_asset_reservations
            where tenant_id=@TenantId and asset_id=@AssetId and status='ACTIVE'
              and starts_at < @Until and ends_at > @From
            union all
            select 'BLOCK', started_at, coalesce(ended_at,@Until), reason, status
            from agro360.fleet_operational_blocks
            where tenant_id=@TenantId and asset_id=@AssetId and status='ACTIVE'
            order by 2
            """, ct, new { AssetId = assetId, From = from, Until = until });
    }

    public Task<Guid> ReserveAssetAsync(AssetReservationCommand command, CancellationToken ct)
    {
        if (command.EndsAt <= command.StartsAt)
            throw new DomainException("Fim da reserva deve ser posterior ao início.", "fleet.reservation_window");
        return Tx(async (c, t) =>
        {
            if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                var replay = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                    "select id from agro360.fleet_asset_reservations where tenant_id=@TenantId and idempotency_key=@Key",
                    new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
                if (replay is Guid found) return found;
            }
            var asset = await EnsureAssetAsync(c, t, command.AssetId, ct);
            if (asset.CadastralStatus is "INACTIVE" or "WRITTEN_OFF" or "SOLD")
                throw new ConflictException("Ativo cadastralmente inapto não pode ser reservado.", "fleet.asset_inactive");
            var blocked = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                select exists(select 1 from agro360.fleet_operational_blocks
                             where tenant_id=@TenantId and asset_id=@Asset and status='ACTIVE' and not dispensable)
                """, new { tenant.TenantId, Asset = command.AssetId }, t, cancellationToken: ct));
            if (blocked)
                throw new ConflictException("Há bloqueio impeditivo vigente. A tela deve indicar a causa e a ação necessária.", "fleet.block_active");
            var conflict = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                select exists(
                  select 1 from agro360.fleet_asset_reservations
                  where tenant_id=@TenantId and asset_id=@Asset and status='ACTIVE'
                    and starts_at < @Ends and ends_at > @Starts)
                """, new { tenant.TenantId, Asset = command.AssetId, Starts = command.StartsAt, Ends = command.EndsAt }, t, cancellationToken: ct));
            if (conflict)
                throw new ConflictException("Conflito de agenda: já existe reserva incompatível no período.", "fleet.reservation_conflict");
            var id = Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.fleet_asset_reservations
                    (id,tenant_id,asset_id,purpose,starts_at,ends_at,reference_type,reference_id,status,notes,idempotency_key,created_by)
                values (@Id,@TenantId,@AssetId,@Purpose,@Starts,@Ends,@ReferenceType,@ReferenceId,'ACTIVE',@Notes,@IdempotencyKey,@UserId);
                update agro360.fleet_assets set status=case when status='AVAILABLE' then 'RESERVED' else status end, updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@AssetId
                """,
                new
                {
                    Id = id,
                    tenant.TenantId,
                    command.AssetId,
                    Purpose = Guard.Required(command.Purpose, nameof(command.Purpose), 40).ToUpperInvariant(),
                    Starts = command.StartsAt,
                    Ends = command.EndsAt,
                    command.ReferenceType,
                    command.ReferenceId,
                    command.Notes,
                    command.IdempotencyKey,
                    tenant.UserId
                }, t, cancellationToken: ct));
            await Audit(c, t, "reserve-asset", "Asset", command.AssetId, command, ct);
            return id;
        }, ct);
    }

    public Task CancelReservationAsync(Guid id, string reason, CancellationToken ct)
    {
        var text = Guard.Required(reason, nameof(reason), 500);
        return Tx(async (c, t) =>
        {
            var reservation = await c.QuerySingleOrDefaultAsync<(Guid Id, Guid AssetId, string Status)>(new CommandDefinition(
                "select id, asset_id as AssetId, status from agro360.fleet_asset_reservations where tenant_id=@TenantId and id=@Id for update",
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
            if (reservation.Id == Guid.Empty) throw new NotFoundException("Reserva", id);
            if (reservation.Status != "ACTIVE") throw new ConflictException("Somente reserva vigente pode ser cancelada.", "fleet.reservation_not_active");
            await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.fleet_asset_reservations set status='CANCELLED', notes=coalesce(notes,'')||' '||@Reason
                where tenant_id=@TenantId and id=@Id;
                update agro360.fleet_assets set status=case when status='RESERVED' then 'AVAILABLE' else status end, updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Asset
                  and not exists(select 1 from agro360.fleet_asset_reservations r where r.tenant_id=@TenantId and r.asset_id=@Asset and r.status='ACTIVE' and r.id<>@Id)
                  and not exists(select 1 from agro360.fleet_operational_blocks b where b.tenant_id=@TenantId and b.asset_id=@Asset and b.status='ACTIVE')
                """,
                new { Reason = text, tenant.TenantId, Id = id, tenant.UserId, Asset = reservation.AssetId }, t, cancellationToken: ct));
        }, ct);
    }

    public Task<Guid> RefuelOperationalAsync(OperationalRefuelCommand command, bool meterOverride, CancellationToken ct)
    {
        var source = Guard.Required(command.Source, nameof(command.Source), 16).ToUpperInvariant();
        if (!FleetRules.RefuelSources.Contains(source)) throw new DomainException("Origem do abastecimento inválida.", "fleet.refuel_source");
        var total = FleetRules.RefuelingTotal(command.Quantity, command.UnitPrice);
        return Tx(async (c, t) =>
        {
            if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                var replay = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                    "select id from agro360.fleet_refuelings where tenant_id=@TenantId and idempotency_key=@Key",
                    new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
                if (replay is Guid found) return found;
            }
            var asset = await EnsureAssetAsync(c, t, command.AssetId, ct);
            if (command.Odometer is not null) FleetRules.ValidateMeterChange(asset.Odometer, command.Odometer.Value, command.MeterJustification, meterOverride);
            if (command.HourMeter is not null) FleetRules.ValidateMeterChange(asset.HourMeter, command.HourMeter.Value, command.MeterJustification, meterOverride);
            if (asset.FuelCapacity is { } capacity && command.Quantity > capacity && !command.TankFull)
                throw new DomainException("Quantidade excede a capacidade do tanque. Marque tanque completo ou autorize exceção.", "fleet.tank_capacity");
            var id = Guid.CreateVersion7();
            Guid? movementId = null;
            if (source == "INTERNAL")
            {
                if (command.WarehouseId is null || command.ProductId is null)
                    throw new DomainException("Abastecimento interno exige depósito e produto.", "fleet.internal_stock_required");
                var balance = await LockStockAsync(c, t, command.WarehouseId.Value, command.ProductId.Value, "l", ct);
                if (balance.Available < command.Quantity)
                    throw new ConflictException("Estoque de combustível insuficiente.", "agro360.inventory_insufficient_stock");
                movementId = Guid.CreateVersion7();
                var newBalance = balance.Available - command.Quantity;
                await c.ExecuteAsync(new CommandDefinition(
                    """
                    update agro360.inventory_stock_balances set available=@Balance, version=version+1, updated_at=now()
                    where id=@Id and tenant_id=@TenantId and version=@Version;
                    insert into agro360.inventory_stock_movements
                        (id,tenant_id,warehouse_id,product_id,movement_type,quantity,unit,unit_cost,total_cost,lot_number,reference_type,reference_id,
                         balance_after,average_cost_after,balance_version,occurred_at,created_by,idempotency_key)
                    values (@Movement,@TenantId,@Warehouse,@Product,'CONSUMPTION',@Quantity,'l',@Cost,@Total,@Lot,'REFUELING',@Refuel,
                            @Balance,@Cost,@NewVersion,@At,@UserId,@IdempotencyKey)
                    """,
                    new
                    {
                        Balance = newBalance,
                        Id = balance.Id,
                        tenant.TenantId,
                        balance.Version,
                        Movement = movementId,
                        Warehouse = command.WarehouseId,
                        Product = command.ProductId,
                        command.Quantity,
                        Cost = balance.AverageCost,
                        Total = total,
                        Lot = command.LotNumber,
                        Refuel = id,
                        At = command.OccurredAt,
                        tenant.UserId,
                        IdempotencyKey = string.IsNullOrWhiteSpace(command.IdempotencyKey) ? null : $"fuel:{command.IdempotencyKey}",
                        NewVersion = balance.Version + 1
                    }, t, cancellationToken: ct));
            }
            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.fleet_refuelings
                    (id,tenant_id,asset_id,operator_id,fuel_type_id,quantity,unit_price,total_value,occurred_at,odometer,hour_meter,
                     location,property_id,notes,status,source,warehouse_id,product_id,lot_number,tank_full,idempotency_key,stock_movement_id,created_by,updated_by)
                values (@Id,@TenantId,@AssetId,@OperatorId,@FuelTypeId,@Quantity,@UnitPrice,@Total,@At,@Odometer,@HourMeter,
                        @Location,@PropertyId,@Notes,'ACTIVE',@Source,@WarehouseId,@ProductId,@LotNumber,@TankFull,@IdempotencyKey,@Movement,@UserId,@UserId);
                insert into agro360.fleet_operational_costs(id,tenant_id,asset_id,cost_type,value,occurred_on,property_id,origin_type,origin_id,status,created_by,updated_by)
                values (gen_random_uuid(),@TenantId,@AssetId,'FUEL',@Total,@At::date,@PropertyId,'REFUELING',@Id,'ACTIVE',@UserId,@UserId)
                on conflict (tenant_id,origin_type,origin_id) do nothing;
                update agro360.fleet_assets
                set odometer=greatest(odometer,coalesce(@Odometer,odometer)), hour_meter=greatest(hour_meter,coalesce(@HourMeter,hour_meter)),
                    updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@AssetId
                """,
                new
                {
                    Id = id,
                    tenant.TenantId,
                    command.AssetId,
                    command.OperatorId,
                    command.FuelTypeId,
                    command.Quantity,
                    command.UnitPrice,
                    Total = total,
                    At = command.OccurredAt,
                    command.Odometer,
                    command.HourMeter,
                    command.Location,
                    command.PropertyId,
                    command.Notes,
                    Source = source,
                    command.WarehouseId,
                    command.ProductId,
                    LotNumber = command.LotNumber,
                    command.TankFull,
                    command.IdempotencyKey,
                    Movement = movementId,
                    tenant.UserId
                }, t, cancellationToken: ct));
            await Audit(c, t, "refuel", "Asset", command.AssetId, command, ct);
            return id;
        }, ct);
    }

    public Task<IReadOnlyList<dynamic>> ListRefuelingsAsync(Guid? assetId, CancellationToken ct) =>
        List("""
            select r.id, r.asset_id as "assetId", a.name as "assetName", r.quantity, r.unit_price as "unitPrice", r.total_value as "totalValue",
                   r.occurred_at as "occurredAt", r.source, r.tank_full as "tankFull", r.notes
            from agro360.fleet_refuelings r
            join agro360.fleet_assets a on a.id=r.asset_id and a.tenant_id=r.tenant_id
            where r.tenant_id=@TenantId and r.deleted_at is null and (@AssetId is null or r.asset_id=@AssetId)
            order by r.occurred_at desc limit 200
            """, ct, new { AssetId = assetId });

    public Task<IReadOnlyList<dynamic>> CostSummaryAsync(Guid? assetId, DateOnly? from, DateOnly? until, CancellationToken ct) =>
        List("""
            select cost_type as "costType", sum(value) as value, count(*) as entries,
                   min(occurred_on) as "periodStart", max(occurred_on) as "periodEnd"
            from agro360.fleet_operational_costs
            where tenant_id=@TenantId and status='ACTIVE'
              and (@AssetId is null or asset_id=@AssetId)
              and (@From is null or occurred_on>=@From)
              and (@Until is null or occurred_on<=@Until)
            group by cost_type
            order by cost_type
            """, ct, new { AssetId = assetId, From = from, Until = until });

    public Task<byte[]> ExportCsvAsync(FleetExportFilter filter, CancellationToken ct) => Tx(async (c, t) =>
    {
        var kind = string.IsNullOrWhiteSpace(filter.Kind) ? "assets" : filter.Kind.ToLowerInvariant();
        var sql = kind switch
        {
            "assets" => "select coalesce(internal_code,code) codigo, name, status, cadastral_status, plate, odometer, hour_meter from agro360.fleet_assets where tenant_id=@TenantId and deleted_at is null order by name",
            "orders" => "select w.code, a.name ativo, w.type, w.priority, w.status, w.due_at::text from agro360.fleet_work_orders w join agro360.fleet_assets a on a.id=w.asset_id where w.tenant_id=@TenantId and w.deleted_at is null order by w.opened_at desc",
            "refuelings" => "select a.name ativo, r.source, r.quantity::text, r.total_value::text, r.occurred_at::text from agro360.fleet_refuelings r join agro360.fleet_assets a on a.id=r.asset_id where r.tenant_id=@TenantId and r.deleted_at is null order by r.occurred_at desc",
            _ => throw new DomainException("Relatório não suportado.", "fleet.report_invalid")
        };
        var rows = (await c.QueryAsync(new CommandDefinition(sql, new { tenant.TenantId }, t, cancellationToken: ct))).ToArray();
        var csv = new StringBuilder();
        if (rows.Length == 0)
        {
            csv.AppendLine("mensagem");
            csv.AppendLine(EscapeCsv("Nenhum registro autorizado."));
            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        }
        var names = ((IDictionary<string, object>)rows[0]).Keys.ToArray();
        csv.AppendLine(string.Join(';', names.Select(EscapeCsv)));
        foreach (var row in rows)
        {
            var map = (IDictionary<string, object>)row;
            csv.AppendLine(string.Join(';', names.Select(n => EscapeCsv(Convert.ToString(map[n], CultureInfo.InvariantCulture)))));
        }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }, ct);

    private static void LivestockPositive(decimal value)
    {
        if (value <= 0) throw new DomainException("Quantidade deve ser positiva.", "fleet.quantity_positive");
    }

    private async Task<AssetRow> EnsureAssetAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid id, CancellationToken ct)
    {
        var asset = await c.QuerySingleOrDefaultAsync<AssetRow>(new CommandDefinition(
            """
            select id, status, coalesce(cadastral_status,'ACTIVE') as CadastralStatus, odometer as Odometer, hour_meter as HourMeter,
                   fuel_capacity as FuelCapacity
            from agro360.fleet_assets where tenant_id=@TenantId and id=@Id and deleted_at is null for update
            """, new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        return asset ?? throw new NotFoundException("Ativo", id);
    }

    private async Task<OrderRow> EnsureOpenOrderAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid id, CancellationToken ct)
    {
        var order = await c.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition(
            "select id, asset_id as AssetId, status from agro360.fleet_work_orders where tenant_id=@TenantId and id=@Id and deleted_at is null for update",
            new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        if (order is null) throw new NotFoundException("Ordem de serviço", id);
        if (order.Status is "COMPLETED" or "CANCELLED")
            throw new ConflictException("Ordem encerrada não pode receber este apontamento sem reabertura autorizada.", "fleet.order_closed");
        return order;
    }

    private async Task<StockRow> LockStockAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid warehouse, Guid product, string unit, CancellationToken ct)
    {
        var row = await c.QuerySingleOrDefaultAsync<StockRow>(new CommandDefinition(
            """
            select b.id, b.available as Available, b.reserved as Reserved, b.average_cost as AverageCost, b.version as Version, b.unit as Unit,
                   (select l.quality_status from agro360.inventory_stock_lots l
                    where l.tenant_id=b.tenant_id and l.warehouse_id=b.warehouse_id and l.product_id=b.product_id
                    order by case when l.quality_status='APPROVED' then 0 else 1 end limit 1) as QualityStatus
            from agro360.inventory_stock_balances b
            where b.tenant_id=@TenantId and b.warehouse_id=@Warehouse and b.product_id=@Product
            for update of b
            """, new { tenant.TenantId, Warehouse = warehouse, Product = product }, t, cancellationToken: ct))
            ?? throw new ConflictException("Saldo de estoque não encontrado.", "agro360.inventory_insufficient_stock");
        if (!string.Equals(row.Unit, unit, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Unidade incompatível. Informe conversão explícita.", "agro360.inventory_unit_mismatch");
        return row;
    }

    private static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        FleetRules.EnsureCsvSafe(ref text);
        var escaped = text.Replace("\"", "\"\"", StringComparison.Ordinal);
        return escaped.Contains(';', StringComparison.Ordinal) || escaped.Contains('"', StringComparison.Ordinal) || escaped.Contains('\n', StringComparison.Ordinal)
            ? $"\"{escaped}\"" : escaped;
    }

    private Task<IReadOnlyList<dynamic>> List(string sql, CancellationToken ct, object? extra = null) =>
        Tx(async (c, t) =>
        {
            var args = new DynamicParameters(extra ?? new { });
            args.Add("TenantId", tenant.TenantId);
            args.Add("UserId", tenant.UserId);
            return (IReadOnlyList<dynamic>)(await c.QueryAsync(new CommandDefinition(sql, args, t, cancellationToken: ct))).AsList();
        }, ct);

    private Task Audit(NpgsqlConnection c, NpgsqlTransaction t, string action, string entity, Guid id, object value, CancellationToken ct) =>
        c.WriteAuditAsync(t, tenant, action, entity, id, null, value, ct);

    private Task<T> Tx<T>(Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> work, CancellationToken ct) =>
        db.InTenantTransactionAsync(work, ct);
    private Task Tx(Func<NpgsqlConnection, NpgsqlTransaction, Task> work, CancellationToken ct) =>
        db.InTenantTransactionAsync(work, ct);

    private sealed class AssetRow
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "AVAILABLE";
        public string CadastralStatus { get; set; } = "ACTIVE";
        public decimal Odometer { get; set; }
        public decimal HourMeter { get; set; }
        public decimal? FuelCapacity { get; set; }
    }
    private sealed class OrderRow
    {
        public Guid Id { get; set; }
        public Guid AssetId { get; set; }
        public string Status { get; set; } = "OPEN";
    }
    private sealed class PartLine
    {
        public Guid Id { get; set; }
        public Guid WorkOrderId { get; set; }
        public Guid? ProductId { get; set; }
        public Guid? WarehouseId { get; set; }
        public string? Unit { get; set; }
        public decimal ReservedQuantity { get; set; }
        public decimal ConsumedQuantity { get; set; }
        public decimal ReturnedQuantity { get; set; }
        public decimal LostQuantity { get; set; }
        public decimal UnitCost { get; set; }
    }
    private sealed class StockRow
    {
        public Guid Id { get; set; }
        public decimal Available { get; set; }
        public decimal Reserved { get; set; }
        public decimal AverageCost { get; set; }
        public long Version { get; set; }
        public string Unit { get; set; } = "unit";
        public string? QualityStatus { get; set; }
    }
    private sealed class ReadingNeighbor
    {
        public decimal PhysicalValue { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
        public decimal Accumulated { get; set; }
    }
}
