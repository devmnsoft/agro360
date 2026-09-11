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

public sealed class LogisticsService(DatabaseExecutor db, ITenantContext tenant, ILogger<LogisticsService> logger) : ILogisticsService
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
        left join lateral(select sum(quantity) reserved from agro360.fulfillment_reservations r where r.tenant_id=o.tenant_id and r.order_item_id=i.id and r.status in('ACTIVE','CONSUMED')) r on true
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
        if (command.Items.Count == 0 || command.Items.Any(x => x.Quantity <= 0 || x.PickedQuantity <= 0 || x.CheckedQuantity < 0 || x.PickedQuantity > x.Quantity || x.CheckedQuantity > x.PickedQuantity)) throw new DomainException("Quantidades de reserva, separação e conferência são inválidas.");
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
                var available = await c.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition("""select l.quantity-coalesce((select sum(r.quantity) from agro360.fulfillment_reservations r where r.tenant_id=l.tenant_id and r.stock_lot_id=l.id and r.status='ACTIVE'),0) from agro360.inventory_stock_lots l join agro360.sales_order_items oi on oi.tenant_id=l.tenant_id and oi.id=@OrderItemId and oi.product_id=l.product_id join agro360.sales_orders o on o.tenant_id=oi.tenant_id and o.id=oi.order_id and o.status in('APPROVED','FULFILLMENT') where l.tenant_id=@TenantId and l.id=@LotId and l.warehouse_id=@WarehouseId and lower(oi.unit)=lower(@Unit)""", new { tenant.TenantId, item.OrderItemId, LotId = item.StockLotId, WarehouseId = command.OriginWarehouseId, item.Unit }, t, cancellationToken: ct));
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
        var items = (await c.QueryAsync<(Guid Id, Guid LotId, Guid ReservationId, decimal Quantity, string Unit)>(new CommandDefinition("select id,stock_lot_id lot_id,reservation_id,checked_quantity quantity,unit from agro360.fulfillment_shipment_items where tenant_id=@TenantId and shipment_id=@Id order by id for update", new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).AsList();
        foreach (var item in items)
        {
            var balanceChanged = await c.ExecuteAsync(new CommandDefinition("update agro360.inventory_stock_balances b set available=available-@Quantity,reserved=greatest(0,reserved-@Quantity),version=version+1,updated_at=now() from agro360.inventory_stock_lots l where b.tenant_id=@TenantId and l.tenant_id=b.tenant_id and l.id=@LotId and b.warehouse_id=l.warehouse_id and b.product_id=l.product_id and b.available>=@Quantity and b.reserved>=@Quantity", new { tenant.TenantId, item.LotId, item.Quantity }, t, cancellationToken: ct));
            if (balanceChanged == 0) throw new ConflictException("Saldo autorizado mudou após a separação.");
            var changed = await c.ExecuteAsync(new CommandDefinition("update agro360.inventory_stock_lots set quantity=quantity-@Quantity where tenant_id=@TenantId and id=@LotId and quantity>=@Quantity", new { tenant.TenantId, item.LotId, item.Quantity }, t, cancellationToken: ct));
            if (changed == 0) throw new ConflictException("Saldo ou qualidade do lote mudou após a separação.");
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.inventory_stock_movements(id,tenant_id,warehouse_id,product_id,movement_type,quantity,unit,unit_cost,total_cost,lot_number,reference_type,reference_id,idempotency_key,balance_after,average_cost_after,balance_version,occurred_at,created_by) select @Movement,@TenantId,l.warehouse_id,l.product_id,'SHIPMENT',@Quantity,@Unit,b.average_cost,round(@Quantity*b.average_cost,4),l.lot_number,'FULFILLMENT_SHIPMENT',@Shipment,@Key,b.available,b.average_cost,b.version,now(),@UserId from agro360.inventory_stock_lots l join agro360.inventory_stock_balances b on b.tenant_id=l.tenant_id and b.warehouse_id=l.warehouse_id and b.product_id=l.product_id where l.tenant_id=@TenantId and l.id=@LotId and not exists(select 1 from agro360.inventory_stock_movements m where m.tenant_id=@TenantId and m.idempotency_key=@Key)", new { Movement = Guid.CreateVersion7(), tenant.TenantId, item.LotId, item.Quantity, item.Unit, Shipment = id, Key = $"fulfillment:{id}:{item.Id}", tenant.UserId }, t, cancellationToken: ct));
            await c.ExecuteAsync(new CommandDefinition("update agro360.fulfillment_reservations set status='CONSUMED',version=version+1,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Reservation", new { tenant.TenantId, Reservation = item.ReservationId, tenant.UserId }, t, cancellationToken: ct));
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
