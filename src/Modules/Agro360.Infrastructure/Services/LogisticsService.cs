using Agro360.Application.Contracts;
using Agro360.Domain.Storage;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
namespace Agro360.Infrastructure.Services;

public sealed class LogisticsService(
    DatabaseExecutor db,
    ITenantContext tenant,
    ILogger<LogisticsService> logger,
    IOperationalInspectionTrigger inspectionTrigger) : ILogisticsService
{
    public Task<IReadOnlyList<dynamic>> ListAsync(CancellationToken ct) => Tx<IReadOnlyList<dynamic>>(async (c, t) => (await c.QueryAsync(new CommandDefinition("select * from agro360.logistics_trips where tenant_id=@TenantId order by created_at desc", new { tenant.TenantId }, t, cancellationToken: ct))).AsList());
    public Task<Guid> SaveAsync(Guid? id, TripCommand command, CancellationToken ct) { StorageRules.Freight(command.FreightValue, command.EstimatedDistance, command.Tonnes); return Tx(async (c, t) => { var key = id ?? Guid.CreateVersion7(); var p = new DynamicParameters(command); p.Add("Id", key); p.Add("TenantId", tenant.TenantId); p.Add("UserId", tenant.UserId); var sql = id is null ? "insert into agro360.logistics_trips(id,tenant_id,number,shipment_id,origin,destination,estimated_distance,carrier,driver,vehicle,freight_type,freight_value,cost_per_tonne,cost_per_km,status,created_by) values(@Id,@TenantId,@Number,@ShipmentId,@Origin,@Destination,@EstimatedDistance,@Carrier,@Driver,@Vehicle,@FreightType,@FreightValue,case when @Tonnes=0 then 0 else @FreightValue/@Tonnes end,case when @EstimatedDistance=0 then 0 else @FreightValue/@EstimatedDistance end,@Status,@UserId)" : "update agro360.logistics_trips set number=@Number,shipment_id=@ShipmentId,origin=@Origin,destination=@Destination,estimated_distance=@EstimatedDistance,carrier=@Carrier,driver=@Driver,vehicle=@Vehicle,freight_type=@FreightType,freight_value=@FreightValue,cost_per_tonne=case when @Tonnes=0 then 0 else @FreightValue/@Tonnes end,cost_per_km=case when @EstimatedDistance=0 then 0 else @FreightValue/@EstimatedDistance end,status=@Status,updated_at=now() where tenant_id=@TenantId and id=@Id"; if (await c.ExecuteAsync(new CommandDefinition(sql, p, t, cancellationToken: ct)) == 0) throw new NotFoundException("Viagem", key); await Audit(c, t, id is null ? "create" : "update", key, command, ct); return key; }); }
    public Task AddOccurrenceAsync(Guid id, TripOccurrenceCommand command, CancellationToken ct) { if (string.IsNullOrWhiteSpace(command.Description)) throw new DomainException("Descrição da ocorrência é obrigatória."); return Tx(async (c, t) => { await c.ExecuteAsync(new CommandDefinition("insert into agro360.logistics_trip_occurrences(id,tenant_id,trip_id,description,created_by) select @Occurrence,@TenantId,@Id,@Description,@UserId where exists(select 1 from agro360.logistics_trips where tenant_id=@TenantId and id=@Id); update agro360.logistics_trips set status='WITH_OCCURRENCE',updated_at=now() where tenant_id=@TenantId and id=@Id", new { Occurrence = Guid.CreateVersion7(), tenant.TenantId, Id = id, command.Description, tenant.UserId }, t, cancellationToken: ct)); await Audit(c, t, "occurrence", id, command, ct); }); }
    public Task CompleteAsync(Guid id, CancellationToken ct) => Tx(async (c, t) => { if (await c.ExecuteAsync(new CommandDefinition("update agro360.logistics_trips set status='DELIVERED',delivered_at=now(),updated_at=now() where tenant_id=@TenantId and id=@Id and status not in('DELIVERED','CANCELLED')", new { tenant.TenantId, Id = id }, t, cancellationToken: ct)) == 0) throw new ConflictException("Viagem não pode ser concluída."); await Audit(c, t, "complete", id, new { }, ct); });
    public Task<IReadOnlyList<dynamic>> FulfillmentQueueAsync(string? customer, Guid? unitId, DateOnly? dueUntil, string? status, CancellationToken ct) => Tx<IReadOnlyList<dynamic>>(async (c, t) => (await c.QueryAsync(new CommandDefinition("""
        select o.id order_id,o.order_number,o.status,c.name customer,o.property_id unit_id,o.expected_delivery,
               count(i.id) item_count,sum(i.quantity-coalesce(r.reserved,0)) quantity_pending
        from agro360.sales_orders o join agro360.crm_customers c on c.tenant_id=o.tenant_id and c.id=o.customer_id
        join agro360.sales_order_items i on i.tenant_id=o.tenant_id and i.order_id=o.id
        left join lateral(select sum(case when status='ACTIVE' then quantity else consumed_quantity end) reserved from agro360.fulfillment_reservations r where r.tenant_id=o.tenant_id and r.order_item_id=i.id and r.status in('ACTIVE','CONSUMED','RELEASED')) r on true
        where o.tenant_id=@TenantId and o.deleted_at is null and o.status in('APPROVED','FULFILLMENT')
          and (@Customer is null or c.name ilike '%'||@Customer||'%') and (@UnitId is null or o.property_id=@UnitId)
          and (@DueUntil is null or o.expected_delivery<=@DueUntil) and (@Status is null or o.status=@Status)
        group by o.id,c.name having sum(i.quantity-coalesce(r.reserved,0))>0 order by o.expected_delivery nulls last,o.created_at
        """, new { tenant.TenantId, Customer = string.IsNullOrWhiteSpace(customer) ? null : customer, UnitId = unitId, DueUntil = dueUntil, Status = string.IsNullOrWhiteSpace(status) ? null : status }, t, cancellationToken: ct))).AsList());

    public Task<FulfillmentIndicators> FulfillmentIndicatorsAsync(CancellationToken ct) => Tx(async (c, t) => await c.QuerySingleAsync<FulfillmentIndicators>(new CommandDefinition("""
        select
        (select count(distinct o.id) from agro360.sales_orders o where o.tenant_id=@TenantId and o.deleted_at is null and o.status in('APPROVED','FULFILLMENT')) awaiting_picking,
        count(*) filter(where s.status='CHECKED') ready,
        (select count(*) from agro360.logistics_trips x where x.tenant_id=@TenantId and x.status='IN_TRANSIT') trips_in_progress,
        count(*) filter(where s.status not in('RECONCILED','CANCELLED') and o.expected_delivery<current_date) late,
        count(*) filter(where s.status='PARTIAL') partial,
        coalesce((select count(*) from agro360.fulfillment_delivery_attempt_items a join agro360.fulfillment_delivery_attempts d on d.tenant_id=a.tenant_id and d.id=a.attempt_id where a.tenant_id=@TenantId and a.refused_quantity>0),0) refusals,
        coalesce((select count(*) from agro360.fulfillment_returns r where r.tenant_id=@TenantId and r.status='AWAITING_QUALITY'),0) returns_awaiting_quality,
        coalesce((select count(*) from agro360.fulfillment_shipment_items i where i.tenant_id=@TenantId and i.divergence_reason is not null and i.accepted_quantity+i.returned_quantity+i.lost_quantity<i.checked_quantity),0) untreated_divergences
        from agro360.fulfillment_shipments s left join agro360.fulfillment_shipment_items i on i.tenant_id=s.tenant_id and i.shipment_id=s.id
        left join agro360.sales_order_items oi on oi.tenant_id=i.tenant_id and oi.id=i.order_item_id left join agro360.sales_orders o on o.tenant_id=oi.tenant_id and o.id=oi.order_id
        where s.tenant_id=@TenantId and s.deleted_at is null
        """, new { tenant.TenantId }, t, cancellationToken: ct)));
    public Task<dynamic?> FulfillmentDetailAsync(Guid id, CancellationToken ct) => Tx<dynamic?>(async (c, t) =>
    {
        var shipment = await c.QuerySingleOrDefaultAsync(new CommandDefinition("select * from agro360.fulfillment_shipments where tenant_id=@TenantId and id=@Id and deleted_at is null", new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        if (shipment is null) return null;
        var items = (await c.QueryAsync(new CommandDefinition("""select i.*,o.order_number,p.name product,l.lot_number from agro360.fulfillment_shipment_items i join agro360.sales_order_items oi on oi.tenant_id=i.tenant_id and oi.id=i.order_item_id join agro360.sales_orders o on o.tenant_id=oi.tenant_id and o.id=oi.order_id join agro360.inventory_products p on p.tenant_id=oi.tenant_id and p.id=oi.product_id join agro360.inventory_stock_lots l on l.tenant_id=i.tenant_id and l.id=i.stock_lot_id where i.tenant_id=@TenantId and i.shipment_id=@Id""", new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).AsList();
        var attempts = (await c.QueryAsync(new CommandDefinition("select * from agro360.fulfillment_delivery_attempts where tenant_id=@TenantId and shipment_id=@Id order by occurred_at,recorded_at", new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).AsList();
        var returns = (await c.QueryAsync(new CommandDefinition("select r.* from agro360.fulfillment_returns r join agro360.fulfillment_shipment_items i on i.tenant_id=r.tenant_id and i.id=r.shipment_item_id where r.tenant_id=@TenantId and i.shipment_id=@Id", new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).AsList();
        return new { shipment, items, attempts, returns };
    });
    public Task<Guid> CreateFulfillmentAsync(CreateFulfillmentCommand command, CancellationToken ct)
    {
        if (command.Items.Count == 0 || command.Items.Any(x => x.Quantity <= 0 || x.PickedQuantity <= 0 || x.CheckedQuantity <= 0 || x.PickedQuantity > x.Quantity || x.CheckedQuantity > x.PickedQuantity)) throw new DomainException("Quantidades de reserva, separação e conferência são inválidas.");
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey)) throw new DomainException("Chave de idempotência é obrigatória.");
        var hash = Hash(command);
        return Tx(async (c, t) =>
        {
            var previous = await c.QuerySingleOrDefaultAsync<(Guid Id, string RequestHash)>(new CommandDefinition("select id,request_hash requesthash from agro360.fulfillment_shipments where tenant_id=@TenantId and idempotency_key=@Key", new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
            if (previous.Id != Guid.Empty) { if (previous.RequestHash != hash) throw new ConflictException("Chave de idempotência reutilizada com conteúdo diferente."); return previous.Id; }
            var id = Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.fulfillment_shipments(id,tenant_id,number,origin_warehouse_id,destination,customer_id,status,idempotency_key,request_hash,created_by,updated_by) values(@Id,@TenantId,@Number,@Warehouse,@Destination,@Customer,'CHECKED',@Key,@Hash,@UserId,@UserId)", new { Id = id, tenant.TenantId, command.Number, Warehouse = command.OriginWarehouseId, command.Destination, Customer = command.CustomerId, Key = command.IdempotencyKey, Hash = hash, tenant.UserId }, t, cancellationToken: ct));
            foreach (var item in command.Items)
            {
                await c.ExecuteAsync(new CommandDefinition("select pg_advisory_xact_lock(hashtextextended(@LockKey,0))", new { LockKey = $"{tenant.TenantId}:{item.StockLotId}" }, t, cancellationToken: ct));
                var available = await c.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition("""
                    select least(
                        l.quantity-coalesce((select sum(r.quantity) from agro360.fulfillment_reservations r where r.tenant_id=l.tenant_id and r.stock_lot_id=l.id and r.status='ACTIVE'),0),
                        oi.quantity-coalesce((select sum(case when r.status='ACTIVE' then r.quantity else r.consumed_quantity end) from agro360.fulfillment_reservations r where r.tenant_id=oi.tenant_id and r.order_item_id=oi.id and r.status in('ACTIVE','CONSUMED','RELEASED')),0))
                    from agro360.inventory_stock_lots l
                    join agro360.sales_order_items oi on oi.tenant_id=l.tenant_id and oi.id=@OrderItemId and oi.product_id=l.product_id
                    join agro360.sales_orders o on o.tenant_id=oi.tenant_id and o.id=oi.order_id and o.customer_id=@CustomerId and o.status in('APPROVED','FULFILLMENT')
                    where l.tenant_id=@TenantId and l.id=@LotId and l.warehouse_id=@WarehouseId and lower(oi.unit)=lower(@Unit)
                      and l.quality_status='APPROVED' and (l.expires_on is null or l.expires_on>=current_date)
                    """, new { tenant.TenantId, item.OrderItemId, LotId = item.StockLotId, WarehouseId = command.OriginWarehouseId, command.CustomerId, item.Unit }, t, cancellationToken: ct));
                if (available is null || available < item.Quantity) throw new ConflictException("Pedido, unidade, lote ou saldo não está apto à expedição.");
                var balanceReserved = await c.ExecuteAsync(new CommandDefinition("update agro360.inventory_stock_balances b set reserved=reserved+@Quantity,version=version+1,updated_at=now() from agro360.inventory_stock_lots l where b.tenant_id=@TenantId and l.tenant_id=b.tenant_id and l.id=@LotId and b.warehouse_id=l.warehouse_id and b.product_id=l.product_id and b.available-b.reserved>=@Quantity", new { tenant.TenantId, LotId = item.StockLotId, item.Quantity }, t, cancellationToken: ct));
                if (balanceReserved == 0) throw new ConflictException("Saldo autorizado mudou durante a reserva.");
                var reservation = Guid.CreateVersion7();
                await c.ExecuteAsync(new CommandDefinition("insert into agro360.fulfillment_reservations(id,tenant_id,order_item_id,stock_lot_id,quantity,unit,status,idempotency_key,request_hash,created_by,updated_by) values(@Id,@TenantId,@OrderItemId,@LotId,@Quantity,@Unit,'ACTIVE',@Key,@Hash,@UserId,@UserId)", new { Id = reservation, tenant.TenantId, item.OrderItemId, LotId = item.StockLotId, item.Quantity, item.Unit, Key = $"{command.IdempotencyKey}:{item.OrderItemId}:{item.StockLotId}", Hash = hash, tenant.UserId }, t, cancellationToken: ct));
                await c.ExecuteAsync(new CommandDefinition("insert into agro360.fulfillment_shipment_items(id,tenant_id,shipment_id,reservation_id,order_item_id,stock_lot_id,requested_quantity,reserved_quantity,picked_quantity,checked_quantity,unit,divergence_reason,created_by,updated_by) values(@Id,@TenantId,@ShipmentId,@ReservationId,@OrderItemId,@LotId,@Quantity,@Quantity,@Picked,@Checked,@Unit,@Reason,@UserId,@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, ShipmentId = id, ReservationId = reservation, item.OrderItemId, LotId = item.StockLotId, item.Quantity, Picked = item.PickedQuantity, Checked = item.CheckedQuantity, item.Unit, Reason = item.CheckedQuantity == item.Quantity ? null : item.DivergenceReason, tenant.UserId }, t, cancellationToken: ct));
            }
            if (command.Items.Any(x => x.CheckedQuantity != x.Quantity && string.IsNullOrWhiteSpace(x.DivergenceReason))) throw new DomainException("Diferença de conferência exige tratamento explícito.");
            await c.ExecuteAsync(new CommandDefinition("update agro360.sales_orders set status='FULFILLMENT',updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id in(select oi.order_id from agro360.sales_order_items oi join agro360.fulfillment_shipment_items si on si.tenant_id=oi.tenant_id and si.order_item_id=oi.id where si.shipment_id=@Id)", new { Id = id, tenant.TenantId, tenant.UserId }, t, cancellationToken: ct));
            await Audit(c, t, "fulfillment.create", id, command, ct); return id;
        });
    }
    public Task DispatchFulfillmentAsync(Guid id, DispatchFulfillmentCommand command, CancellationToken ct) => Tx(async (c, t) =>
    {
        var shipment = await c.QuerySingleOrDefaultAsync<(string Status, long Version)>(new CommandDefinition("select status,version from agro360.fulfillment_shipments where tenant_id=@TenantId and id=@Id for update", new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        if (shipment.Status == "DISPATCHED" || shipment.Status == "IN_DELIVERY") return;
        if (shipment.Status != "CHECKED" || shipment.Version != command.Version) throw new ConflictException("Expedição foi alterada ou não está conferida.");
        var items = (await c.QueryAsync<(Guid Id, Guid LotId, Guid ReservationId, decimal Quantity, decimal ReservedQuantity, string Unit)>(new CommandDefinition("select id,stock_lot_id lot_id,reservation_id,checked_quantity quantity,reserved_quantity,unit from agro360.fulfillment_shipment_items where tenant_id=@TenantId and shipment_id=@Id order by id for update", new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).AsList();
        foreach (var item in items)
        {
            var balanceChanged = await c.ExecuteAsync(new CommandDefinition("update agro360.inventory_stock_balances b set available=available-@Quantity,reserved=reserved-@ReservedQuantity,version=version+1,updated_at=now() from agro360.inventory_stock_lots l where b.tenant_id=@TenantId and l.tenant_id=b.tenant_id and l.id=@LotId and b.warehouse_id=l.warehouse_id and b.product_id=l.product_id and b.available>=@Quantity and b.reserved>=@ReservedQuantity", new { tenant.TenantId, item.LotId, item.Quantity, item.ReservedQuantity }, t, cancellationToken: ct));
            if (balanceChanged == 0) throw new ConflictException("Saldo autorizado mudou após a separação.");
            var changed = await c.ExecuteAsync(new CommandDefinition("update agro360.inventory_stock_lots set quantity=quantity-@Quantity where tenant_id=@TenantId and id=@LotId and quantity>=@Quantity and quality_status='APPROVED' and (expires_on is null or expires_on>=current_date)", new { tenant.TenantId, item.LotId, item.Quantity }, t, cancellationToken: ct));
            if (changed == 0) throw new ConflictException("Saldo ou qualidade do lote mudou após a separação.");
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.inventory_stock_movements(id,tenant_id,warehouse_id,product_id,movement_type,quantity,unit,unit_cost,total_cost,lot_number,reference_type,reference_id,idempotency_key,balance_after,average_cost_after,balance_version,occurred_at,created_by) select @Movement,@TenantId,l.warehouse_id,l.product_id,'SALE',@Quantity,@Unit,b.average_cost,round(@Quantity*b.average_cost,4),l.lot_number,'FULFILLMENT_SHIPMENT',@Shipment,@Key,b.available,b.average_cost,b.version,now(),@UserId from agro360.inventory_stock_lots l join agro360.inventory_stock_balances b on b.tenant_id=l.tenant_id and b.warehouse_id=l.warehouse_id and b.product_id=l.product_id where l.tenant_id=@TenantId and l.id=@LotId and not exists(select 1 from agro360.inventory_stock_movements m where m.tenant_id=@TenantId and m.idempotency_key=@Key)", new { Movement = Guid.CreateVersion7(), tenant.TenantId, item.LotId, item.Quantity, item.Unit, Shipment = id, Key = $"fulfillment:{id}:{item.Id}", tenant.UserId }, t, cancellationToken: ct));
            await c.ExecuteAsync(new CommandDefinition("update agro360.fulfillment_reservations set status=case when @Quantity=quantity then 'CONSUMED' else 'RELEASED' end,consumed_quantity=@Quantity,released_quantity=quantity-@Quantity,version=version+1,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Reservation and status='ACTIVE'", new { tenant.TenantId, Reservation = item.ReservationId, item.Quantity, tenant.UserId }, t, cancellationToken: ct));
        }
        await c.ExecuteAsync(new CommandDefinition("update agro360.fulfillment_shipments set status='DISPATCHED',dispatched_at=now(),version=version+1,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id", new { tenant.TenantId, Id = id, tenant.UserId }, t, cancellationToken: ct));
        await Audit(c, t, "fulfillment.dispatch", id, new { command.IdempotencyKey }, ct);
    });
    public Task<Guid> RecordDeliveryAttemptAsync(Guid id, DeliveryAttemptCommand command, CancellationToken ct)
    {
        if (command.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(5) || command.Items.Count == 0) throw new DomainException("Tentativa de entrega inválida.");
        var hash = Hash(command);
        return Tx(async (c, t) =>
        {
            var old = await c.QuerySingleOrDefaultAsync<(Guid Id, string RequestHash)>(new CommandDefinition("select id,request_hash requesthash from agro360.fulfillment_delivery_attempts where tenant_id=@TenantId and idempotency_key=@Key", new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
            if (old.Id != Guid.Empty) { if (old.RequestHash != hash) throw new ConflictException("Chave de idempotência reutilizada com conteúdo diferente."); return old.Id; }
            var valid = await c.QuerySingleOrDefaultAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.fulfillment_shipments where tenant_id=@TenantId and id=@Id and status in('DISPATCHED','IN_DELIVERY','PARTIAL','RETURN_PENDING') for update)", new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
            if (!valid) throw new ConflictException("Expedição não está em entrega.");
            var attempt = Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.fulfillment_delivery_attempts(id,tenant_id,shipment_id,occurred_at,destination,responsible_id,status,reason,evidence_document_id,evidence_pending,pending_notes,idempotency_key,request_hash,created_by) values(@Id,@TenantId,@Shipment,@OccurredAt,@Destination,@Responsible,@Status,@Reason,@Evidence,@EvidencePending,@PendingNotes,@Key,@Hash,@UserId)", new { Id = attempt, tenant.TenantId, Shipment = id, command.OccurredAt, command.Destination, Responsible = command.ResponsibleId, command.Status, command.Reason, Evidence = command.EvidenceDocumentId, command.EvidencePending, command.PendingNotes, Key = command.IdempotencyKey, Hash = hash, tenant.UserId }, t, cancellationToken: ct));
            foreach (var item in command.Items)
            {
                if (item.AcceptedQuantity < 0 || item.RefusedQuantity < 0 || item.AcceptedQuantity + item.RefusedQuantity <= 0) throw new DomainException("Quantidades da tentativa são inválidas.");
                var changed = await c.ExecuteAsync(new CommandDefinition("update agro360.fulfillment_shipment_items set accepted_quantity=accepted_quantity+@Accepted,refused_quantity=refused_quantity+@Refused,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Item and shipment_id=@Shipment and accepted_quantity+refused_quantity+returned_quantity+lost_quantity+@Accepted+@Refused<=checked_quantity", new { tenant.TenantId, Item = item.ShipmentItemId, Shipment = id, Accepted = item.AcceptedQuantity, Refused = item.RefusedQuantity, tenant.UserId }, t, cancellationToken: ct));
                if (changed == 0) throw new ConflictException("A tentativa repete quantidade aceita ou excede o saldo em trânsito.");
                await c.ExecuteAsync(new CommandDefinition("insert into agro360.fulfillment_delivery_attempt_items(id,tenant_id,attempt_id,shipment_item_id,accepted_quantity,refused_quantity,reason,created_by) values(@Id,@TenantId,@Attempt,@Item,@Accepted,@Refused,@Reason,@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, Attempt = attempt, Item = item.ShipmentItemId, Accepted = item.AcceptedQuantity, Refused = item.RefusedQuantity, item.Reason, tenant.UserId }, t, cancellationToken: ct));
            }
            await c.ExecuteAsync(new CommandDefinition("update agro360.fulfillment_shipments s set status=case when not exists(select 1 from agro360.fulfillment_shipment_items i where i.tenant_id=s.tenant_id and i.shipment_id=s.id and i.accepted_quantity+i.returned_quantity+i.lost_quantity<i.checked_quantity) then 'RECONCILED' when exists(select 1 from agro360.fulfillment_shipment_items i where i.tenant_id=s.tenant_id and i.shipment_id=s.id and i.refused_quantity>i.returned_quantity) then 'RETURN_PENDING' else 'PARTIAL' end,version=version+1,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id", new { tenant.TenantId, Id = id, tenant.UserId }, t, cancellationToken: ct));
            await Audit(c, t, "delivery.attempt", id, command, ct); return attempt;
        });
    }
    public Task<Guid> RegisterReturnAsync(ReturnCommand command, CancellationToken ct)
    {
        if (command.Quantity <= 0 || string.IsNullOrWhiteSpace(command.Reason)) throw new DomainException("Retorno exige quantidade e motivo.");
        var hash = Hash(command);
        return Tx(async (c, t) =>
        {
            var old = await c.QuerySingleOrDefaultAsync<(Guid Id, string RequestHash)>(new CommandDefinition("select id,request_hash requesthash from agro360.fulfillment_returns where tenant_id=@TenantId and idempotency_key=@Key", new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
            if (old.Id != Guid.Empty) { if (old.RequestHash != hash) throw new ConflictException("Chave de idempotência reutilizada com conteúdo diferente."); return old.Id; }
            var available = await c.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition("select refused_quantity-returned_quantity from agro360.fulfillment_shipment_items where tenant_id=@TenantId and id=@Id for update", new { tenant.TenantId, Id = command.ShipmentItemId }, t, cancellationToken: ct));
            if (available is null || available < command.Quantity) throw new ConflictException("Quantidade de retorno excede a recusa pendente.");
            var id = Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.fulfillment_returns(id,tenant_id,shipment_item_id,quantity,status,reason,idempotency_key,request_hash,created_by,updated_by) values(@Id,@TenantId,@Item,@Quantity,'AWAITING_RECEIPT',@Reason,@Key,@Hash,@UserId,@UserId)", new { Id = id, tenant.TenantId, Item = command.ShipmentItemId, command.Quantity, command.Reason, Key = command.IdempotencyKey, Hash = hash, tenant.UserId }, t, cancellationToken: ct));
            await Audit(c, t, "return.register", id, command, ct); return id;
        });
    }
    public async Task<Guid> ReceiveReturnAsync(Guid id, ReceiveReturnCommand command, CancellationToken ct)
    {
        if (command.Quantity <= 0 || command.WarehouseId == Guid.Empty || string.IsNullOrWhiteSpace(command.IdempotencyKey)) throw new DomainException("Quantidade, local e chave de idempotência são obrigatórios.");
        var condition=command.Condition.Trim().ToUpperInvariant(); if(condition is not ("INTACT" or "DAMAGED" or "INSPECTION_REQUIRED")) throw new DomainException("Condição física inválida."); var hash=Hash(command);
        Guid receiptId = Guid.Empty;
        bool isNewReceipt = false;
        Guid? productId = null;
        Guid? lotId = null;
        var resultId = await Tx(async(c,t)=> {
            var old=await c.QuerySingleOrDefaultAsync<(Guid Id,string RequestHash)>(new CommandDefinition("select id,request_hash requesthash from agro360.fulfillment_return_receipts where tenant_id=@TenantId and idempotency_key=@Key",new{tenant.TenantId,Key=command.IdempotencyKey},t,cancellationToken:ct));
            if(old.Id!=Guid.Empty){if(old.RequestHash!=hash)throw new ConflictException("Chave de idempotência reutilizada com conteúdo diferente.");return old.Id;}
            var item=await c.QuerySingleOrDefaultAsync<(decimal Authorized,decimal Received,long Version,string Unit,Guid ShipmentItemId,Guid? ProductId,Guid? LotId)>(new CommandDefinition("""
                select r.quantity authorized, r.received_quantity received, r.version, i.unit, i.id shipmentitemid,
                       res.product_id productid, i.stock_lot_id lotid
                from agro360.fulfillment_returns r
                join agro360.fulfillment_shipment_items i on i.tenant_id=r.tenant_id and i.id=r.shipment_item_id
                left join agro360.fulfillment_reservations res on res.tenant_id=i.tenant_id and res.id=i.reservation_id
                where r.tenant_id=@TenantId and r.id=@Id and r.status in('AWAITING_RECEIPT','PARTIALLY_RECEIVED','AWAITING_QUALITY')
                for update
                """,new{tenant.TenantId,Id=id},t,cancellationToken:ct));
            if(item==default)throw new ConflictException("Retorno inexistente ou não disponível para recebimento.");
            if(item.Version!=command.ExpectedVersion)throw new ConflictException("O retorno foi alterado. Recarregue antes de confirmar.");
            if(!string.Equals(item.Unit,command.Unit,StringComparison.OrdinalIgnoreCase))throw new DomainException("A unidade deve coincidir com a expedição; conversão não configurada.");
            if(item.Received+command.Quantity>item.Authorized)throw new ConflictException("Quantidade supera o saldo autorizado do retorno.");
            if(!await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.inventory_warehouses where tenant_id=@TenantId and id=@WarehouseId and deleted_at is null)",new{tenant.TenantId,command.WarehouseId},t,cancellationToken:ct)))throw new DomainException("Local de recebimento indisponível.");
            var receipt=Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.fulfillment_return_receipts(id,tenant_id,return_id,quantity,unit,condition,warehouse_id,lot_number,evidence_document_id,notes,idempotency_key,request_hash,created_by) values(@Receipt,@TenantId,@Id,@Quantity,@Unit,@Condition,@WarehouseId,@LotNumber,@Evidence,@Notes,@Key,@Hash,@UserId); update agro360.fulfillment_returns set received_quantity=received_quantity+@Quantity,status=case when received_quantity+@Quantity<quantity then 'PARTIALLY_RECEIVED' else 'AWAITING_QUALITY' end,received_at=coalesce(received_at,now()),version=version+1,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id; update agro360.fulfillment_shipment_items set returned_quantity=returned_quantity+@Quantity,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@ShipmentItem",new{Receipt=receipt,tenant.TenantId,Id=id,command.Quantity,Unit=command.Unit.ToLowerInvariant(),Condition=condition,command.WarehouseId,command.LotNumber,Evidence=command.EvidenceDocumentId,command.Notes,Key=command.IdempotencyKey,Hash=hash,tenant.UserId,item.ShipmentItemId},t,cancellationToken:ct));
            await Audit(c,t,"return.receive",id,command,ct);
            receiptId = receipt;
            isNewReceipt = true;
            productId = item.ProductId;
            lotId = item.LotId;
            return receipt;
        });

        if (isNewReceipt)
        {
            await inspectionTrigger.TryStartFromOriginAsync(new OperationalInspectionEventRequest(
                ProcessCode: "RETURN",
                OriginType: "fulfillment_return_receipts",
                OriginId: receiptId,
                ProductId: productId,
                LotId: lotId,
                UnitId: command.WarehouseId,
                Notes: $"Recebimento de retorno/devolução condição {condition}"), ct);
        }

        return resultId;
    }
    public Task<Guid> DecideReturnAsync(Guid id, DecideReturnCommand command, CancellationToken ct)
    {
        var decision=command.Decision.Trim().ToUpperInvariant(); if(decision is not ("RELEASE" or "BLOCK" or "DISPOSE")||command.Quantity<=0||string.IsNullOrWhiteSpace(command.Reason)||string.IsNullOrWhiteSpace(command.IdempotencyKey))throw new DomainException("Decisão, quantidade, motivo e chave são obrigatórios.");var hash=Hash(command);
        return Tx(async(c,t)=>{var old=await c.QuerySingleOrDefaultAsync<(Guid Id,string RequestHash)>(new CommandDefinition("select id,request_hash requesthash from agro360.fulfillment_return_decisions where tenant_id=@TenantId and idempotency_key=@Key",new{tenant.TenantId,Key=command.IdempotencyKey},t,cancellationToken:ct));if(old.Id!=Guid.Empty){if(old.RequestHash!=hash)throw new ConflictException("Chave de idempotência reutilizada com conteúdo diferente.");return old.Id;}var state=await c.QuerySingleOrDefaultAsync<(decimal Received,decimal Decided,long Version)>(new CommandDefinition("select r.received_quantity received,coalesce((select sum(d.quantity) from agro360.fulfillment_return_decisions d where d.tenant_id=r.tenant_id and d.return_id=r.id),0) decided,r.version from agro360.fulfillment_returns r where r.tenant_id=@TenantId and r.id=@Id and r.status in('AWAITING_QUALITY','BLOCKED') for update",new{tenant.TenantId,Id=id},t,cancellationToken:ct));if(state==default||state.Version!=command.ExpectedVersion)throw new ConflictException("Retorno alterado ou indisponível. Recarregue os dados.");if(state.Decided+command.Quantity>state.Received)throw new ConflictException("A destinação supera a quantidade fisicamente recebida.");var decisionId=Guid.CreateVersion7();await c.ExecuteAsync(new CommandDefinition("insert into agro360.fulfillment_return_decisions(id,tenant_id,return_id,decision,quantity,reason,idempotency_key,request_hash,created_by) values(@DecisionId,@TenantId,@Id,@Decision,@Quantity,@Reason,@Key,@Hash,@UserId); update agro360.fulfillment_returns set status=case when @Decision='RELEASE' and @Quantity+@Decided=received_quantity then 'RELEASED' when @Decision='DISPOSE' and @Quantity+@Decided=received_quantity then 'DISPOSED' else 'BLOCKED' end,quality_decision_at=now(),version=version+1,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id",new{DecisionId=decisionId,tenant.TenantId,Id=id,Decision=decision,command.Quantity,command.Reason,Key=command.IdempotencyKey,Hash=hash,tenant.UserId,state.Decided},t,cancellationToken:ct));await Audit(c,t,"return.decide",id,command,ct);return decisionId;});
    }
    public Task<AfterSalesPage> OccurrencesAsync(AfterSalesQuery query, CancellationToken ct) => Tx(async (c, t) =>
    {
        var page = Math.Max(1, query.Page); var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var p = new { tenant.TenantId, Search = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim(), query.CustomerId, Status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status.ToUpperInvariant(), query.AssigneeId, Type = string.IsNullOrWhiteSpace(query.Type) ? null : query.Type.ToUpperInvariant(), query.From, query.To, Take = pageSize, Skip = (page - 1) * pageSize };
        const string where = "o.tenant_id=@TenantId and o.deleted_at is null and (@Search is null or o.number::text ilike '%'||@Search||'%' or c.name ilike '%'||@Search||'%' or o.description ilike '%'||@Search||'%') and (@CustomerId is null or o.customer_id=@CustomerId) and (@Status is null or o.status=@Status) and (@AssigneeId is null or o.assignee_id=@AssigneeId) and (@Type is null or o.type=@Type) and (@From is null or o.occurred_at>=@From::date) and (@To is null or o.occurred_at<(@To::date+1))";
        var total = await c.ExecuteScalarAsync<long>(new CommandDefinition($"select count(*) from agro360.after_sales_occurrences o join agro360.crm_customers c on c.tenant_id=o.tenant_id and c.id=o.customer_id where {where}", p, t, cancellationToken: ct));
        var rows = (await c.QueryAsync(new CommandDefinition($"select o.id,'OCC-'||lpad(o.number::text,8,'0') number,c.name customer,so.order_number,o.type,o.status,o.affected_quantity,o.unit,o.occurred_at,o.recorded_at,o.due_at,coalesce(u.name,u.email) assignee,(select count(*) from agro360.after_sales_occurrences x where x.tenant_id=o.tenant_id and x.shipment_id=o.shipment_id and x.type=o.type and x.id<>o.id and x.status not in('CANCELLED','RESOLVED') and x.occurred_at between o.occurred_at-interval '7 days' and o.occurred_at+interval '7 days') similar_open from agro360.after_sales_occurrences o join agro360.crm_customers c on c.tenant_id=o.tenant_id and c.id=o.customer_id join agro360.sales_orders so on so.tenant_id=o.tenant_id and so.id=o.order_id left join agro360.identity_users u on u.tenant_id=o.tenant_id and u.id=o.assignee_id where {where} order by o.recorded_at desc,o.id desc limit @Take offset @Skip", p, t, cancellationToken: ct))).AsList();
        return new AfterSalesPage(rows, page, pageSize, total);
    });
    public Task<dynamic?> OccurrenceAsync(Guid id, CancellationToken ct) => Tx<dynamic?>(async (c, t) =>
    {
        var occurrence = await c.QuerySingleOrDefaultAsync(new CommandDefinition("select o.*,'OCC-'||lpad(o.number::text,8,'0') display_number,c.name customer,so.order_number,s.number shipment_number,coalesce(u.name,u.email) assignee from agro360.after_sales_occurrences o join agro360.crm_customers c on c.tenant_id=o.tenant_id and c.id=o.customer_id join agro360.sales_orders so on so.tenant_id=o.tenant_id and so.id=o.order_id join agro360.fulfillment_shipments s on s.tenant_id=o.tenant_id and s.id=o.shipment_id left join agro360.identity_users u on u.tenant_id=o.tenant_id and u.id=o.assignee_id where o.tenant_id=@TenantId and o.id=@Id and o.deleted_at is null", new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        if (occurrence is null) return null;
        var events = (await c.QueryAsync(new CommandDefinition("select e.*,coalesce(u.name,u.email) actor from agro360.after_sales_events e left join agro360.identity_users u on u.tenant_id=e.tenant_id and u.id=e.actor_id where e.tenant_id=@TenantId and e.occurrence_id=@Id order by e.occurred_at,e.id", new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).AsList();
        var solutions = (await c.QueryAsync(new CommandDefinition("select * from agro360.after_sales_solutions where tenant_id=@TenantId and occurrence_id=@Id order by created_at", new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).AsList();
        var returns = (await c.QueryAsync(new CommandDefinition("select r.*,r.quantity-r.received_quantity pending_quantity from agro360.fulfillment_returns r where r.tenant_id=@TenantId and r.occurrence_id=@Id order by r.created_at", new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).AsList();
        var adjustments = (await c.QueryAsync(new CommandDefinition("select *,case when status in('APPROVED','EXECUTION_PENDING') then 'Execução financeira pendente' end execution_note from agro360.after_sales_adjustments where tenant_id=@TenantId and occurrence_id=@Id order by created_at", new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).AsList();
        var replacements = (await c.QueryAsync(new CommandDefinition("select * from agro360.after_sales_replacements where tenant_id=@TenantId and occurrence_id=@Id order by created_at", new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).AsList();
        return new { occurrence, events, solutions, returns, adjustments, replacements };
    });
    public Task<Guid> CreateOccurrenceAsync(CreateOccurrenceCommand command, CancellationToken ct)
    {
        var type = command.Type.Trim().ToUpperInvariant(); var allowed = new[] { "SHORTAGE", "WRONG_PRODUCT", "DAMAGE", "REFUSAL", "QUALITY", "DELAY", "NOT_DELIVERED", "OTHER" };
        if (!allowed.Contains(type) || string.IsNullOrWhiteSpace(command.Description) || string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(5)) throw new DomainException("Tipo, descrição, data do fato e chave de idempotência devem ser válidos.");
        if (command.ShipmentItemId is null && (command.AffectedQuantity is not null || command.StockLotId is not null)) throw new DomainException("Item é obrigatório quando produto, lote ou quantidade são informados.");
        if (command.ShipmentItemId is not null && (command.AffectedQuantity is null or <= 0 || string.IsNullOrWhiteSpace(command.Unit))) throw new DomainException("Ocorrência de produto exige quantidade positiva e unidade.");
        var hash = Hash(command); return Tx(async (c, t) =>
        {
            var old = await c.QuerySingleOrDefaultAsync<(Guid Id, string RequestHash)>(new CommandDefinition("select id,request_hash requesthash from agro360.after_sales_occurrences where tenant_id=@TenantId and idempotency_key=@Key", new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct)); if (old.Id != Guid.Empty) { if (old.RequestHash != hash) throw new ConflictException("Chave de idempotência reutilizada com conteúdo diferente."); return old.Id; }
            var eligible = await c.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition("select case when @ItemId is null then 1 else i.checked_quantity-i.lost_quantity end from agro360.fulfillment_shipments s join agro360.sales_orders o on o.tenant_id=s.tenant_id and o.customer_id=s.customer_id left join agro360.fulfillment_shipment_items i on i.tenant_id=s.tenant_id and i.shipment_id=s.id and i.id=@ItemId where s.tenant_id=@TenantId and s.id=@ShipmentId and s.customer_id=@CustomerId and o.id=@OrderId and exists(select 1 from agro360.fulfillment_shipment_items sx join agro360.sales_order_items ox on ox.tenant_id=sx.tenant_id and ox.id=sx.order_item_id where sx.tenant_id=s.tenant_id and sx.shipment_id=s.id and ox.order_id=o.id and (@ItemId is null or sx.id=@ItemId)) and (@ItemId is null or lower(i.unit)=lower(@Unit)) and (@LotId is null or i.stock_lot_id=@LotId) limit 1 for update of s", new { tenant.TenantId, command.ShipmentId, command.CustomerId, command.OrderId, ItemId = command.ShipmentItemId, LotId = command.StockLotId, command.Unit }, t, cancellationToken: ct));
            if (eligible is null) throw new DomainException("Cliente, pedido, expedição, item ou lote não pertencem ao mesmo contexto."); if (command.AffectedQuantity > eligible) throw new ConflictException("Quantidade afetada supera o saldo elegível expedido.");
            var id = Guid.CreateVersion7(); await c.ExecuteAsync(new CommandDefinition("insert into agro360.after_sales_occurrences(id,tenant_id,customer_id,order_id,shipment_id,shipment_item_id,stock_lot_id,type,description,affected_quantity,unit,occurred_at,assignee_id,due_at,evidence_document_id,evidence_pending,idempotency_key,request_hash,created_by,updated_by) values(@Id,@TenantId,@CustomerId,@OrderId,@ShipmentId,@ShipmentItemId,@StockLotId,@Type,@Description,@AffectedQuantity,@Unit,@OccurredAt,@AssigneeId,@DueAt,@EvidenceDocumentId,@EvidencePending,@Key,@Hash,@UserId,@UserId); insert into agro360.after_sales_events(id,tenant_id,occurrence_id,event_type,to_status,reason,actor_id) values(@Event,@TenantId,@Id,'CREATED','OPEN',@Description,@UserId)", new { Id = id, Event = Guid.CreateVersion7(), tenant.TenantId, command.CustomerId, command.OrderId, command.ShipmentId, command.ShipmentItemId, command.StockLotId, Type = type, Description = command.Description.Trim(), command.AffectedQuantity, Unit = command.Unit?.Trim().ToLowerInvariant(), command.OccurredAt, command.AssigneeId, command.DueAt, command.EvidenceDocumentId, command.EvidencePending, Key = command.IdempotencyKey, Hash = hash, tenant.UserId }, t, cancellationToken: ct)); await Audit(c, t, "after-sales.create", id, command, ct); return id;
        });
    }
    public Task TransitionOccurrenceAsync(Guid id, TransitionOccurrenceCommand command, CancellationToken ct)
    {
        var target = command.Status.Trim().ToUpperInvariant(); if (string.IsNullOrWhiteSpace(command.Reason)) throw new DomainException("A transição exige justificativa.");
        return Tx(async (c, t) => { var current = await c.QuerySingleOrDefaultAsync<(string Status, long Version)>(new CommandDefinition("select status,version from agro360.after_sales_occurrences where tenant_id=@TenantId and id=@Id and deleted_at is null for update", new { tenant.TenantId, Id = id }, t, cancellationToken: ct)); if (current == default || current.Version != command.ExpectedVersion) throw new ConflictException("Caso alterado; recarregue antes de continuar.");
            var allowed = (current.Status, target) switch { ("OPEN", "ANALYSIS") or ("OPEN", "CANCELLED") or ("ANALYSIS", "AWAITING_INFORMATION") or ("ANALYSIS", "SOLUTION_PROPOSED") or ("ANALYSIS", "CANCELLED") or ("AWAITING_INFORMATION", "ANALYSIS") or ("SOLUTION_PROPOSED", "AWAITING_EXECUTION") or ("SOLUTION_PROPOSED", "ANALYSIS") or ("AWAITING_EXECUTION", "RESOLVED") or ("AWAITING_EXECUTION", "ANALYSIS") or ("RESOLVED", "ANALYSIS") => true, _ => false }; if (!allowed) throw new ConflictException("Transição não permitida no estado atual.");
            if (target == "RESOLVED" && await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.after_sales_solutions where tenant_id=@TenantId and occurrence_id=@Id and required and status<>'COMPLETED') or exists(select 1 from agro360.fulfillment_returns where tenant_id=@TenantId and occurrence_id=@Id and status not in('RELEASED','DISPOSED','CANCELLED')) or exists(select 1 from agro360.after_sales_adjustments where tenant_id=@TenantId and occurrence_id=@Id and status not in('EXECUTED','CANCELLED') and type<>'NONE')", new { tenant.TenantId, Id = id }, t, cancellationToken: ct))) throw new ConflictException("Ações obrigatórias, devolução ou execução financeira ainda impedem a resolução.");
            await c.ExecuteAsync(new CommandDefinition("update agro360.after_sales_occurrences set status=@Target,version=version+1,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id; insert into agro360.after_sales_events(id,tenant_id,occurrence_id,event_type,from_status,to_status,reason,actor_id) values(@Event,@TenantId,@Id,@EventType,@From,@Target,@Reason,@UserId)", new { tenant.TenantId, Id = id, Target = target, Event = Guid.CreateVersion7(), EventType = current.Status == "RESOLVED" ? "REOPENED" : "STATUS_CHANGED", From = current.Status, command.Reason, tenant.UserId }, t, cancellationToken: ct)); await Audit(c, t, "after-sales.transition", id, command, ct); });
    }
    public Task<Guid> ProposeSolutionAsync(Guid id, ProposeSolutionCommand command, CancellationToken ct)
    {
        var type = command.Type.Trim().ToUpperInvariant(); var allowed = new[] { "COMPLEMENT", "COLLECTION", "RETURN", "REPLACEMENT", "INFORMATION_CORRECTION", "COMMERCIAL_ADJUSTMENT", "CLOSE_WITHOUT_ADJUSTMENT" }; if (!allowed.Contains(type) || string.IsNullOrWhiteSpace(command.Description) || string.IsNullOrWhiteSpace(command.IdempotencyKey)) throw new DomainException("Solução, descrição e chave são obrigatórias."); var hash = Hash(command);
        return Tx(async (c, t) => { var old = await c.QuerySingleOrDefaultAsync<(Guid Id, string RequestHash)>(new CommandDefinition("select id,request_hash requesthash from agro360.after_sales_solutions where tenant_id=@TenantId and idempotency_key=@Key", new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct)); if (old.Id != Guid.Empty) { if (old.RequestHash != hash) throw new ConflictException("Chave reutilizada com conteúdo diferente."); return old.Id; } var solution = Guid.CreateVersion7(); var changed = await c.ExecuteAsync(new CommandDefinition("insert into agro360.after_sales_solutions(id,tenant_id,occurrence_id,type,description,required,due_at,idempotency_key,request_hash,created_by) select @Solution,@TenantId,@Id,@Type,@Description,@Required,@DueAt,@Key,@Hash,@UserId where exists(select 1 from agro360.after_sales_occurrences where tenant_id=@TenantId and id=@Id and status in('OPEN','ANALYSIS','AWAITING_INFORMATION','SOLUTION_PROPOSED')); update agro360.after_sales_occurrences set status='SOLUTION_PROPOSED',version=version+1,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id and status in('OPEN','ANALYSIS','AWAITING_INFORMATION','SOLUTION_PROPOSED')", new { Solution = solution, tenant.TenantId, Id = id, Type = type, command.Description, command.Required, command.DueAt, Key = command.IdempotencyKey, Hash = hash, tenant.UserId }, t, cancellationToken: ct)); if (changed == 0) throw new ConflictException("Caso não aceita nova solução."); await Audit(c, t, "after-sales.solution.proposed", id, command, ct); return solution; });
    }
    public Task<Guid> RegisterAdjustmentAsync(Guid id, CommercialAdjustmentCommand command, CancellationToken ct)
    {
        var type = command.Type.Trim().ToUpperInvariant(); var allowed = new[] { "DISCOUNT", "CREDIT", "PARTIAL_CANCELLATION", "REFUND", "NONE" }; if (!allowed.Contains(type) || command.Currency.Trim().Length != 3 || command.ProposedAmount < 0 || (type == "NONE" && command.ProposedAmount != 0) || string.IsNullOrWhiteSpace(command.Reason)) throw new DomainException("Decisão comercial inválida."); var hash = Hash(command);
        return Tx(async (c, t) => { var old = await c.QuerySingleOrDefaultAsync<(Guid Id, string RequestHash)>(new CommandDefinition("select id,request_hash requesthash from agro360.after_sales_adjustments where tenant_id=@TenantId and idempotency_key=@Key", new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct)); if (old.Id != Guid.Empty) { if (old.RequestHash != hash) throw new ConflictException("Chave reutilizada com conteúdo diferente."); return old.Id; } var adjustment = Guid.CreateVersion7(); if (await c.ExecuteAsync(new CommandDefinition("insert into agro360.after_sales_adjustments(id,tenant_id,occurrence_id,type,currency,proposed_amount,status,reason,idempotency_key,request_hash,created_by) select @Adjustment,@TenantId,@Id,@Type,@Currency,@Amount,'PROPOSED',@Reason,@Key,@Hash,@UserId where exists(select 1 from agro360.after_sales_occurrences where tenant_id=@TenantId and id=@Id and status not in('RESOLVED','CANCELLED'))", new { Adjustment = adjustment, tenant.TenantId, Id = id, Type = type, Currency = command.Currency.Trim().ToUpperInvariant(), Amount = command.ProposedAmount, command.Reason, Key = command.IdempotencyKey, Hash = hash, tenant.UserId }, t, cancellationToken: ct)) == 0) throw new ConflictException("Caso não aceita ajuste comercial."); await Audit(c, t, "after-sales.adjustment.proposed", id, command, ct); return adjustment; });
    }
    private static string Hash<T>(T command) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command))));
    private Task Audit(NpgsqlConnection c, NpgsqlTransaction t, string action, Guid id, object x, CancellationToken ct) => c.WriteAuditAsync(t, tenant, action, "Trip", id, null, x, ct);
    private async Task<T> Tx<T>(Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> f) { try { return await db.InTenantTransactionAsync(f, CancellationToken.None); } catch (Exception ex) { InfrastructureLogMessages.LogisticsFailed(logger, tenant.TenantId, ex); throw; } }
    private Task<bool> Tx(Func<NpgsqlConnection, NpgsqlTransaction, Task> f) => Tx(async (c, t) => { await f(c, t); return true; });
}
public sealed class DeliveryContractService(DatabaseExecutor db, ITenantContext tenant, ILogger<DeliveryContractService> logger) : IDeliveryContractService
{
    public Task<IReadOnlyList<dynamic>> ListAsync(CancellationToken ct) => Tx<IReadOnlyList<dynamic>>(async (c, t) => (await c.QueryAsync(new CommandDefinition("select *,contracted_quantity-delivered_quantity balance_to_deliver from agro360.commercial_delivery_contracts where tenant_id=@TenantId order by delivery_deadline", new { tenant.TenantId }, t, cancellationToken: ct))).AsList());
    public Task<Guid> SaveAsync(Guid? id, DeliveryContractCommand command, CancellationToken ct) { if (command.ContractedQuantity <= 0 || command.ContractedPrice < 0) throw new DomainException("Quantidade e preço contratados são inválidos."); if (command.Status == "CANCELLED" && string.IsNullOrWhiteSpace(command.CancellationReason)) throw new DomainException("Cancelamento exige motivo."); return Tx(async (c, t) => { var key = id ?? Guid.CreateVersion7(); var p = new DynamicParameters(command); p.Add("Id", key); p.Add("TenantId", tenant.TenantId); p.Add("UserId", tenant.UserId); var sql = id is null ? "insert into agro360.commercial_delivery_contracts(id,tenant_id,number,customer,product_id,contracted_quantity,contracted_price,unit,delivery_deadline,payment_terms,status,cancellation_reason,allow_overdelivery,created_by) values(@Id,@TenantId,@Number,@Customer,@ProductId,@ContractedQuantity,@ContractedPrice,@Unit,@DeliveryDeadline,@PaymentTerms,@Status,@CancellationReason,@AllowOverdelivery,@UserId)" : "update agro360.commercial_delivery_contracts set number=@Number,customer=@Customer,product_id=@ProductId,contracted_quantity=@ContractedQuantity,contracted_price=@ContractedPrice,unit=@Unit,delivery_deadline=@DeliveryDeadline,payment_terms=@PaymentTerms,status=@Status,cancellation_reason=@CancellationReason,allow_overdelivery=@AllowOverdelivery,updated_at=now() where tenant_id=@TenantId and id=@Id"; if (await c.ExecuteAsync(new CommandDefinition(sql, p, t, cancellationToken: ct)) == 0) throw new NotFoundException("Contrato", key); await c.WriteAuditAsync(t, tenant, id is null ? "create" : "update", "DeliveryContract", key, null, command, ct); return key; }); }
    private async Task<T> Tx<T>(Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> f) { try { return await db.InTenantTransactionAsync(f, CancellationToken.None); } catch (Exception ex) { InfrastructureLogMessages.OperationalContractFailed(logger, tenant.TenantId, ex); throw; } }
}
