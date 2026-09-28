using Agro360.Application.Contracts;
using Agro360.Domain.Logistics;
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
    public Task<Guid> SaveAsync(Guid? id, TripCommand command, CancellationToken ct) { LogisticsRules.ValidateTrip(command.Origin, command.Destination, command.TransportMode, command.EstimatedDistance, command.FreightValue, command.Tonnes, command.Status); StorageRules.Freight(command.FreightValue, command.EstimatedDistance, command.Tonnes); return Tx(async (c, t) => { var key = id ?? Guid.CreateVersion7(); var p = new DynamicParameters(command); p.Add("Id", key); p.Add("TenantId", tenant.TenantId); p.Add("UserId", tenant.UserId); var sql = id is null ? "insert into agro360.logistics_trips(id,tenant_id,number,shipment_id,origin,destination,estimated_distance,carrier,driver,vehicle,freight_type,transport_mode,freight_value,cost_per_tonne,cost_per_km,status,created_by) values(@Id,@TenantId,@Number,@ShipmentId,@Origin,@Destination,@EstimatedDistance,@Carrier,@Driver,@Vehicle,@FreightType,@TransportMode,@FreightValue,case when @Tonnes=0 then 0 else @FreightValue/@Tonnes end,case when @EstimatedDistance=0 then 0 else @FreightValue/@EstimatedDistance end,@Status,@UserId)" : "update agro360.logistics_trips set number=@Number,shipment_id=@ShipmentId,origin=@Origin,destination=@Destination,estimated_distance=@EstimatedDistance,carrier=@Carrier,driver=@Driver,vehicle=@Vehicle,freight_type=@FreightType,transport_mode=@TransportMode,freight_value=@FreightValue,cost_per_tonne=case when @Tonnes=0 then 0 else @FreightValue/@Tonnes end,cost_per_km=case when @EstimatedDistance=0 then 0 else @FreightValue/@EstimatedDistance end,status=@Status,updated_at=now() where tenant_id=@TenantId and id=@Id"; if (await c.ExecuteAsync(new CommandDefinition(sql, p, t, cancellationToken: ct)) == 0) throw new NotFoundException("Viagem", key); await Audit(c, t, id is null ? "create" : "update", key, command, ct); return key; }); }
    public Task AddOccurrenceAsync(Guid id, TripOccurrenceCommand command, CancellationToken ct) { if (string.IsNullOrWhiteSpace(command.Description)) throw new DomainException("Descrição da ocorrência é obrigatória."); return Tx(async (c, t) => { await c.ExecuteAsync(new CommandDefinition("insert into agro360.logistics_trip_occurrences(id,tenant_id,trip_id,description,created_by) select @Occurrence,@TenantId,@Id,@Description,@UserId where exists(select 1 from agro360.logistics_trips where tenant_id=@TenantId and id=@Id); update agro360.logistics_trips set status='WITH_OCCURRENCE',updated_at=now() where tenant_id=@TenantId and id=@Id", new { Occurrence = Guid.CreateVersion7(), tenant.TenantId, Id = id, command.Description, tenant.UserId }, t, cancellationToken: ct)); await Audit(c, t, "occurrence", id, command, ct); }); }
    public Task CompleteAsync(Guid id, CancellationToken ct) => Tx(async (c, t) =>
    {
        var trip = await c.QuerySingleOrDefaultAsync<(string Status, Guid? ShipmentId)>(new CommandDefinition(
            "select status, shipment_id ShipmentId from agro360.logistics_trips where tenant_id=@TenantId and id=@Id for update",
            new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        if (string.IsNullOrWhiteSpace(trip.Status)) throw new NotFoundException("Viagem", id);
        LogisticsRules.EnsureCanDeliver(trip.Status);

        await c.ExecuteAsync(new CommandDefinition(
            "update agro360.logistics_trips set status='DELIVERED',delivered_at=now(),updated_at=now() where tenant_id=@TenantId and id=@Id",
            new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        if (trip.ShipmentId is not null)
            await c.ExecuteAsync(new CommandDefinition(
                "update agro360.storage_shipments set status='DELIVERED',updated_at=now() where tenant_id=@TenantId and id=@ShipmentId and status in('IN_TRANSIT','DELAYED')",
                new { tenant.TenantId, trip.ShipmentId }, t, cancellationToken: ct));
        await Audit(c, t, "complete", id, new { PreviousStatus = trip.Status, ShipmentId = trip.ShipmentId }, ct);
    });
    public Task<FulfillmentQueuePage> FulfillmentQueueAsync(FulfillmentQueueQuery query, CancellationToken ct) => Tx(async (c, t) =>
    {
        var page = Math.Clamp(query.Page, 1, 1_000_000); var pageSize = Math.Clamp(query.PageSize, 1, 100); var offset = checked((long)(page - 1) * pageSize);
        const string sql = """
        with queue as (
        select o.id order_id,o.order_number,o.status,c.name customer,o.property_id unit_id,o.expected_delivery,
               count(i.id) item_count,sum(i.quantity-coalesce(x.cancelled,0)-coalesce(x.dispatched,0)) quantity_pending,
               sum(coalesce(x.active_reserved,0)) quantity_reserved,
               bool_or(coalesce(x.dispatched,0)>0) partially_fulfilled
        from agro360.sales_orders o join agro360.crm_customers c on c.tenant_id=o.tenant_id and c.id=o.customer_id
        join agro360.sales_order_items i on i.tenant_id=o.tenant_id and i.order_id=o.id
        left join lateral(select
            sum(r.quantity-r.consumed_quantity-r.released_quantity) filter(where r.status='ACTIVE') active_reserved,
            sum(r.consumed_quantity) dispatched,
            i.cancelled_quantity cancelled
          from agro360.fulfillment_reservations r where r.tenant_id=o.tenant_id and r.order_item_id=i.id) x on true
        where o.tenant_id=@TenantId and o.deleted_at is null and o.status in('APPROVED','FULFILLMENT')
          and (@Customer is null or c.name ilike '%'||@Customer||'%') and (@Number is null or o.order_number ilike '%'||@Number||'%')
          and (@UnitId is null or o.property_id=@UnitId) and (@DueUntil is null or o.expected_delivery<=@DueUntil)
          and (@Status is null or o.status=@Status)
          and (@WarehouseId is null or exists(select 1 from agro360.inventory_stock_lots l join agro360.inventory_stock_balances b on b.tenant_id=l.tenant_id and b.warehouse_id=l.warehouse_id and b.product_id=l.product_id where l.tenant_id=o.tenant_id and l.warehouse_id=@WarehouseId and l.product_id=i.product_id and l.quality_status='APPROVED' and (l.expires_on is null or l.expires_on>=current_date) and b.available-b.reserved>0))
        group by o.id,c.name having sum(i.quantity-coalesce(x.cancelled,0)-coalesce(x.dispatched,0))>0)
        select *, (select count(*) from queue) total_count from queue order by expected_delivery nulls last,order_number,order_id limit @PageSize offset @Offset;
        with queue as (
        select o.id from agro360.sales_orders o join agro360.crm_customers c on c.tenant_id=o.tenant_id and c.id=o.customer_id
        join agro360.sales_order_items i on i.tenant_id=o.tenant_id and i.order_id=o.id
        where o.tenant_id=@TenantId and o.deleted_at is null and o.status in('APPROVED','FULFILLMENT')
          and (@Customer is null or c.name ilike '%'||@Customer||'%') and (@Number is null or o.order_number ilike '%'||@Number||'%')
          and (@UnitId is null or o.property_id=@UnitId) and (@DueUntil is null or o.expected_delivery<=@DueUntil)
          and (@Status is null or o.status=@Status)
          and (@WarehouseId is null or exists(select 1 from agro360.inventory_stock_lots l where l.tenant_id=o.tenant_id and l.warehouse_id=@WarehouseId and l.product_id=i.product_id and l.quality_status='APPROVED' and (l.expires_on is null or l.expires_on>=current_date)))
        group by o.id having sum(i.quantity-i.cancelled_quantity-coalesce((select sum(r.consumed_quantity) from agro360.fulfillment_reservations r where r.tenant_id=i.tenant_id and r.order_item_id=i.id),0))>0)
        select count(*) from queue
        """;
        var parameters = new { tenant.TenantId, Customer = string.IsNullOrWhiteSpace(query.Customer) ? null : query.Customer, Number = string.IsNullOrWhiteSpace(query.Number) ? null : query.Number, query.UnitId, query.DueUntil, Status = string.IsNullOrWhiteSpace(query.Status) ? null : query.Status, query.WarehouseId, PageSize = pageSize, Offset = offset };
        using var multiple = await c.QueryMultipleAsync(new CommandDefinition(sql, parameters, t, cancellationToken: ct));
        var rows = (await multiple.ReadAsync()).AsList();
        var total = await multiple.ReadSingleAsync<long>();
        return new FulfillmentQueuePage(rows, page, pageSize, total);
    });

    public Task<dynamic?> OrderFulfillmentDetailAsync(Guid orderId, CancellationToken ct) => Tx<dynamic?>(async (c, t) =>
    {
        var order = await c.QuerySingleOrDefaultAsync(new CommandDefinition("""
            select o.id,o.order_number,o.status,o.currency,o.payment_terms,o.expected_delivery,o.customer_id,c.name customer,
                   pc.proposal_id,pc.version_number proposal_version,p.proposal_number
            from agro360.sales_orders o join agro360.crm_customers c on c.tenant_id=o.tenant_id and c.id=o.customer_id
            left join agro360.sales_proposal_conversions pc on pc.tenant_id=o.tenant_id and pc.order_id=o.id
            left join agro360.sales_proposals p on p.tenant_id=pc.tenant_id and p.id=pc.proposal_id
            where o.tenant_id=@TenantId and o.id=@Id and o.deleted_at is null
            """, new { tenant.TenantId, Id = orderId }, t, cancellationToken: ct));
        if (order is null) return null;
        var items = (await c.QueryAsync(new CommandDefinition("""
            select i.id,i.quantity ordered_quantity,i.unit,p.name product,p.code product_code,
              coalesce((select sum(r.quantity-r.consumed_quantity-r.released_quantity) from agro360.fulfillment_reservations r where r.tenant_id=i.tenant_id and r.order_item_id=i.id and r.status='ACTIVE'),0) reserved_quantity,
              coalesce((select sum(si.picked_quantity) from agro360.fulfillment_shipment_items si join agro360.fulfillment_shipments s on s.tenant_id=si.tenant_id and s.id=si.shipment_id where si.tenant_id=i.tenant_id and si.order_item_id=i.id and s.status in('PREPARING','CHECKED')),0) picked_quantity,
              i.cancelled_quantity,
              coalesce((select sum(r.consumed_quantity) from agro360.fulfillment_reservations r where r.tenant_id=i.tenant_id and r.order_item_id=i.id),0) dispatched_quantity,
              i.quantity-i.cancelled_quantity-coalesce((select sum(r.consumed_quantity) from agro360.fulfillment_reservations r where r.tenant_id=i.tenant_id and r.order_item_id=i.id),0) pending_quantity,
              i.fulfillment_version
            from agro360.sales_order_items i join agro360.inventory_products p on p.tenant_id=i.tenant_id and p.id=i.product_id
            where i.tenant_id=@TenantId and i.order_id=@Id group by i.id,p.name,p.code order by i.created_at,i.id
            """, new { tenant.TenantId, Id = orderId }, t, cancellationToken: ct))).AsList();
        var reservations = (await c.QueryAsync(new CommandDefinition("select r.id,r.order_item_id,r.quantity,r.consumed_quantity,r.released_quantity,r.quantity-r.consumed_quantity-r.released_quantity active_quantity,r.unit,r.status,r.version,l.lot_number,w.name warehouse,r.created_at from agro360.fulfillment_reservations r join agro360.inventory_stock_lots l on l.tenant_id=r.tenant_id and l.id=r.stock_lot_id join agro360.inventory_warehouses w on w.tenant_id=l.tenant_id and w.id=l.warehouse_id join agro360.sales_order_items i on i.tenant_id=r.tenant_id and i.id=r.order_item_id where r.tenant_id=@TenantId and i.order_id=@Id order by r.created_at", new { tenant.TenantId, Id = orderId }, t, cancellationToken: ct))).AsList();
        var shipments = (await c.QueryAsync(new CommandDefinition("select distinct s.id,s.number,s.status,s.version,s.dispatched_at,s.created_at from agro360.fulfillment_shipments s join agro360.fulfillment_shipment_items si on si.tenant_id=s.tenant_id and si.shipment_id=s.id join agro360.sales_order_items i on i.tenant_id=si.tenant_id and i.id=si.order_item_id where s.tenant_id=@TenantId and i.order_id=@Id order by s.created_at", new { tenant.TenantId, Id = orderId }, t, cancellationToken: ct))).AsList();
        var history = (await c.QueryAsync(new CommandDefinition("select event_type,payload,created_at from agro360.sales_commercial_events where tenant_id=@TenantId and aggregate_id=@Id order by created_at", new { tenant.TenantId, Id = orderId }, t, cancellationToken: ct))).AsList();
        return new { order, items, reservations, shipments, history };
    });
    public Task<IReadOnlyList<dynamic>> EligibleLotsAsync(Guid orderItemId, CancellationToken ct) => Tx<IReadOnlyList<dynamic>>(async (c, t) => (await c.QueryAsync(new CommandDefinition("""
        select l.id lot_id,l.warehouse_id,w.name warehouse,l.lot_number,l.expires_on,l.quality_status,
          least(l.quantity-coalesce((select sum(r.quantity-r.consumed_quantity-r.released_quantity) from agro360.fulfillment_reservations r where r.tenant_id=l.tenant_id and r.stock_lot_id=l.id and r.status='ACTIVE'),0),b.available-b.reserved) eligible_quantity
        from agro360.sales_order_items i join agro360.inventory_stock_lots l on l.tenant_id=i.tenant_id and l.product_id=i.product_id
        join agro360.inventory_warehouses w on w.tenant_id=l.tenant_id and w.id=l.warehouse_id and w.deleted_at is null
        join agro360.inventory_stock_balances b on b.tenant_id=l.tenant_id and b.warehouse_id=l.warehouse_id and b.product_id=l.product_id
        where i.tenant_id=@TenantId and i.id=@Id and l.quality_status='APPROVED' and (l.expires_on is null or l.expires_on>=current_date)
          and b.available-b.reserved>0 order by w.name,l.expires_on nulls last,l.lot_number,l.id
        """, new { tenant.TenantId, Id = orderItemId }, t, cancellationToken: ct))).AsList());

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
        if (command.Items.Count == 0 || command.Items.Any(x => x.Quantity <= 0 || x.PickedQuantity < 0 || x.CheckedQuantity < 0 || x.PickedQuantity > x.Quantity || x.CheckedQuantity > x.PickedQuantity)) throw new DomainException("Quantidades de reserva, separação e conferência são inválidas.");
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey)) throw new DomainException("Chave de idempotência é obrigatória.");
        var hash = Hash(command);
        return Tx(async (c, t) =>
        {
            // Serialize the lookup/insert pair: a concurrent replay must observe the
            // committed document instead of losing the unique-key race and leaving
            // PostgreSQL's transaction in an aborted state.
            await c.ExecuteAsync(new CommandDefinition("select pg_advisory_xact_lock(hashtextextended(@LockKey,0))", new { LockKey = $"fulfillment:create:{tenant.TenantId}:{command.IdempotencyKey}" }, t, cancellationToken: ct));
            var previous = await c.QuerySingleOrDefaultAsync<(Guid Id, string RequestHash)>(new CommandDefinition("select id,request_hash requesthash from agro360.fulfillment_shipments where tenant_id=@TenantId and idempotency_key=@Key", new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
            if (previous.Id != Guid.Empty) { if (previous.RequestHash != hash) throw new ConflictException("Chave de idempotência reutilizada com conteúdo diferente."); return previous.Id; }
            // Ordem global: itens comerciais -> lotes -> saldos -> reservas -> documentos.
            foreach (var orderItemId in command.Items.Select(x => x.OrderItemId).Distinct().Order())
                await c.ExecuteAsync(new CommandDefinition("select pg_advisory_xact_lock(hashtextextended(@LockKey,0))", new { LockKey = $"fulfillment:item:{tenant.TenantId}:{orderItemId}" }, t, cancellationToken: ct));
            foreach (var lotId in command.Items.Select(x => x.StockLotId).Distinct().Order())
                await c.ExecuteAsync(new CommandDefinition("select pg_advisory_xact_lock(hashtextextended(@LockKey,0))", new { LockKey = $"fulfillment:lot:{tenant.TenantId}:{lotId}" }, t, cancellationToken: ct));
            var id = Guid.CreateVersion7();
            var initialStatus = command.Items.All(x => x.CheckedQuantity > 0) ? "CHECKED" : "PREPARING";
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.fulfillment_shipments(id,tenant_id,number,origin_warehouse_id,destination,customer_id,status,idempotency_key,request_hash,created_by,updated_by) values(@Id,@TenantId,@Number,@Warehouse,@Destination,@Customer,@Status,@Key,@Hash,@UserId,@UserId)", new { Id = id, tenant.TenantId, command.Number, Warehouse = command.OriginWarehouseId, command.Destination, Customer = command.CustomerId, Status = initialStatus, Key = command.IdempotencyKey, Hash = hash, tenant.UserId }, t, cancellationToken: ct));
            foreach (var item in command.Items)
            {
                var available = await c.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition("""
                    select least(
                        l.quantity-coalesce((select sum(r.quantity-r.consumed_quantity-r.released_quantity) from agro360.fulfillment_reservations r where r.tenant_id=l.tenant_id and r.stock_lot_id=l.id and r.status='ACTIVE'),0),
                        oi.quantity-oi.cancelled_quantity-coalesce((select sum(r.quantity-r.released_quantity) from agro360.fulfillment_reservations r where r.tenant_id=oi.tenant_id and r.order_item_id=oi.id and r.status in('ACTIVE','CONSUMED','RELEASED')),0))
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
                await c.ExecuteAsync(new CommandDefinition("insert into agro360.fulfillment_shipment_items(id,tenant_id,shipment_id,reservation_id,order_item_id,stock_lot_id,requested_quantity,reserved_quantity,picked_quantity,checked_quantity,check_completed,unit,divergence_reason,created_by,updated_by) values(@Id,@TenantId,@ShipmentId,@ReservationId,@OrderItemId,@LotId,@Quantity,@Quantity,@Picked,@Checked,@Completed,@Unit,@Reason,@UserId,@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, ShipmentId = id, ReservationId = reservation, item.OrderItemId, LotId = item.StockLotId, item.Quantity, Picked = item.PickedQuantity, Checked = item.CheckedQuantity, Completed = item.CheckedQuantity > 0, item.Unit, Reason = item.CheckedQuantity == item.Quantity ? null : item.DivergenceReason, tenant.UserId }, t, cancellationToken: ct));
            }
            if (command.Items.Any(x => x.CheckedQuantity > 0 && x.CheckedQuantity != x.PickedQuantity && string.IsNullOrWhiteSpace(x.DivergenceReason))) throw new DomainException("Diferença de conferência exige tratamento explícito.");
            await c.ExecuteAsync(new CommandDefinition("update agro360.sales_orders set status='FULFILLMENT',updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id in(select oi.order_id from agro360.sales_order_items oi join agro360.fulfillment_shipment_items si on si.tenant_id=oi.tenant_id and si.order_item_id=oi.id where si.shipment_id=@Id)", new { Id = id, tenant.TenantId, tenant.UserId }, t, cancellationToken: ct));
            await Audit(c, t, "fulfillment.create", id, command, ct); return id;
        });
    }
    public Task PrepareFulfillmentAsync(Guid shipmentItemId, PrepareFulfillmentCommand command, CancellationToken ct)
    {
        if (command.PickedQuantity < 0 || command.CheckedQuantity < 0 || command.CheckedQuantity > command.PickedQuantity || string.IsNullOrWhiteSpace(command.IdempotencyKey))
            throw new DomainException("Separação, conferência e chave de idempotência são obrigatórias e coerentes.");
        var checkCompleted = command.CompleteCheck ?? command.CheckedQuantity > 0; // consumidores antigos continuam concluindo ao informar conferência
        if (checkCompleted && command.CheckedQuantity != command.PickedQuantity && string.IsNullOrWhiteSpace(command.DivergenceReason))
            throw new DomainException("Divergência exige motivo e decisão explícita para o remanescente.");
        var hash = Hash(new { shipmentItemId, command.PickedQuantity, command.CheckedQuantity, command.DivergenceReason, command.Version, CheckCompleted = checkCompleted });
        return Tx(async (c, t) =>
        {
            var orderItemId = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("select order_item_id from agro360.fulfillment_shipment_items where tenant_id=@TenantId and id=@Id", new { tenant.TenantId, Id = shipmentItemId }, t, cancellationToken: ct));
            if (orderItemId is null) throw new NotFoundException("Item da expedição", shipmentItemId);
            await c.ExecuteAsync(new CommandDefinition("select pg_advisory_xact_lock(hashtextextended(@LockKey,0))", new { LockKey = $"fulfillment:item:{tenant.TenantId}:{orderItemId}" }, t, cancellationToken: ct));
            // Replay is deliberately revalidated after the aggregate lock.
            var replay = await c.QuerySingleOrDefaultAsync<(Guid AggregateId, string RequestHash)>(new CommandDefinition("select aggregate_id AggregateId,request_hash RequestHash from agro360.fulfillment_operation_requests where tenant_id=@TenantId and operation='PREPARE' and idempotency_key=@Key", new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
            if (replay.AggregateId != Guid.Empty) { if (replay.RequestHash != hash || replay.AggregateId != shipmentItemId) throw new ConflictException("Chave de idempotência reutilizada com conteúdo diferente."); return; }
            var changed = await c.ExecuteAsync(new CommandDefinition("""
                update agro360.fulfillment_shipment_items i set picked_quantity=@PickedQuantity,checked_quantity=@CheckedQuantity,
                  check_completed=@CheckCompleted,divergence_reason=case when @CheckCompleted then @DivergenceReason else null end,version=version+1,updated_at=now(),updated_by=@UserId
                from agro360.fulfillment_shipments s, agro360.fulfillment_reservations r
                where i.tenant_id=@TenantId and i.id=@Id and i.version=@Version and s.tenant_id=i.tenant_id and s.id=i.shipment_id
                  and s.status='PREPARING' and r.tenant_id=i.tenant_id and r.id=i.reservation_id and r.status='ACTIVE'
                  and @PickedQuantity<=r.quantity-r.consumed_quantity-r.released_quantity
                """, new { tenant.TenantId, Id = shipmentItemId, command.PickedQuantity, command.CheckedQuantity, CheckCompleted = checkCompleted, command.DivergenceReason, command.Version, tenant.UserId }, t, cancellationToken: ct));
            if (changed != 1) throw new ConflictException("A preparação foi alterada ou a reserva não possui saldo ativo.");
            await c.ExecuteAsync(new CommandDefinition("update agro360.fulfillment_shipments s set status=case when exists(select 1 from agro360.fulfillment_shipment_items i where i.tenant_id=s.tenant_id and i.shipment_id=s.id and not i.check_completed) then 'PREPARING' else 'CHECKED' end,version=version+1,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=(select shipment_id from agro360.fulfillment_shipment_items where tenant_id=@TenantId and id=@Id)", new { tenant.TenantId, Id = shipmentItemId, tenant.UserId }, t, cancellationToken: ct));
            await RecordOperation(c, t, "PREPARE", shipmentItemId, command.IdempotencyKey, hash, command.Version + 1, ct);
            await Audit(c, t, "fulfillment.prepare", shipmentItemId, command, ct);
        });
    }
    public Task ReleaseReservationAsync(Guid reservationId, ReleaseReservationCommand command, CancellationToken ct)
    {
        if (command.Quantity <= 0 || string.IsNullOrWhiteSpace(command.Reason) || string.IsNullOrWhiteSpace(command.IdempotencyKey)) throw new DomainException("Liberação exige quantidade, motivo e chave de idempotência.");
        var hash = Hash(new { reservationId, command.Quantity, command.Reason, command.Version });
        return Tx(async (c, t) =>
        {
            var orderItemId = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("select order_item_id from agro360.fulfillment_reservations where tenant_id=@TenantId and id=@Id", new { tenant.TenantId, Id = reservationId }, t, cancellationToken: ct));
            if (orderItemId is null) throw new NotFoundException("Reserva", reservationId);
            await c.ExecuteAsync(new CommandDefinition("select pg_advisory_xact_lock(hashtextextended(@LockKey,0))", new { LockKey = $"fulfillment:item:{tenant.TenantId}:{orderItemId}" }, t, cancellationToken: ct));
            var replay = await c.QuerySingleOrDefaultAsync<(Guid AggregateId, string RequestHash)>(new CommandDefinition("select aggregate_id AggregateId,request_hash RequestHash from agro360.fulfillment_operation_requests where tenant_id=@TenantId and operation='RELEASE' and idempotency_key=@Key", new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
            if (replay.AggregateId != Guid.Empty) { if (replay.RequestHash != hash || replay.AggregateId != reservationId) throw new ConflictException("Chave de idempotência reutilizada com conteúdo diferente."); return; }
            var row = await c.QuerySingleOrDefaultAsync<(Guid LotId, decimal Quantity, decimal Consumed, decimal Released, long Version)>(new CommandDefinition("select stock_lot_id LotId,quantity,consumed_quantity Consumed,released_quantity Released,version from agro360.fulfillment_reservations where tenant_id=@TenantId and id=@Id and status='ACTIVE' for update", new { tenant.TenantId, Id = reservationId }, t, cancellationToken: ct));
            if (row.LotId == Guid.Empty || row.Version != command.Version || row.Quantity-row.Consumed-row.Released < command.Quantity) throw new ConflictException("Reserva alterada ou quantidade de liberação indisponível.");
            var linked = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.fulfillment_shipment_items i join agro360.fulfillment_shipments s on s.tenant_id=i.tenant_id and s.id=i.shipment_id where i.tenant_id=@TenantId and i.reservation_id=@Id and s.status<>'CANCELLED' and (i.picked_quantity>0 or i.checked_quantity>0))", new { tenant.TenantId, Id = reservationId }, t, cancellationToken: ct));
            if (linked) throw new ConflictException("Desfaça explicitamente a separação e a conferência antes de liberar a reserva.");
            var requestId = await RecordOperation(c, t, "RELEASE", reservationId, command.IdempotencyKey, hash, command.Version + 1, ct);
            var released = await c.ExecuteAsync(new CommandDefinition("update agro360.fulfillment_reservations set released_quantity=released_quantity+@Quantity,status=case when consumed_quantity+released_quantity+@Quantity=quantity then 'RELEASED' else 'ACTIVE' end,version=version+1,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id and version=@Version and status='ACTIVE'; update agro360.inventory_stock_balances b set reserved=reserved-@Quantity,version=version+1,updated_at=now() from agro360.inventory_stock_lots l where l.tenant_id=b.tenant_id and l.id=@LotId and b.tenant_id=@TenantId and b.warehouse_id=l.warehouse_id and b.product_id=l.product_id and b.reserved>=@Quantity", new { tenant.TenantId, Id = reservationId, row.LotId, command.Quantity, command.Version, tenant.UserId }, t, cancellationToken: ct));
            if (released != 2) throw new ConflictException("Saldo mudou durante a liberação.");
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.fulfillment_reservation_releases(id,tenant_id,reservation_id,quantity,reason,request_id,created_by) values(@Id,@TenantId,@Reservation,@Quantity,@Reason,@Request,@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, Reservation = reservationId, command.Quantity, command.Reason, Request = requestId, tenant.UserId }, t, cancellationToken: ct));
            await Audit(c, t, "fulfillment.reservation.release", reservationId, command, ct);
        });
    }
    public Task ReopenFulfillmentAsync(Guid shipmentId, ReopenFulfillmentCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.Reason) || string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.ItemIds.Count == 0)
            throw new DomainException("Reabertura exige motivo, itens, versão esperada e chave de idempotência.");
        var itemIds = command.ItemIds.Distinct().Order().ToArray();
        var hash = Hash(new { shipmentId, Reason = command.Reason.Trim(), command.ExpectedVersion, ItemIds = itemIds });
        return Tx(async (c, t) =>
        {
            var commercialItems = (await c.QueryAsync<Guid>(new CommandDefinition("select distinct order_item_id from agro360.fulfillment_shipment_items where tenant_id=@TenantId and shipment_id=@Id order by order_item_id", new { tenant.TenantId, Id = shipmentId }, t, cancellationToken: ct))).AsList();
            if (commercialItems.Count == 0) throw new NotFoundException("Expedição", shipmentId);
            foreach (var orderItemId in commercialItems)
                await c.ExecuteAsync(new CommandDefinition("select pg_advisory_xact_lock(hashtextextended(@LockKey,0))", new { LockKey = $"fulfillment:item:{tenant.TenantId}:{orderItemId}" }, t, cancellationToken: ct));
            var replay = await c.QuerySingleOrDefaultAsync<(Guid AggregateId, string RequestHash)>(new CommandDefinition("select aggregate_id AggregateId,request_hash RequestHash from agro360.fulfillment_operation_requests where tenant_id=@TenantId and operation='REOPEN' and idempotency_key=@Key", new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
            if (replay.AggregateId != Guid.Empty) { if (replay.AggregateId != shipmentId || replay.RequestHash != hash) throw new ConflictException("Chave de idempotência reutilizada com conteúdo diferente."); return; }
            var shipment = await c.QuerySingleOrDefaultAsync<(string Status, long Version)>(new CommandDefinition("select status,version from agro360.fulfillment_shipments where tenant_id=@TenantId and id=@Id for update", new { tenant.TenantId, Id = shipmentId }, t, cancellationToken: ct));
            if (string.IsNullOrWhiteSpace(shipment.Status)) throw new NotFoundException("Expedição", shipmentId);
            if (shipment.Status != "CHECKED" || shipment.Version != command.ExpectedVersion) throw new ConflictException("Somente uma conferência atual e ainda não expedida pode ser reaberta.");
            var previous = (await c.QueryAsync<(Guid Id, decimal Picked, decimal Checked, bool Completed)>(new CommandDefinition("select id,picked_quantity Picked,checked_quantity Checked,check_completed Completed from agro360.fulfillment_shipment_items where tenant_id=@TenantId and shipment_id=@Shipment and id=any(@Items) order by id for update", new { tenant.TenantId, Shipment = shipmentId, Items = itemIds }, t, cancellationToken: ct))).AsList();
            if (previous.Count != itemIds.Length) throw new ConflictException("Um ou mais itens não pertencem ao documento.");
            var requestId = await RecordOperation(c, t, "REOPEN", shipmentId, command.IdempotencyKey, hash, command.ExpectedVersion + 1, ct);
            foreach (var item in previous)
                await c.ExecuteAsync(new CommandDefinition("insert into agro360.fulfillment_preparation_reopens(id,tenant_id,shipment_id,item_id,reason,previous_picked,previous_checked,previous_check_completed,new_picked,new_checked,new_check_completed,request_id,created_by) values(@Id,@TenantId,@Shipment,@Item,@Reason,@Picked,@Checked,@Completed,0,0,false,@Request,@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, Shipment = shipmentId, Item = item.Id, Reason = command.Reason.Trim(), item.Picked, item.Checked, item.Completed, Request = requestId, tenant.UserId }, t, cancellationToken: ct));
            var changed = await c.ExecuteAsync(new CommandDefinition("update agro360.fulfillment_shipment_items set picked_quantity=0,checked_quantity=0,check_completed=false,divergence_reason=null,version=version+1,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and shipment_id=@Shipment and id=any(@Items)", new { tenant.TenantId, Shipment = shipmentId, Items = itemIds, tenant.UserId }, t, cancellationToken: ct));
            if (changed != itemIds.Length) throw new ConflictException("Itens mudaram durante a reabertura.");
            await c.ExecuteAsync(new CommandDefinition("update agro360.fulfillment_shipments set status='PREPARING',version=version+1,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id and status='CHECKED'", new { tenant.TenantId, Id = shipmentId, tenant.UserId }, t, cancellationToken: ct));
            await Audit(c, t, "fulfillment.reopen", shipmentId, new { command.Reason, Previous = previous, Current = new { Picked = 0, Checked = 0, CheckCompleted = false } }, ct);
        });
    }
    public Task CancelOrderItemAsync(Guid orderItemId, CancelOrderItemCommand command, CancellationToken ct)
    {
        if (command.Quantity <= 0 || string.IsNullOrWhiteSpace(command.Reason) || string.IsNullOrWhiteSpace(command.IdempotencyKey)) throw new DomainException("Cancelamento exige quantidade positiva, motivo e chave de idempotência.");
        var hash = Hash(new { orderItemId, command.Quantity, command.Reason, command.Version });
        return Tx(async (c, t) =>
        {
            await c.ExecuteAsync(new CommandDefinition("select pg_advisory_xact_lock(hashtextextended(@LockKey,0))", new { LockKey = $"fulfillment:item:{tenant.TenantId}:{orderItemId}" }, t, cancellationToken: ct));
            var replay = await c.QuerySingleOrDefaultAsync<(Guid AggregateId, string RequestHash)>(new CommandDefinition("select aggregate_id AggregateId,request_hash RequestHash from agro360.fulfillment_operation_requests where tenant_id=@TenantId and operation='CANCEL' and idempotency_key=@Key", new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
            if (replay.AggregateId != Guid.Empty) { if (replay.RequestHash != hash || replay.AggregateId != orderItemId) throw new ConflictException("Chave de idempotência reutilizada com conteúdo diferente."); return; }
            var dispatched = await c.ExecuteScalarAsync<decimal>(new CommandDefinition("select coalesce(sum(consumed_quantity),0) from agro360.fulfillment_reservations where tenant_id=@TenantId and order_item_id=@Id", new { tenant.TenantId, Id = orderItemId }, t, cancellationToken: ct));
            var prepared = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.fulfillment_reservations r join agro360.fulfillment_shipment_items i on i.tenant_id=r.tenant_id and i.reservation_id=r.id join agro360.fulfillment_shipments s on s.tenant_id=i.tenant_id and s.id=i.shipment_id where r.tenant_id=@TenantId and r.order_item_id=@Id and r.status='ACTIVE' and s.status<>'CANCELLED' and (i.picked_quantity>0 or i.checked_quantity>0))", new { tenant.TenantId, Id = orderItemId }, t, cancellationToken: ct));
            if (prepared) throw new ConflictException("Desfaça explicitamente a separação e a conferência antes de cancelar o saldo.");
            var reservations = (await c.QueryAsync<(Guid Id, Guid LotId, decimal Active)>(new CommandDefinition("select id,stock_lot_id LotId,quantity-consumed_quantity-released_quantity Active from agro360.fulfillment_reservations where tenant_id=@TenantId and order_item_id=@Id and status='ACTIVE' order by id for update", new { tenant.TenantId, Id = orderItemId }, t, cancellationToken: ct))).AsList();
            var toRelease = command.Quantity;
            foreach (var reservation in reservations)
            {
                if (toRelease <= 0) break;
                var amount = Math.Min(toRelease, reservation.Active);
                var released = await c.ExecuteAsync(new CommandDefinition("update agro360.fulfillment_reservations set released_quantity=released_quantity+@Amount,status=case when consumed_quantity+released_quantity+@Amount=quantity then 'CANCELLED' else 'ACTIVE' end,version=version+1,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Reservation and status='ACTIVE'; update agro360.inventory_stock_balances b set reserved=reserved-@Amount,version=version+1,updated_at=now() from agro360.inventory_stock_lots l where l.tenant_id=b.tenant_id and l.id=@LotId and b.tenant_id=@TenantId and b.warehouse_id=l.warehouse_id and b.product_id=l.product_id and b.reserved>=@Amount", new { tenant.TenantId, Reservation = reservation.Id, reservation.LotId, Amount = amount, tenant.UserId }, t, cancellationToken: ct));
                if (released != 2) throw new ConflictException("Saldo de reserva mudou durante o cancelamento.");
                toRelease -= amount;
            }
            var changed = await c.ExecuteAsync(new CommandDefinition("update agro360.sales_order_items set cancelled_quantity=cancelled_quantity+@Quantity,fulfillment_version=fulfillment_version+1 where tenant_id=@TenantId and id=@Id and fulfillment_version=@Version and quantity-cancelled_quantity-@Dispatched>=@Quantity", new { tenant.TenantId, Id = orderItemId, command.Quantity, command.Version, Dispatched = dispatched }, t, cancellationToken: ct));
            if (changed != 1) throw new ConflictException("Item alterado, saldo insuficiente ou preparação vinculada. Desfaça a preparação antes de cancelar.");
            var requestId = await RecordOperation(c, t, "CANCEL", orderItemId, command.IdempotencyKey, hash, command.Version + 1, ct);
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.fulfillment_order_item_cancellations(id,tenant_id,order_item_id,quantity,reason,request_id,created_by) values(@Id,@TenantId,@Item,@Quantity,@Reason,@Request,@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, Item = orderItemId, command.Quantity, command.Reason, Request = requestId, tenant.UserId }, t, cancellationToken: ct));
            await Audit(c, t, "fulfillment.order-item.cancel", orderItemId, command, ct);
        });
    }
    public Task DispatchFulfillmentAsync(Guid id, DispatchFulfillmentCommand command, CancellationToken ct) => Tx(async (c, t) =>
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey)) throw new DomainException("Chave de idempotência é obrigatória.");
        var requestHash = Hash(new { ShipmentId = id, command.Version });
        var commercialItems = (await c.QueryAsync<Guid>(new CommandDefinition("select distinct order_item_id from agro360.fulfillment_shipment_items where tenant_id=@TenantId and shipment_id=@Id order by order_item_id", new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).AsList();
        foreach (var orderItemId in commercialItems)
            await c.ExecuteAsync(new CommandDefinition("select pg_advisory_xact_lock(hashtextextended(@LockKey,0))", new { LockKey = $"fulfillment:item:{tenant.TenantId}:{orderItemId}" }, t, cancellationToken: ct));
        var shipment = await c.QuerySingleOrDefaultAsync<(string Status, long Version, string? DispatchIdempotencyKey, string? DispatchRequestHash)>(new CommandDefinition("select status,version,dispatch_idempotency_key DispatchIdempotencyKey,dispatch_request_hash DispatchRequestHash from agro360.fulfillment_shipments where tenant_id=@TenantId and id=@Id for update", new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        if (string.IsNullOrWhiteSpace(shipment.Status)) throw new NotFoundException("Expedição", id);
        if (shipment.DispatchIdempotencyKey is not null)
        {
            if (shipment.DispatchIdempotencyKey == command.IdempotencyKey && shipment.DispatchRequestHash == requestHash) return;
            throw new ConflictException("Expedição já confirmada por outra requisição.");
        }
        if (shipment.Status != "CHECKED" || shipment.Version != command.Version) throw new ConflictException("Expedição foi alterada ou não está conferida.");
        var items = (await c.QueryAsync<(Guid Id, Guid LotId, Guid ReservationId, decimal Quantity, string Unit)>(new CommandDefinition("select id,stock_lot_id lot_id,reservation_id,checked_quantity quantity,unit from agro360.fulfillment_shipment_items where tenant_id=@TenantId and shipment_id=@Id order by id for update", new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).AsList();
        if (items.Count == 0 || items.Any(x => x.Quantity <= 0)) throw new ConflictException("A expedição precisa ter itens conferidos com quantidade positiva.");
        foreach (var item in items)
        {
            var reservation = await c.QuerySingleOrDefaultAsync<(decimal Quantity, decimal Consumed, decimal Released)>(new CommandDefinition("select quantity,consumed_quantity Consumed,released_quantity Released from agro360.fulfillment_reservations where tenant_id=@TenantId and id=@Id and status='ACTIVE' for update", new { tenant.TenantId, Id = item.ReservationId }, t, cancellationToken: ct));
            var active = reservation.Quantity - reservation.Consumed - reservation.Released;
            if (active < item.Quantity) throw new ConflictException("A reserva mudou durante a confirmação da saída.");
            var remainder = active - item.Quantity; // política vigente: expedir o conferido e liberar explicitamente o remanescente
            var balanceChanged = await c.ExecuteAsync(new CommandDefinition("update agro360.inventory_stock_balances b set available=available-@Quantity,reserved=reserved-@Active,version=version+1,updated_at=now() from agro360.inventory_stock_lots l where b.tenant_id=@TenantId and l.tenant_id=b.tenant_id and l.id=@LotId and b.warehouse_id=l.warehouse_id and b.product_id=l.product_id and b.available>=@Quantity and b.reserved>=@Active", new { tenant.TenantId, item.LotId, item.Quantity, Active = active }, t, cancellationToken: ct));
            if (balanceChanged == 0) throw new ConflictException("Saldo autorizado mudou após a separação.");
            var changed = await c.ExecuteAsync(new CommandDefinition("update agro360.inventory_stock_lots set quantity=quantity-@Quantity where tenant_id=@TenantId and id=@LotId and quantity>=@Quantity and quality_status='APPROVED' and (expires_on is null or expires_on>=current_date)", new { tenant.TenantId, item.LotId, item.Quantity }, t, cancellationToken: ct));
            if (changed == 0) throw new ConflictException("Saldo ou qualidade do lote mudou após a separação.");
            var movementCreated = await c.ExecuteAsync(new CommandDefinition("insert into agro360.inventory_stock_movements(id,tenant_id,warehouse_id,product_id,movement_type,quantity,unit,unit_cost,total_cost,lot_number,reference_type,reference_id,idempotency_key,balance_after,average_cost_after,balance_version,occurred_at,created_by) select @Movement,@TenantId,l.warehouse_id,l.product_id,'SALE',@Quantity,@Unit,b.average_cost,round(@Quantity*b.average_cost,4),l.lot_number,'FULFILLMENT_SHIPMENT',@Shipment,@Key,b.available,b.average_cost,b.version,now(),@UserId from agro360.inventory_stock_lots l join agro360.inventory_stock_balances b on b.tenant_id=l.tenant_id and b.warehouse_id=l.warehouse_id and b.product_id=l.product_id where l.tenant_id=@TenantId and l.id=@LotId and not exists(select 1 from agro360.inventory_stock_movements m where m.tenant_id=@TenantId and m.idempotency_key=@Key)", new { Movement = Guid.CreateVersion7(), tenant.TenantId, item.LotId, item.Quantity, item.Unit, Shipment = id, Key = $"fulfillment:{id}:{item.Id}", tenant.UserId }, t, cancellationToken: ct));
            if (movementCreated != 1) throw new ConflictException("A saída física já possui movimento ou não pôde ser persistida.");
            var reservationConsumed = await c.ExecuteAsync(new CommandDefinition("update agro360.fulfillment_reservations set status=case when consumed_quantity+@Quantity=quantity then 'CONSUMED' else 'RELEASED' end,consumed_quantity=consumed_quantity+@Quantity,released_quantity=released_quantity+@Remainder,version=version+1,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Reservation and status='ACTIVE' and quantity-consumed_quantity-released_quantity=@Active", new { tenant.TenantId, Reservation = item.ReservationId, item.Quantity, Remainder = remainder, Active = active, tenant.UserId }, t, cancellationToken: ct));
            if (reservationConsumed != 1) throw new ConflictException("A reserva mudou durante a confirmação da saída.");
        }
        var shipmentChanged = await c.ExecuteAsync(new CommandDefinition("update agro360.fulfillment_shipments set status='DISPATCHED',dispatched_at=now(),dispatch_idempotency_key=@Key,dispatch_request_hash=@Hash,version=version+1,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id and status='CHECKED' and version=@Version", new { tenant.TenantId, Id = id, Key = command.IdempotencyKey, Hash = requestHash, Version = command.Version, tenant.UserId }, t, cancellationToken: ct));
        if (shipmentChanged != 1) throw new ConflictException("O documento mudou durante a confirmação da saída.");
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
                       coalesce(soi.product_id, lot.product_id) productid, i.stock_lot_id lotid
                from agro360.fulfillment_returns r
                join agro360.fulfillment_shipment_items i on i.tenant_id=r.tenant_id and i.id=r.shipment_item_id
                left join agro360.sales_order_items soi on soi.tenant_id=i.tenant_id and soi.id=i.order_item_id
                left join agro360.inventory_stock_lots lot on lot.tenant_id=i.tenant_id and lot.id=i.stock_lot_id
                where r.tenant_id=@TenantId and r.id=@Id and r.status in('AWAITING_RECEIPT','PARTIALLY_RECEIVED','AWAITING_QUALITY')
                for update of r
                """,new{tenant.TenantId,Id=id},t,cancellationToken:ct));
            if(item==default)throw new ConflictException("Retorno inexistente ou não disponível para recebimento.");
            if(item.Version!=command.ExpectedVersion)throw new ConflictException("O retorno foi alterado. Recarregue antes de confirmar.");
            if(!string.Equals(item.Unit,command.Unit,StringComparison.OrdinalIgnoreCase))throw new DomainException("A unidade deve coincidir com a expedição; conversão não configurada.");
            if(item.Received+command.Quantity>item.Authorized)throw new ConflictException("Quantidade supera o saldo autorizado do retorno.");
            if(!await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.inventory_warehouses where tenant_id=@TenantId and id=@WarehouseId and deleted_at is null)",new{tenant.TenantId,command.WarehouseId},t,cancellationToken:ct)))throw new DomainException("Local de recebimento indisponível.");
            var receipt=Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.fulfillment_return_receipts(id,tenant_id,return_id,quantity,unit,condition,warehouse_id,lot_number,evidence_document_id,notes,idempotency_key,request_hash,created_by) values(@Receipt,@TenantId,@Id,@Quantity,@Unit,@Condition,@WarehouseId,@LotNumber,@Evidence,@Notes,@Key,@Hash,@UserId); update agro360.fulfillment_returns set received_quantity=received_quantity+@Quantity,status=case when received_quantity+@Quantity<quantity then 'PARTIALLY_RECEIVED' else 'AWAITING_QUALITY' end,received_at=coalesce(received_at,now()),version=version+1,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id; update agro360.fulfillment_shipment_items set returned_quantity=returned_quantity+@Quantity,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@ShipmentItemId",new{Receipt=receipt,tenant.TenantId,Id=id,command.Quantity,Unit=command.Unit.ToLowerInvariant(),Condition=condition,command.WarehouseId,command.LotNumber,Evidence=command.EvidenceDocumentId,command.Notes,Key=command.IdempotencyKey,Hash=hash,tenant.UserId,item.ShipmentItemId},t,cancellationToken:ct));
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
        var decision = (command.Decision ?? string.Empty).Trim().ToUpperInvariant();
        StorageRules.ValidateReturnDecision(decision, command.Quantity, command.Reason, command.IdempotencyKey, command.Unit, command.Cost);

        var hash = Hash(command);
        return Tx(async (c, t) =>
        {
            var old = await c.QuerySingleOrDefaultAsync<ReturnDecisionExistingRow>(new CommandDefinition(
                "select id Id, request_hash RequestHash from agro360.fulfillment_return_decisions where tenant_id=@TenantId and idempotency_key=@Key",
                new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
            if (old is not null && old.Id != Guid.Empty)
            {
                if (old.RequestHash != hash)
                    throw new ConflictException("Chave de idempotência reutilizada com conteúdo diferente.", "return.idempotency_conflict");
                return old.Id;
            }

            var state = await c.QuerySingleOrDefaultAsync<ReturnStateRow>(new CommandDefinition("""
                select r.received_quantity Received,
                       coalesce((select sum(d.quantity) from agro360.fulfillment_return_decisions d where d.tenant_id=r.tenant_id and d.return_id=r.id),0) Decided,
                       r.version Version,
                       r.status Status
                from agro360.fulfillment_returns r
                where r.tenant_id=@TenantId and r.id=@Id and r.status in ('AWAITING_QUALITY','BLOCKED')
                for update
                """, new { tenant.TenantId, Id = id }, t, cancellationToken: ct));

            if (state is null)
                throw new ConflictException("Retorno não encontrado ou indisponível para destinação.", "return.not_found_or_closed");
            if (state.Version != command.ExpectedVersion)
                throw new ConflictException("Retorno alterado concorrentemente. Recarregue os dados antes de destinar.", "return.concurrency_conflict");
            if (state.Decided + command.Quantity > state.Received)
                throw new ConflictException("A destinação supera a quantidade fisicamente recebida.", "return.quantity_exceeded");

            var qualityRecords = (await c.QueryAsync<ReturnQualityEvaluationRow>(new CommandDefinition("""
                select
                    rc.unit as ReceiptUnit,
                    coalesce(i.status, 'NO_INTENT') as IntentStatus,
                    r.status as RunStatus,
                    r.overall_result as RunResult
                from agro360.fulfillment_return_receipts rc
                left join agro360.quality_inspection_event_intents i
                    on i.tenant_id = rc.tenant_id
                    and i.origin_type = 'fulfillment_return_receipts'
                    and i.origin_id = rc.id
                    and i.deleted_at is null
                left join agro360.quality_inspection_runs r
                    on r.tenant_id = i.tenant_id
                    and r.id = i.run_id
                    and r.deleted_at is null
                where rc.tenant_id = @TenantId and rc.return_id = @Id
                """, new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).AsList();

            var fallbackUnit = qualityRecords.Select(q => q.ReceiptUnit).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u));
            var finalUnit = string.IsNullOrWhiteSpace(command.Unit) ? fallbackUnit : command.Unit.Trim().ToLowerInvariant();

            StorageRules.ValidateReturnDecision(decision, command.Quantity, command.Reason, command.IdempotencyKey, finalUnit, command.Cost);

            if (decision == "RELEASE")
            {
                var tuples = qualityRecords.Select(q => ((string?)q.IntentStatus, (string?)q.RunStatus, (string?)q.RunResult)).ToList();
                StorageRules.ValidateReturnQualityForRelease(tuples);
            }

            var decisionId = Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition("""
                insert into agro360.fulfillment_return_decisions(
                    id, tenant_id, return_id, decision, quantity, unit, cost, reason,
                    idempotency_key, request_hash, created_by)
                values(
                    @DecisionId, @TenantId, @Id, @Decision, @Quantity, @Unit, @Cost, @Reason,
                    @Key, @Hash, @UserId);

                update agro360.fulfillment_returns set
                    status = case
                        when @Decision = 'RELEASE' and @Quantity + @Decided >= received_quantity then 'RELEASED'
                        when @Decision = 'DISPOSE' and @Quantity + @Decided >= received_quantity then 'DISPOSED'
                        else 'BLOCKED'
                    end,
                    quality_decision_at = now(),
                    version = version + 1,
                    updated_at = now(),
                    updated_by = @UserId
                where tenant_id = @TenantId and id = @Id;
                """, new
            {
                DecisionId = decisionId,
                tenant.TenantId,
                Id = id,
                Decision = decision,
                command.Quantity,
                Unit = finalUnit,
                command.Cost,
                command.Reason,
                Key = command.IdempotencyKey,
                Hash = hash,
                tenant.UserId,
                state.Decided
            }, t, cancellationToken: ct));

            await Audit(c, t, "return.decide", id, command, ct);
            return decisionId;
        });
    }

    public Task<IReadOnlyList<dynamic>> ListReturnsAsync(CancellationToken ct) => Tx<IReadOnlyList<dynamic>>(async (c, t) =>
    {
        const string sql = """
            select
                r.id as Id,
                r.status as Status,
                r.quantity as Quantity,
                r.received_quantity as ReceivedQuantity,
                r.version as Version,
                r.reason as Reason,
                r.created_at as CreatedAt,
                r.quality_decision_at as QualityDecisionAt,
                s.number as ShipmentNumber,
                so.order_number as OrderNumber,
                c.name as CustomerName,
                p.name as ProductName,
                p.code as ProductCode,
                coalesce(si.unit, '') as Unit,
                latest_intent.status as IntentStatus,
                latest_run.overall_result as QualityResult,
                latest_run.status as RunStatus,
                latest_run.number as RunNumber
            from agro360.fulfillment_returns r
            join agro360.fulfillment_shipment_items si on si.tenant_id = r.tenant_id and si.id = r.shipment_item_id
            join agro360.fulfillment_shipments s on s.tenant_id = r.tenant_id and s.id = si.shipment_id
            join agro360.sales_orders so on so.tenant_id = r.tenant_id and so.id = s.order_id
            join agro360.crm_customers c on c.tenant_id = r.tenant_id and c.id = so.customer_id
            left join agro360.stock_lots sl on sl.tenant_id = r.tenant_id and sl.id = si.stock_lot_id
            left join agro360.catalog_products p on p.tenant_id = r.tenant_id and p.id = sl.product_id
            left join lateral (
                select rc.id, i.status, i.run_id
                from agro360.fulfillment_return_receipts rc
                left join agro360.quality_inspection_event_intents i
                    on i.tenant_id = rc.tenant_id and i.origin_type = 'fulfillment_return_receipts' and i.origin_id = rc.id and i.deleted_at is null
                where rc.tenant_id = r.tenant_id and rc.return_id = r.id
                order by rc.received_at desc
                limit 1
            ) latest_intent on true
            left join agro360.quality_inspection_runs latest_run
                on latest_run.tenant_id = r.tenant_id and latest_run.id = latest_intent.run_id and latest_run.deleted_at is null
            where r.tenant_id = @TenantId
            order by r.created_at desc
            """;

        var rows = (await c.QueryAsync<FulfillmentReturnDtoRow>(new CommandDefinition(sql, new { tenant.TenantId }, t, cancellationToken: ct))).AsList();
        return rows;
    });

    public Task<dynamic?> ReturnDetailAsync(Guid id, CancellationToken ct) => Tx<dynamic?>(async (c, t) =>
    {
        const string headerSql = """
            select
                r.id as Id,
                r.status as Status,
                r.quantity as Quantity,
                r.received_quantity as ReceivedQuantity,
                r.version as Version,
                r.reason as Reason,
                r.created_at as CreatedAt,
                r.quality_decision_at as QualityDecisionAt,
                s.number as ShipmentNumber,
                so.order_number as OrderNumber,
                c.name as CustomerName,
                p.name as ProductName,
                p.code as ProductCode,
                coalesce(si.unit, '') as Unit,
                latest_intent.status as IntentStatus,
                latest_run.overall_result as QualityResult,
                latest_run.status as RunStatus,
                latest_run.number as RunNumber
            from agro360.fulfillment_returns r
            join agro360.fulfillment_shipment_items si on si.tenant_id = r.tenant_id and si.id = r.shipment_item_id
            join agro360.fulfillment_shipments s on s.tenant_id = r.tenant_id and s.id = si.shipment_id
            join agro360.sales_orders so on so.tenant_id = r.tenant_id and so.id = s.order_id
            join agro360.crm_customers c on c.tenant_id = r.tenant_id and c.id = so.customer_id
            left join agro360.stock_lots sl on sl.tenant_id = r.tenant_id and sl.id = si.stock_lot_id
            left join agro360.catalog_products p on p.tenant_id = r.tenant_id and p.id = sl.product_id
            left join lateral (
                select rc.id, i.status, i.run_id
                from agro360.fulfillment_return_receipts rc
                left join agro360.quality_inspection_event_intents i
                    on i.tenant_id = rc.tenant_id and i.origin_type = 'fulfillment_return_receipts' and i.origin_id = rc.id and i.deleted_at is null
                where rc.tenant_id = r.tenant_id and rc.return_id = r.id
                order by rc.received_at desc
                limit 1
            ) latest_intent on true
            left join agro360.quality_inspection_runs latest_run
                on latest_run.tenant_id = r.tenant_id and latest_run.id = latest_intent.run_id and latest_run.deleted_at is null
            where r.tenant_id = @TenantId and r.id = @Id
            """;

        var header = await c.QuerySingleOrDefaultAsync<FulfillmentReturnDtoRow>(new CommandDefinition(headerSql, new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        if (header is null) return null;

        const string receiptsSql = """
            select
                rc.id as Id,
                rc.quantity as Quantity,
                rc.unit as Unit,
                rc.condition as Condition,
                w.name as WarehouseName,
                rc.lot_number as LotNumber,
                rc.notes as Notes,
                rc.received_at as ReceivedAt,
                i.status as IntentStatus,
                qrun.overall_result as QualityResult
            from agro360.fulfillment_return_receipts rc
            left join agro360.inventory_warehouses w on w.tenant_id = rc.tenant_id and w.id = rc.warehouse_id
            left join agro360.quality_inspection_event_intents i
                on i.tenant_id = rc.tenant_id and i.origin_type = 'fulfillment_return_receipts' and i.origin_id = rc.id and i.deleted_at is null
            left join agro360.quality_inspection_runs qrun
                on qrun.tenant_id = i.tenant_id and qrun.id = i.run_id and qrun.deleted_at is null
            where rc.tenant_id = @TenantId and rc.return_id = @Id
            order by rc.received_at desc
            """;
        var receipts = (await c.QueryAsync<ReturnReceiptDtoRow>(new CommandDefinition(receiptsSql, new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).AsList();

        const string decisionsSql = """
            select
                d.id as Id,
                d.decision as Decision,
                d.quantity as Quantity,
                d.unit as Unit,
                d.cost as Cost,
                d.reason as Reason,
                d.decided_at as DecidedAt,
                coalesce(u.name, u.email) as DecidedBy
            from agro360.fulfillment_return_decisions d
            left join agro360.identity_users u on u.tenant_id = d.tenant_id and u.id = d.created_by
            where d.tenant_id = @TenantId and d.return_id = @Id
            order by d.decided_at desc
            """;
        var decisions = (await c.QueryAsync<ReturnDecisionDtoRow>(new CommandDefinition(decisionsSql, new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).AsList();

        return new
        {
            Return = header,
            Receipts = receipts,
            Decisions = decisions
        };
    });
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
    public Task<Guid> PlanTripAsync(PlanTripCommand command, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(command.IdempotencyKey) || command.PlannedEnd <= command.PlannedStart || command.Stops.Count < 2 || command.Legs.Count == 0 || command.Allocations.Count == 0)
            throw new DomainException("Planejamento exige chave, período, paradas, trechos e cargas.", "logistics.plan_invalid");
        if (command.Stops.Select(x => x.Sequence).Distinct().Count() != command.Stops.Count || command.Stops.Any(x => x.Sequence <= 0))
            throw new DomainException("A sequência das paradas deve ser positiva e única.", "logistics.stop_sequence_invalid");
        foreach (var leg in command.Legs)
        {
            var loads = command.Allocations.Where(a => a.LoadingStopSequence <= leg.OriginStopSequence && a.UnloadingStopSequence >= leg.DestinationStopSequence).ToList();
            decimal? used = null;
            if (!string.IsNullOrWhiteSpace(leg.CapacityUnit) && loads.All(a => a.Unit.Equals(leg.CapacityUnit, StringComparison.OrdinalIgnoreCase)))
                used = loads.Sum(a => a.Quantity);
            else if (!string.IsNullOrWhiteSpace(leg.CapacityUnit) && loads.All(a => a.Weight is not null && string.Equals(a.WeightUnit, leg.CapacityUnit, StringComparison.OrdinalIgnoreCase)))
                used = loads.Sum(a => a.Weight!.Value);
            else if (!string.IsNullOrWhiteSpace(leg.CapacityUnit) && loads.All(a => a.Volume is not null && string.Equals(a.VolumeUnit, leg.CapacityUnit, StringComparison.OrdinalIgnoreCase)))
                used = loads.Sum(a => a.Volume!.Value);
            if (used is null)
                throw new DomainException("Não há medição compatível para comprovar a capacidade do trecho; informe peso, volume ou unidade sem conversão implícita.", "logistics.capacity_measurement_pending");
            LogisticsRules.ValidateCapacity(leg.CapacityTotal, used, leg.CapacityUnit);
            if (!LogisticsRules.RouteTypes.Contains(leg.Mode.Trim().ToUpperInvariant()) || leg.OriginStopSequence >= leg.DestinationStopSequence)
                throw new DomainException("Trecho ou ordem das paradas é inválido.", "logistics.leg_invalid");
            if (leg.Mode.Equals("RIVER", StringComparison.OrdinalIgnoreCase) &&
                (string.IsNullOrWhiteSpace(leg.NavigationSource) || leg.NavigationValidUntil is null || leg.NavigationResponsibleId is null))
                throw new DomainException("Trecho fluvial exige fonte e validade da informação manual de navegabilidade.", "logistics.navigation_evidence_required");
        }
        var hash = Hash(command);
        return Tx(async (c, t) =>
        {
            var old = await c.QuerySingleOrDefaultAsync<(Guid Id, string RequestHash)>(new CommandDefinition("select trip_id id,request_hash requesthash from agro360.logistics_trip_plans where tenant_id=@TenantId and idempotency_key=@Key", new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
            if (old.Id != Guid.Empty) { if (old.RequestHash != hash) throw new ConflictException("Chave de idempotência reutilizada com outro planejamento."); return old.Id; }
            foreach (var allocation in command.Allocations)
            {
                if (allocation.Quantity <= 0 || string.IsNullOrWhiteSpace(allocation.Unit) || allocation.LoadingStopSequence >= allocation.UnloadingStopSequence)
                    throw new DomainException("Carga, unidade ou paradas da alocação são inválidas.", "logistics.allocation_invalid");
                await c.ExecuteAsync(new CommandDefinition("select pg_advisory_xact_lock(hashtextextended(@Key,0))", new { Key = $"trip-allocation:{tenant.TenantId}:{allocation.ShipmentItemId}" }, t, cancellationToken: ct));
                var pending = await c.ExecuteScalarAsync<decimal?>(new CommandDefinition("select i.checked_quantity-coalesce((select sum(a.quantity) from agro360.logistics_trip_allocations a where a.tenant_id=i.tenant_id and a.shipment_item_id=i.id and a.status<>'CANCELLED'),0) from agro360.fulfillment_shipment_items i join agro360.fulfillment_shipments s on s.tenant_id=i.tenant_id and s.id=i.shipment_id where i.tenant_id=@TenantId and i.id=@Item and lower(i.unit)=lower(@Unit) and s.status in('CHECKED','DISPATCHED','IN_DELIVERY','PARTIAL','RETURN_PENDING') for update of i", new { tenant.TenantId, Item = allocation.ShipmentItemId, allocation.Unit }, t, cancellationToken: ct));
                if (pending is null || pending < allocation.Quantity) throw new ConflictException("Alocação excede o saldo pendente ou usa unidade incompatível.");
            }
            var id = Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.logistics_trips(id,tenant_id,number,origin,destination,estimated_distance,carrier,freight_type,transport_mode,freight_value,cost_per_tonne,cost_per_km,status,responsible_id,planned_start,planned_end,created_by) values(@Id,@TenantId,@Number,@Origin,@Destination,0,@Carrier,'PENDING','MIXED',0,0,0,'PLANNED',@ResponsibleId,@PlannedStart,@PlannedEnd,@UserId); insert into agro360.logistics_trip_plans(id,tenant_id,trip_id,idempotency_key,request_hash,version,created_by) values(@Plan,@TenantId,@Id,@Key,@Hash,1,@UserId)", new { Id = id, Plan = Guid.CreateVersion7(), tenant.TenantId, command.Number, command.Origin, command.Destination, command.Carrier, command.ResponsibleId, command.PlannedStart, command.PlannedEnd, Key = command.IdempotencyKey, Hash = hash, tenant.UserId }, t, cancellationToken: ct));
            foreach (var stop in command.Stops) await c.ExecuteAsync(new CommandDefinition("insert into agro360.logistics_trip_stops(id,tenant_id,trip_id,sequence,type,name,operational_window,planned_arrival,planned_departure,created_by) values(@Id,@TenantId,@Trip,@Sequence,@Type,@Name,@Window,@Arrival,@Departure,@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, Trip = id, stop.Sequence, Type = stop.Type.Trim().ToUpperInvariant(), stop.Name, Window = stop.OperationalWindow, Arrival = stop.PlannedArrival, Departure = stop.PlannedDeparture, tenant.UserId }, t, cancellationToken: ct));
            foreach (var leg in command.Legs) await c.ExecuteAsync(new CommandDefinition("insert into agro360.logistics_trip_legs(id,tenant_id,trip_id,sequence,origin_stop_sequence,destination_stop_sequence,mode,asset_id,capacity_total,capacity_unit,navigation_source,navigation_valid_until,navigation_responsible_id,created_by) values(@Id,@TenantId,@Trip,@Sequence,@Origin,@Destination,@Mode,@Asset,@Capacity,@Unit,@Source,@ValidUntil,@NavigationResponsibleId,@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, Trip = id, leg.Sequence, Origin = leg.OriginStopSequence, Destination = leg.DestinationStopSequence, Mode = leg.Mode.Trim().ToUpperInvariant(), Asset = leg.AssetId, Capacity = leg.CapacityTotal, Unit = leg.CapacityUnit, Source = leg.NavigationSource, ValidUntil = leg.NavigationValidUntil, leg.NavigationResponsibleId, tenant.UserId }, t, cancellationToken: ct));
            foreach (var a in command.Allocations) await c.ExecuteAsync(new CommandDefinition("insert into agro360.logistics_trip_allocations(id,tenant_id,trip_id,shipment_item_id,quantity,unit,loading_stop_sequence,unloading_stop_sequence,weight,weight_unit,volume,volume_unit,status,created_by) values(@Id,@TenantId,@Trip,@Item,@Quantity,@Unit,@Loading,@Unloading,@Weight,@WeightUnit,@Volume,@VolumeUnit,'PLANNED',@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, Trip = id, Item = a.ShipmentItemId, a.Quantity, Unit = a.Unit.Trim().ToLowerInvariant(), Loading = a.LoadingStopSequence, Unloading = a.UnloadingStopSequence, a.Weight, a.WeightUnit, a.Volume, a.VolumeUnit, tenant.UserId }, t, cancellationToken: ct));
            await Audit(c, t, "trip.plan", id, command, ct); return id;
        });
    }
    public Task<dynamic?> TripDetailAsync(Guid id, CancellationToken ct) => Tx<dynamic?>(async (c, t) =>
    {
        var trip = await c.QuerySingleOrDefaultAsync(new CommandDefinition("select * from agro360.logistics_trips where tenant_id=@TenantId and id=@Id", new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        if (trip is null) return null;
        var stops = (await c.QueryAsync(new CommandDefinition("select * from agro360.logistics_trip_stops where tenant_id=@TenantId and trip_id=@Id order by sequence", new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).AsList();
        var legs = (await c.QueryAsync(new CommandDefinition("select * from agro360.logistics_trip_legs where tenant_id=@TenantId and trip_id=@Id order by sequence", new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).AsList();
        var allocations = (await c.QueryAsync(new CommandDefinition("select a.*,i.checked_quantity dispatched_reference from agro360.logistics_trip_allocations a join agro360.fulfillment_shipment_items i on i.tenant_id=a.tenant_id and i.id=a.shipment_item_id where a.tenant_id=@TenantId and a.trip_id=@Id order by a.loading_stop_sequence,a.id", new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).AsList();
        return new { trip, stops, legs, allocations };
    });
    private static string Hash<T>(T command) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(command))));
    private async Task<Guid> RecordOperation(NpgsqlConnection c, NpgsqlTransaction t, string operation, Guid aggregateId, string key, string hash, long resultVersion, CancellationToken ct)
    {
        var id = Guid.CreateVersion7();
        await c.ExecuteAsync(new CommandDefinition("insert into agro360.fulfillment_operation_requests(id,tenant_id,operation,aggregate_id,idempotency_key,request_hash,result_version,created_by) values(@Id,@TenantId,@Operation,@AggregateId,@Key,@Hash,@ResultVersion,@UserId)", new { Id = id, tenant.TenantId, Operation = operation, AggregateId = aggregateId, Key = key, Hash = hash, ResultVersion = resultVersion, tenant.UserId }, t, cancellationToken: ct));
        return id;
    }
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

public sealed class ReturnDecisionExistingRow
{
    public Guid Id { get; set; }
    public string RequestHash { get; set; } = string.Empty;
}

public sealed class ReturnStateRow
{
    public decimal Received { get; set; }
    public decimal Decided { get; set; }
    public long Version { get; set; }
    public string Status { get; set; } = string.Empty;
}

public sealed class ReturnQualityEvaluationRow
{
    public string? ReceiptUnit { get; set; }
    public string IntentStatus { get; set; } = string.Empty;
    public string? RunStatus { get; set; }
    public string? RunResult { get; set; }
}

public sealed class FulfillmentReturnDtoRow
{
    public Guid Id { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal ReceivedQuantity { get; set; }
    public long Version { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? QualityDecisionAt { get; set; }
    public string? ShipmentNumber { get; set; }
    public string? OrderNumber { get; set; }
    public string? CustomerName { get; set; }
    public string? ProductName { get; set; }
    public string? ProductCode { get; set; }
    public string? Unit { get; set; }
    public string? IntentStatus { get; set; }
    public string? QualityResult { get; set; }
    public string? RunStatus { get; set; }
    public string? RunNumber { get; set; }
}

public sealed class ReturnReceiptDtoRow
{
    public Guid Id { get; set; }
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public string? WarehouseName { get; set; }
    public string? LotNumber { get; set; }
    public string? Notes { get; set; }
    public DateTimeOffset ReceivedAt { get; set; }
    public string? IntentStatus { get; set; }
    public string? QualityResult { get; set; }
}

public sealed class ReturnDecisionDtoRow
{
    public Guid Id { get; set; }
    public string Decision { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public string? Unit { get; set; }
    public decimal? Cost { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset DecidedAt { get; set; }
    public string? DecidedBy { get; set; }
}
