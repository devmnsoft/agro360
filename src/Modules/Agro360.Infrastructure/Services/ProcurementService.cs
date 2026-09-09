using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agro360.Application;
using Agro360.Application.Contracts;
using Agro360.Domain.Procurement;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Agro360.Infrastructure.Services;

public sealed class ProcurementService(DatabaseExecutor db, ITenantContext tenant, ILogger<ProcurementService> logger) : IProcurementService
{
    public Task<dynamic> DashboardAsync(CancellationToken ct) => Tx(async (c, t) => await c.QuerySingleAsync(new CommandDefinition("select count(*) filter(where status='OPEN') requisitions_open,count(*) filter(where status='OPEN' and priority='URGENT') requisitions_urgent,(select count(*) from agro360.procurement_quotations where tenant_id=@TenantId and status in('SENT','PARTIAL','RESPONDED','ANALYSIS')) quotations_running,(select count(*) from agro360.procurement_purchase_orders where tenant_id=@TenantId and status='AWAITING_APPROVAL') orders_awaiting_approval,(select count(*) from agro360.procurement_purchase_orders where tenant_id=@TenantId and status='PARTIALLY_RECEIVED') orders_partially_received,(select count(*) from agro360.procurement_receipts where tenant_id=@TenantId and status='DIVERGENT') divergent_receipts,(select count(*) from agro360.procurement_suppliers where tenant_id=@TenantId and status in('ACTIVE','APPROVED')) active_suppliers,(select count(*) from agro360.procurement_suppliers where tenant_id=@TenantId and status='BLOCKED') blocked_suppliers,(select coalesce(sum(total),0) from agro360.procurement_purchase_orders where tenant_id=@TenantId and status not in('DRAFT','CANCELLED') and approved_at>=date_trunc('month',now())) purchased_month from agro360.procurement_requisitions where tenant_id=@TenantId", new { tenant.TenantId }, t, cancellationToken: ct)));
    public Task<IReadOnlyList<dynamic>> SuppliersAsync(ProcurementQuery q, CancellationToken ct) => List("select * from agro360.procurement_suppliers where tenant_id=@TenantId and deleted_at is null and (@Search is null or legal_name ilike '%'||@Search||'%' or trade_name ilike '%'||@Search||'%') and (@Status is null or status=@Status) and (@Category is null or main_category=@Category) order by legal_name limit @Take offset @Skip", q, ct);
    public Task<Guid> SaveSupplierAsync(Guid? id, ProcurementSupplierCommand x, CancellationToken ct)
    {
        ProcurementRules.Supplier(x.LegalName, x.Email, x.Status, x.RejectionReason); if (x.AverageDeliveryDays < 0) throw new DomainException("Prazo médio inválido.", "agro360.procurement_delivery_invalid");
        return Tx(async (c, t) => { var key = id ?? Guid.CreateVersion7(); var p = new DynamicParameters(x); p.Add("Id", key); p.Add("TenantId", tenant.TenantId); p.Add("UserId", tenant.UserId); var sql = id is null ? "insert into agro360.procurement_suppliers(id,tenant_id,legal_name,trade_name,tax_document,state_registration,supplier_type,main_category,email,phone,address,city,state,country,main_contact,payment_terms,average_delivery_days,status,rejection_reason,notes,tags,created_by,updated_by) values(@Id,@TenantId,@LegalName,@TradeName,nullif(regexp_replace(coalesce(@TaxDocument,''),'[^0-9]','','g'),''),@StateRegistration,@Type,@Category,@Email,@Phone,@Address,@City,@State,@Country,@MainContact,@PaymentTerms,@AverageDeliveryDays,@Status,@RejectionReason,@Notes,@Tags,@UserId,@UserId)" : "update agro360.procurement_suppliers set legal_name=@LegalName,trade_name=@TradeName,tax_document=nullif(regexp_replace(coalesce(@TaxDocument,''),'[^0-9]','','g'),''),state_registration=@StateRegistration,supplier_type=@Type,main_category=@Category,email=@Email,phone=@Phone,address=@Address,city=@City,state=@State,country=@Country,main_contact=@MainContact,payment_terms=@PaymentTerms,average_delivery_days=@AverageDeliveryDays,status=@Status,rejection_reason=@RejectionReason,notes=@Notes,tags=@Tags,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id and deleted_at is null"; if (await c.ExecuteAsync(new CommandDefinition(sql, p, t, cancellationToken: ct)) == 0) throw new NotFoundException("Fornecedor", key); await Audit(c, t, "SUPPLIER", key, id is null ? "CREATED" : "UPDATED", x, ct); return key; });
    }
    public Task HomologateAsync(Guid supplierId, bool approve, HomologationCommand x, CancellationToken ct)
    {
        if (!approve && string.IsNullOrWhiteSpace(x.Reason)) throw new DomainException("Reprovação exige motivo.", "agro360.procurement_rejection_reason_required"); if (approve && x.ValidUntil <= DateOnly.FromDateTime(DateTime.UtcNow)) throw new DomainException("Validade deve ser futura.", "agro360.procurement_homologation_validity_invalid");
        return Tx(async (c, t) => { if (!await Exists(c, t, "suppliers", supplierId, ct)) throw new NotFoundException("Fornecedor", supplierId); var status = approve ? "APPROVED" : "REJECTED"; await c.ExecuteAsync(new CommandDefinition("insert into agro360.procurement_supplier_homologations(id,tenant_id,supplier_id,status,valid_until,criteria,reason,decided_at,decided_by,created_by,updated_by) values(@Id,@TenantId,@SupplierId,@Status,@ValidUntil,jsonb_build_object('fiscal',@Fiscal,'sanitary',@Sanitary,'environmental',@Environmental,'certifications',@Certifications,'deliveryCapacity',@DeliveryCapacity,'qualityHistory',@QualityHistory,'commercial',@Commercial,'compliance',@Compliance,'operationalRisk',@OperationalRisk),@Reason,now(),@UserId,@UserId,@UserId);update agro360.procurement_suppliers set status=@Status,homologated_at=case when @Status='APPROVED' then now() end,homologated_by=case when @Status='APPROVED' then @UserId end,rejection_reason=@Reason,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@SupplierId", new { Id = Guid.CreateVersion7(), tenant.TenantId, SupplierId = supplierId, Status = status, x.ValidUntil, x.Fiscal, x.Sanitary, x.Environmental, x.Certifications, x.DeliveryCapacity, x.QualityHistory, x.Commercial, x.Compliance, x.OperationalRisk, x.Reason, tenant.UserId }, t, cancellationToken: ct)); await Audit(c, t, "SUPPLIER", supplierId, status, x, ct); });
    }
    public Task<IReadOnlyList<dynamic>> CatalogAsync(ProcurementQuery q, CancellationToken ct) => List("select * from agro360.procurement_item_catalog where tenant_id=@TenantId and deleted_at is null and (@Search is null or name ilike '%'||@Search||'%' or internal_code ilike '%'||@Search||'%') and (@Category is null or category=@Category) and (@Status is null or (@Status='ACTIVE' and active) or (@Status='INACTIVE' and not active)) order by name limit @Take offset @Skip", q, ct);
    public Task<Guid> SaveCatalogItemAsync(Guid? id, CatalogItemCommand x, CancellationToken ct) => Tx(async (c, t) =>
    {
        if (string.IsNullOrWhiteSpace(x.Name) || string.IsNullOrWhiteSpace(x.Code) || string.IsNullOrWhiteSpace(x.Category) || string.IsNullOrWhiteSpace(x.Unit)) throw new DomainException("Nome, código, categoria e unidade são obrigatórios.", "agro360.procurement_catalog_required");
        if (x.MinimumStock < 0) throw new DomainException("Estoque mínimo inválido.", "agro360.procurement_minimum_stock_invalid");
        if (x.Type is not "SERVICE" && x.RelatedProductId is null) throw new DomainException("Materiais e ativos devem estar vinculados a um produto de estoque.", "agro360.procurement_stock_product_required");
        if (x.RelatedProductId is not null && !await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.inventory_products where tenant_id=@TenantId and id=@ProductId and deleted_at is null)", new { tenant.TenantId, ProductId = x.RelatedProductId }, t, cancellationToken: ct))) throw new DomainException("Produto de estoque inexistente ou inativo.", "agro360.procurement_stock_product_invalid");
        var key = id ?? Guid.CreateVersion7();
        var p = new DynamicParameters(x);
        p.Add("Id", key); p.Add("TenantId", tenant.TenantId); p.Add("UserId", tenant.UserId);
        var sql = id is null
            ? "insert into agro360.procurement_item_catalog(id,tenant_id,name,internal_code,category,unit,item_type,description,active,minimum_stock,cost_center_id,related_product_id,requires_lot,requires_expiry,requires_document,requires_inspection,requires_approved_supplier,notes,created_by,updated_by) values(@Id,@TenantId,@Name,@Code,@Category,@Unit,@Type,@Description,@Active,@MinimumStock,@CostCenterId,@RelatedProductId,@RequiresLot,@RequiresExpiry,@RequiresDocument,@RequiresInspection,@RequiresApprovedSupplier,@Notes,@UserId,@UserId)"
            : "update agro360.procurement_item_catalog set name=@Name,internal_code=@Code,category=@Category,unit=@Unit,item_type=@Type,description=@Description,active=@Active,minimum_stock=@MinimumStock,cost_center_id=@CostCenterId,related_product_id=@RelatedProductId,requires_lot=@RequiresLot,requires_expiry=@RequiresExpiry,requires_document=@RequiresDocument,requires_inspection=@RequiresInspection,requires_approved_supplier=@RequiresApprovedSupplier,notes=@Notes,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id";
        if (await c.ExecuteAsync(new CommandDefinition(sql, p, t, cancellationToken: ct)) == 0) throw new NotFoundException("Item", key);
        return key;
    });
    public Task<Guid> CreateRequisitionAsync(RequisitionCommand x, CancellationToken ct)
    {
        ProcurementRules.Requisition(x.Priority, x.NeededOn, x.Items.Count, x.Justification); if (x.Items.Any(i => i.Quantity <= 0)) throw new DomainException("Quantidades devem ser positivas.", "agro360.procurement_quantity_invalid");
        return Tx(async (c, t) => { foreach (var line in x.Items) if (!await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.procurement_item_catalog where tenant_id=@TenantId and id=@Id and active and deleted_at is null)", new { tenant.TenantId, Id = line.CatalogItemId }, t, cancellationToken: ct))) throw new DomainException("Item inexistente ou inativo.", "agro360.procurement_item_inactive"); var id = Guid.CreateVersion7(); var number = await Number(c, t, "REQ", ct); await c.ExecuteAsync(new CommandDefinition("insert into agro360.procurement_requisitions(id,tenant_id,number,requester_id,origin,cost_center_id,property_id,justification,priority,needed_on,status,created_by,updated_by) values(@Id,@TenantId,@Number,@UserId,@Origin,@CostCenterId,@PropertyId,@Justification,@Priority,@NeededOn,'OPEN',@UserId,@UserId)", new { Id = id, tenant.TenantId, Number = number, tenant.UserId, x.Origin, x.CostCenterId, x.PropertyId, x.Justification, Priority = x.Priority.ToUpperInvariant(), x.NeededOn }, t, cancellationToken: ct)); foreach (var i in x.Items) await c.ExecuteAsync(new CommandDefinition("insert into agro360.procurement_requisition_items(id,tenant_id,requisition_id,catalog_item_id,quantity,unit,notes,created_by,updated_by) values(@Id,@TenantId,@RequisitionId,@CatalogItemId,@Quantity,@Unit,@Notes,@UserId,@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, RequisitionId = id, i.CatalogItemId, i.Quantity, i.Unit, i.Notes, tenant.UserId }, t, cancellationToken: ct)); return id; });
    }
    public Task<IReadOnlyList<dynamic>> RequisitionsAsync(ProcurementQuery q, CancellationToken ct) => List("select r.*,count(i.id) item_count from agro360.procurement_requisitions r left join agro360.procurement_requisition_items i on i.tenant_id=r.tenant_id and i.requisition_id=r.id where r.tenant_id=@TenantId and r.deleted_at is null and (@Search is null or r.number ilike '%'||@Search||'%') and (@Status is null or r.status=@Status) group by r.id order by r.created_at desc limit @Take offset @Skip", q, ct);
    public Task<Guid> CreateOrderAsync(PurchaseOrderCommand x, CancellationToken ct)
    {
        var total = ProcurementRules.OrderTotal(x.Items.Select(i => (i.Quantity, i.UnitPrice, i.Discount)), x.Freight, x.Taxes); return Tx(async (c, t) => { var supplier = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition("select status from agro360.procurement_suppliers where tenant_id=@TenantId and id=@Id and deleted_at is null", new { tenant.TenantId, Id = x.SupplierId }, t, cancellationToken: ct)); if (supplier is null) throw new NotFoundException("Fornecedor", x.SupplierId); if (supplier is "BLOCKED" or "INACTIVE" or "REJECTED") throw new DomainException("Fornecedor indisponível para compras.", "agro360.procurement_supplier_unavailable"); var id = Guid.CreateVersion7(); var number = await Number(c, t, "PO", ct); await c.ExecuteAsync(new CommandDefinition("insert into agro360.procurement_purchase_orders(id,tenant_id,number,supplier_id,requisition_id,quotation_id,cost_center_id,property_id,payment_terms,delivery_on,delivery_address,freight,taxes,total,status,created_by,updated_by) values(@Id,@TenantId,@Number,@SupplierId,@RequisitionId,@QuotationId,@CostCenterId,@PropertyId,@PaymentTerms,@DeliveryOn,@DeliveryAddress,@Freight,@Taxes,@Total,'AWAITING_APPROVAL',@UserId,@UserId)", new { Id = id, tenant.TenantId, Number = number, x.SupplierId, x.RequisitionId, x.QuotationId, x.CostCenterId, x.PropertyId, x.PaymentTerms, x.DeliveryOn, x.DeliveryAddress, x.Freight, x.Taxes, Total = total, tenant.UserId }, t, cancellationToken: ct)); foreach (var i in x.Items) await c.ExecuteAsync(new CommandDefinition("insert into agro360.procurement_purchase_order_items(id,tenant_id,purchase_order_id,catalog_item_id,quantity,unit,unit_price,discount,total,created_by,updated_by) values(@Id,@TenantId,@OrderId,@CatalogItemId,@Quantity,@Unit,@UnitPrice,@Discount,@Total,@UserId,@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, OrderId = id, i.CatalogItemId, i.Quantity, i.Unit, i.UnitPrice, i.Discount, Total = i.Quantity * i.UnitPrice - i.Discount, tenant.UserId }, t, cancellationToken: ct)); return id; });
    }
    public Task<IReadOnlyList<dynamic>> OrdersAsync(ProcurementQuery q, CancellationToken ct) => List("select o.*,s.legal_name supplier_name,(select oi.id from agro360.procurement_purchase_order_items oi where oi.tenant_id=o.tenant_id and oi.purchase_order_id=o.id and oi.received_quantity<oi.quantity order by oi.created_at limit 1) pending_item_id from agro360.procurement_purchase_orders o join agro360.procurement_suppliers s on s.tenant_id=o.tenant_id and s.id=o.supplier_id where o.tenant_id=@TenantId and o.deleted_at is null and (@Search is null or o.number ilike '%'||@Search||'%' or s.legal_name ilike '%'||@Search||'%') and (@Status is null or o.status=@Status) order by o.created_at desc limit @Take offset @Skip", q, ct);
    public Task ApproveOrderAsync(Guid id, string? comment, CancellationToken ct) => Tx(async (c, t) => { var row = await c.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition("select o.requester_id,s.status supplier_status from agro360.procurement_purchase_orders o join agro360.procurement_suppliers s on s.tenant_id=o.tenant_id and s.id=o.supplier_id where o.tenant_id=@TenantId and o.id=@Id for update", new { tenant.TenantId, Id = id }, t, cancellationToken: ct)) ?? throw new NotFoundException("Pedido", id); if ((string)row.supplier_status == "BLOCKED") throw new DomainException("Fornecedor bloqueado.", "agro360.procurement_supplier_blocked"); var n = await c.ExecuteAsync(new CommandDefinition("update agro360.procurement_purchase_orders set status='APPROVED',approved_at=now(),approved_by=@UserId,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id and status='AWAITING_APPROVAL';insert into agro360.procurement_purchase_order_events(id,tenant_id,purchase_order_id,event_type,comment,created_by,updated_by) values(gen_random_uuid(),@TenantId,@Id,'APPROVED',@Comment,@UserId,@UserId)", new { tenant.TenantId, tenant.UserId, Id = id, Comment = comment }, t, cancellationToken: ct)); if (n < 2) throw new ConflictException("Pedido não está aguardando aprovação."); InfrastructureLogMessages.PurchaseOrderApproved(logger, id); });
    public Task<Guid> ReceiveAsync(ProcurementReceiptCommand x, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        if (x.Items.Count == 0) throw new DomainException("Informe os itens recebidos.", "agro360.procurement_items_required");
        if (string.IsNullOrWhiteSpace(x.IdempotencyKey) || x.IdempotencyKey.Trim().Length is < 16 or > 100) throw new DomainException("A chave de idempotência do recebimento é obrigatória.", "agro360.procurement_idempotency_required");
        if (x.Installments is < 1 or > 60 || x.FinanceAccountId is null || x.FirstDueOn is null || x.FirstDueOn < DateOnly.FromDateTime(x.ReceivedAt.UtcDateTime)) throw new DomainException("Conta financeira, primeiro vencimento e parcelas válidas são obrigatórios.", "agro360.procurement_financial_forecast_invalid");

        var idempotencyKey = x.IdempotencyKey.Trim();
        var payloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(x)))).ToLowerInvariant();
        await c.ExecuteAsync(new CommandDefinition("select pg_advisory_xact_lock(hashtextextended(@LockKey,0))", new { LockKey = $"{tenant.TenantId:N}:{idempotencyKey}" }, t, cancellationToken: ct));
        var existing = await c.QuerySingleOrDefaultAsync<ExistingReceipt>(new CommandDefinition("select id,payload_hash PayloadHash from agro360.procurement_receipts where tenant_id=@TenantId and idempotency_key=@IdempotencyKey", new { tenant.TenantId, IdempotencyKey = idempotencyKey }, t, cancellationToken: ct));
        if (existing is not null)
        {
            if (!string.Equals(existing.PayloadHash, payloadHash, StringComparison.Ordinal))
                throw new ConflictException("A chave de idempotência já foi usada com outro conteúdo.", "procurement_idempotency_payload_mismatch");
            return existing.Id;
        }

        var order = await c.QuerySingleOrDefaultAsync<ReceiptOrderRow>(new CommandDefinition("select o.id,o.total,o.cost_center_id CostCenterId,s.legal_name SupplierName from agro360.procurement_purchase_orders o join agro360.procurement_suppliers s on s.tenant_id=o.tenant_id and s.id=o.supplier_id where o.tenant_id=@TenantId and o.id=@OrderId and o.status in('APPROVED','SENT','PARTIALLY_RECEIVED') and o.deleted_at is null for update of o", new { tenant.TenantId, OrderId = x.PurchaseOrderId }, t, cancellationToken: ct)) ?? throw new DomainException("Pedido não está apto ao recebimento.", "agro360.procurement_order_not_receivable");
        var financeAccountValid = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.finance_chart_of_accounts where tenant_id=@TenantId and id=@AccountId and active and type in('EXPENSE','COST','LIABILITY'))", new { tenant.TenantId, AccountId = x.FinanceAccountId }, t, cancellationToken: ct));
        if (!financeAccountValid) throw new DomainException("Conta financeira inexistente ou incompatível.", "agro360.procurement_finance_account_invalid");

        var itemIds = x.Items.Select(item => item.PurchaseOrderItemId).Distinct().ToArray();
        var rows = (await c.QueryAsync<ReceiptItemRow>(new CommandDefinition("select oi.id,oi.quantity,oi.received_quantity ReceivedQuantity,oi.unit_price UnitPrice,oi.unit,c.name,c.requires_lot RequiresLot,c.requires_expiry RequiresExpiry,c.requires_inspection RequiresInspection,c.item_type ItemType,c.related_product_id RelatedProductId,p.base_unit ProductUnit from agro360.procurement_purchase_order_items oi join agro360.procurement_item_catalog c on c.tenant_id=oi.tenant_id and c.id=oi.catalog_item_id left join agro360.inventory_products p on p.tenant_id=c.tenant_id and p.id=c.related_product_id where oi.tenant_id=@TenantId and oi.purchase_order_id=@OrderId and oi.id=any(@ItemIds) and c.active and c.deleted_at is null order by oi.id for update of oi", new { tenant.TenantId, OrderId = x.PurchaseOrderId, ItemIds = itemIds }, t, cancellationToken: ct))).ToDictionary(item => item.Id);
        if (rows.Count != itemIds.Length) throw new DomainException("Um ou mais itens não pertencem ao pedido.", "agro360.procurement_receipt_item_invalid");

        var runningQuantity = rows.ToDictionary(pair => pair.Key, pair => pair.Value.ReceivedQuantity);
        var requiresStock = false;
        var excessItems = new HashSet<Guid>();
        foreach (var item in x.Items)
        {
            var row = rows[item.PurchaseOrderItemId];
            ProcurementRules.Receipt(row.Quantity, runningQuantity[row.Id], item.Quantity, x.OverrideExcess, x.ExcessJustification, row.RequiresLot, item.SupplierLot, row.RequiresExpiry, item.ExpiresOn);
            runningQuantity[row.Id] += item.Quantity;
            if (runningQuantity[row.Id] > row.Quantity) excessItems.Add(row.Id);
            if (row.ItemType != "SERVICE")
            {
                requiresStock = true;
                if (row.RelatedProductId is null) throw new DomainException($"O item '{row.Name}' não está vinculado a produto de estoque.", "agro360.procurement_stock_product_required");
                if (!string.Equals(row.Unit, row.ProductUnit, StringComparison.OrdinalIgnoreCase)) throw new DomainException($"O item '{row.Name}' exige conversão de unidade configurada antes do recebimento.", "agro360.procurement_unit_conversion_required");
            }
        }
        if (excessItems.Count > 0)
        {
            var authorized = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.identity_user_roles ur join agro360.identity_role_permissions rp on rp.tenant_id=ur.tenant_id and rp.role_id=ur.role_id join agro360.identity_permissions p on p.id=rp.permission_id where ur.tenant_id=@TenantId and ur.user_id=@UserId and p.code=@Permission)", new { tenant.TenantId, tenant.UserId, Permission = Permissions.PurchasingOverrideExcess }, t, cancellationToken: ct));
            if (!authorized) throw new ForbiddenException("O usuário não possui permissão específica para autorizar recebimento excedente.");
        }

        if (requiresStock)
        {
            if (x.WarehouseId is null) throw new DomainException("Selecione o depósito para a entrada física.", "agro360.procurement_warehouse_required");
            var warehouseValid = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.inventory_warehouses where tenant_id=@TenantId and id=@WarehouseId and deleted_at is null)", new { tenant.TenantId, x.WarehouseId }, t, cancellationToken: ct));
            if (!warehouseValid) throw new DomainException("Depósito inexistente para esta organização.", "agro360.procurement_warehouse_invalid");
        }

        var id = Guid.CreateVersion7();
        var number = await Number(c, t, "RCV", ct);
        await c.ExecuteAsync(new CommandDefinition("insert into agro360.procurement_receipts(id,tenant_id,number,purchase_order_id,received_at,responsible_id,invoice_document,status,excess_justification,stock_integration_status,finance_integration_status,idempotency_key,payload_hash,warehouse_id,created_by,updated_by) values(@Id,@TenantId,@Number,@OrderId,@ReceivedAt,@UserId,@Invoice,'PENDING',@Reason,'PENDING','PENDING',@IdempotencyKey,@PayloadHash,@WarehouseId,@UserId,@UserId)", new { Id = id, tenant.TenantId, Number = number, OrderId = x.PurchaseOrderId, x.ReceivedAt, tenant.UserId, Invoice = x.InvoiceDocument, Reason = x.ExcessJustification, IdempotencyKey = idempotencyKey, PayloadHash = payloadHash, x.WarehouseId }, t, cancellationToken: ct));

        foreach (var item in x.Items)
        {
            var row = rows[item.PurchaseOrderItemId];
            var receiptItemId = Guid.CreateVersion7();
            var qualityStatus = row.RequiresInspection ? "PENDING" : "NOT_REQUIRED";
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.procurement_receipt_items(id,tenant_id,receipt_id,purchase_order_item_id,quantity,supplier_lot,expires_on,quality_status,notes,created_by,updated_by) values(@Id,@TenantId,@ReceiptId,@PurchaseOrderItemId,@Quantity,@SupplierLot,@ExpiresOn,@QualityStatus,@Notes,@UserId,@UserId); update agro360.procurement_purchase_order_items set received_quantity=received_quantity+@Quantity,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@PurchaseOrderItemId", new { Id = receiptItemId, tenant.TenantId, ReceiptId = id, item.PurchaseOrderItemId, item.Quantity, item.SupplierLot, item.ExpiresOn, QualityStatus = qualityStatus, item.Notes, tenant.UserId }, t, cancellationToken: ct));
            if (row.RequiresInspection)
            {
                if (row.ItemType != "SERVICE")
                    await c.ExecuteAsync(new CommandDefinition("insert into agro360.procurement_receipt_quarantines(tenant_id,receipt_item_id,warehouse_id,product_id,quantity,unit_cost,reason,created_by) values(@TenantId,@ReceiptItemId,@WarehouseId,@ProductId,@Quantity,@UnitCost,'Inspeção obrigatória do catálogo',@UserId)", new { tenant.TenantId, ReceiptItemId = receiptItemId, x.WarehouseId, ProductId = row.RelatedProductId, item.Quantity, UnitCost = row.UnitPrice, tenant.UserId }, t, cancellationToken: ct));
                await c.ExecuteAsync(new CommandDefinition("insert into agro360.procurement_receipt_divergences(tenant_id,receipt_id,receipt_item_id,kind,reason,routing,responsible_id,created_by) values(@TenantId,@ReceiptId,@ReceiptItemId,'QUALITY_INSPECTION','Inspeção obrigatória pendente','QUALITY_REVIEW',@UserId,@UserId)", new { tenant.TenantId, ReceiptId = id, ReceiptItemId = receiptItemId, tenant.UserId }, t, cancellationToken: ct));
            }
            else if (row.ItemType != "SERVICE")
            {
                var movementId = await c.ExecuteScalarAsync<Guid>(new CommandDefinition("select agro360.inventory_apply_stock_movement(@TenantId,@WarehouseId,@ProductId,@Quantity,@UnitCost,'PURCHASE_RECEIPT',@ReferenceId,@Lot,@ExpiresOn,@UserId,@Reason)", new { tenant.TenantId, x.WarehouseId, ProductId = row.RelatedProductId, item.Quantity, UnitCost = row.UnitPrice, ReferenceId = receiptItemId, Lot = item.SupplierLot, item.ExpiresOn, tenant.UserId, Reason = $"Recebimento {number}" }, t, cancellationToken: ct));
                await c.ExecuteAsync(new CommandDefinition("insert into agro360.procurement_receipt_stock_links(tenant_id,receipt_item_id,stock_movement_id,created_by) values(@TenantId,@ReceiptItemId,@MovementId,@UserId)", new { tenant.TenantId, ReceiptItemId = receiptItemId, MovementId = movementId, tenant.UserId }, t, cancellationToken: ct));
            }
            if (excessItems.Contains(row.Id))
                await c.ExecuteAsync(new CommandDefinition("insert into agro360.procurement_receipt_divergences(tenant_id,receipt_id,receipt_item_id,kind,reason,routing,responsible_id,created_by) values(@TenantId,@ReceiptId,@ReceiptItemId,'EXCESS',@Reason,'PROCUREMENT_REVIEW',@UserId,@UserId)", new { tenant.TenantId, ReceiptId = id, ReceiptItemId = receiptItemId, Reason = x.ExcessJustification!, tenant.UserId }, t, cancellationToken: ct));
        }

        var forecastExists = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.procurement_order_financial_links where tenant_id=@TenantId and purchase_order_id=@OrderId)", new { tenant.TenantId, OrderId = x.PurchaseOrderId }, t, cancellationToken: ct));
        if (!forecastExists)
        {
            var remaining = decimal.Round(order.Total, 2, MidpointRounding.AwayFromZero);
            var regularAmount = decimal.Floor(remaining * 100 / x.Installments) / 100;
            for (var installment = 1; installment <= x.Installments; installment++)
            {
                var amount = installment == x.Installments ? remaining : regularAmount;
                remaining -= amount;
                var payableId = Guid.CreateVersion7();
                var dueOn = x.FirstDueOn.Value.AddMonths(installment - 1);
                await c.ExecuteAsync(new CommandDefinition("insert into agro360.finance_payables(id,tenant_id,supplier_name,document,original_amount,discount,interest,fine,final_amount,balance,issued_on,due_on,account_id,cost_center_id,notes,source_id,status,created_by) values(@Id,@TenantId,@Supplier,@Document,@Amount,0,0,0,@Amount,@Amount,@IssuedOn,@DueOn,@AccountId,@CostCenterId,@Notes,@OrderId,'OPEN',@UserId); insert into agro360.procurement_order_financial_links(tenant_id,purchase_order_id,installment,payable_id,amount,created_by) values(@TenantId,@OrderId,@Installment,@Id,@Amount,@UserId)", new { Id = payableId, tenant.TenantId, Supplier = order.SupplierName, Document = x.InvoiceDocument, Amount = amount, IssuedOn = DateOnly.FromDateTime(x.ReceivedAt.UtcDateTime), DueOn = dueOn, AccountId = x.FinanceAccountId, order.CostCenterId, Notes = $"Previsão do pedido {x.PurchaseOrderId:N} · parcela {installment}/{x.Installments}", OrderId = x.PurchaseOrderId, Installment = installment, tenant.UserId }, t, cancellationToken: ct));
            }
        }

        var hasPendingQuantity = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.procurement_purchase_order_items where tenant_id=@TenantId and purchase_order_id=@OrderId and received_quantity<quantity)", new { tenant.TenantId, OrderId = x.PurchaseOrderId }, t, cancellationToken: ct));
        var hasPendingQuality = rows.Values.Any(row => row.RequiresInspection);
        var receiptStatus = hasPendingQuality ? "DIVERGENT" : hasPendingQuantity ? "PARTIAL" : "RECEIVED";
        var orderStatus = hasPendingQuality ? "DIVERGENT" : hasPendingQuantity ? "PARTIALLY_RECEIVED" : "RECEIVED";
        var stockStatus = hasPendingQuality && requiresStock ? "PENDING" : requiresStock ? "COMPLETED" : "NOT_APPLICABLE";
        await c.ExecuteAsync(new CommandDefinition("update agro360.procurement_receipts set status=@ReceiptStatus,stock_integration_status=@StockStatus,finance_integration_status='COMPLETED',updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id; update agro360.procurement_purchase_orders set status=@OrderStatus,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@OrderId; insert into agro360.procurement_purchase_order_events(id,tenant_id,purchase_order_id,event_type,comment,metadata,created_by,updated_by) values(gen_random_uuid(),@TenantId,@OrderId,'RECEIPT_INTEGRATED',@Comment,jsonb_build_object('receiptId',@Id,'stockStatus',@StockStatus,'financeStatus','COMPLETED'),@UserId,@UserId)", new { tenant.TenantId, tenant.UserId, Id = id, OrderId = x.PurchaseOrderId, ReceiptStatus = receiptStatus, OrderStatus = orderStatus, StockStatus = stockStatus, Comment = hasPendingQuality ? "Entrada em quarentena; inspeção de qualidade pendente." : "Entrada física e previsão financeira concluídas." }, t, cancellationToken: ct));
        await Audit(c, t, "RECEIPT", id, "INTEGRATED", new { x.PurchaseOrderId, ReceiptStatus = receiptStatus, OrderStatus = orderStatus }, ct);
        return id;
    }, ct);
    public Task DecideReceiptQualityAsync(Guid id, ReceiptQualityCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length is < 5 or > 1000)
            throw new DomainException("Informe uma justificativa de qualidade entre 5 e 1000 caracteres.", "agro360.procurement_quality_reason_required");
        var receipt = await c.QuerySingleOrDefaultAsync<ReceiptControlRow>(new CommandDefinition(
            "select id,number,purchase_order_id PurchaseOrderId,warehouse_id WarehouseId,status from agro360.procurement_receipts where tenant_id=@TenantId and id=@Id and deleted_at is null for update",
            new { tenant.TenantId, Id = id }, t, cancellationToken: ct)) ?? throw new NotFoundException("Recebimento", id);
        if (receipt.Status == "CANCELLED") throw new ConflictException("Recebimento cancelado não aceita decisão de qualidade.", "procurement_receipt_cancelled");
        var items = (await c.QueryAsync<InspectionRow>(new CommandDefinition(
            """
            select ri.id ReceiptItemId,ri.quantity,ri.supplier_lot SupplierLot,ri.expires_on ExpiresOn,
                   oi.unit_price UnitCost,c.item_type ItemType,c.related_product_id ProductId
            from agro360.procurement_receipt_items ri
            join agro360.procurement_purchase_order_items oi on oi.tenant_id=ri.tenant_id and oi.id=ri.purchase_order_item_id
            join agro360.procurement_item_catalog c on c.tenant_id=oi.tenant_id and c.id=oi.catalog_item_id
            where ri.tenant_id=@TenantId and ri.receipt_id=@Id and ri.quality_status='PENDING'
            order by ri.id for update of ri
            """, new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).ToArray();
        if (items.Length == 0) throw new ConflictException("O recebimento não possui inspeção pendente.", "procurement_quality_not_pending");

        foreach (var item in items)
        {
            if (command.Approve && item.ItemType != "SERVICE")
            {
                if (receipt.WarehouseId is null || item.ProductId is null) throw new ConflictException("A quarentena não possui vínculo de estoque válido.", "procurement_quarantine_stock_invalid");
                var movementId = await c.ExecuteScalarAsync<Guid>(new CommandDefinition(
                    "select agro360.inventory_apply_stock_movement(@TenantId,@WarehouseId,@ProductId,@Quantity,@UnitCost,'PURCHASE_RECEIPT',@ReferenceId,@Lot,@ExpiresOn,@UserId,@Reason)",
                    new { tenant.TenantId, receipt.WarehouseId, item.ProductId, item.Quantity, item.UnitCost, ReferenceId = item.ReceiptItemId, Lot = item.SupplierLot, item.ExpiresOn, tenant.UserId, Reason = $"Liberação de qualidade do recebimento {receipt.Number}" }, t, cancellationToken: ct));
                await c.ExecuteAsync(new CommandDefinition("insert into agro360.procurement_receipt_stock_links(tenant_id,receipt_item_id,stock_movement_id,created_by) values(@TenantId,@ReceiptItemId,@MovementId,@UserId)", new { tenant.TenantId, item.ReceiptItemId, MovementId = movementId, tenant.UserId }, t, cancellationToken: ct));
            }
        }

        var qualityStatus = command.Approve ? "APPROVED" : "REJECTED";
        var quarantineStatus = command.Approve ? "RELEASED" : "REJECTED";
        var routing = command.Approve ? "RELEASED_TO_STOCK" : "SUPPLIER_RETURN";
        var itemIds = items.Select(item => item.ReceiptItemId).ToArray();
        await c.ExecuteAsync(new CommandDefinition(
            """
            update agro360.procurement_receipt_items set quality_status=@QualityStatus,notes=concat_ws(E'\n',notes,@Reason),updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=any(@ItemIds);
            update agro360.procurement_receipt_quarantines set status=@QuarantineStatus,reason=@Reason,decided_at=now(),decided_by=@UserId where tenant_id=@TenantId and receipt_item_id=any(@ItemIds) and status='PENDING';
            update agro360.procurement_receipt_divergences set status='RESOLVED',reason=@Reason,routing=@Routing,resolved_at=now(),resolved_by=@UserId where tenant_id=@TenantId and receipt_id=@Id and receipt_item_id=any(@ItemIds) and kind='QUALITY_INSPECTION' and status='OPEN';
            """, new { tenant.TenantId, Id = id, ItemIds = itemIds, QualityStatus = qualityStatus, QuarantineStatus = quarantineStatus, Routing = routing, Reason = command.Reason.Trim(), tenant.UserId }, t, cancellationToken: ct));

        var pendingQuantity = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select exists(select 1 from agro360.procurement_purchase_order_items where tenant_id=@TenantId and purchase_order_id=@OrderId and received_quantity<quantity)", new { tenant.TenantId, OrderId = receipt.PurchaseOrderId }, t, cancellationToken: ct));
        var receiptStatus = command.Approve ? pendingQuantity ? "PARTIAL" : "RECEIVED" : "DIVERGENT";
        var orderStatus = command.Approve ? pendingQuantity ? "PARTIALLY_RECEIVED" : "RECEIVED" : "DIVERGENT";
        await c.ExecuteAsync(new CommandDefinition(
            "update agro360.procurement_receipts set status=@ReceiptStatus,stock_integration_status=case when @Approve then 'COMPLETED' else 'PENDING' end,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id; update agro360.procurement_purchase_orders set status=@OrderStatus,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@OrderId",
            new { tenant.TenantId, Id = id, OrderId = receipt.PurchaseOrderId, ReceiptStatus = receiptStatus, OrderStatus = orderStatus, command.Approve, tenant.UserId }, t, cancellationToken: ct));
        await Audit(c, t, "RECEIPT", id, command.Approve ? "QUALITY_RELEASED" : "QUALITY_REJECTED", new { Reason = command.Reason.Trim(), ReceiptStatus = receiptStatus }, ct);
    }, ct);
    public Task CancelReceiptAsync(Guid id, CancelReceiptCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        if (string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length is < 5 or > 1000)
            throw new DomainException("Informe o motivo do cancelamento entre 5 e 1000 caracteres.", "agro360.procurement_cancel_reason_required");
        var receipt = await c.QuerySingleOrDefaultAsync<ReceiptControlRow>(new CommandDefinition(
            "select id,number,purchase_order_id PurchaseOrderId,warehouse_id WarehouseId,status from agro360.procurement_receipts where tenant_id=@TenantId and id=@Id and deleted_at is null for update",
            new { tenant.TenantId, Id = id }, t, cancellationToken: ct)) ?? throw new NotFoundException("Recebimento", id);
        if (receipt.Status == "CANCELLED") throw new ConflictException("Recebimento já cancelado.", "procurement_receipt_already_cancelled");
        var hasOtherReceipt = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from agro360.procurement_receipts where tenant_id=@TenantId and purchase_order_id=@OrderId and id<>@Id and status<>'CANCELLED' and deleted_at is null)",
            new { tenant.TenantId, OrderId = receipt.PurchaseOrderId, Id = id }, t, cancellationToken: ct));
        if (!hasOtherReceipt)
        {
            var changedFinancialEffect = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from agro360.procurement_order_financial_links link join agro360.finance_payables payable on payable.tenant_id=link.tenant_id and payable.id=link.payable_id where link.tenant_id=@TenantId and link.purchase_order_id=@OrderId and (payable.status<>'OPEN' or payable.balance<>payable.final_amount))",
                new { tenant.TenantId, OrderId = receipt.PurchaseOrderId }, t, cancellationToken: ct));
            if (changedFinancialEffect) throw new ConflictException("Há parcela paga, alterada ou conciliada; estorne-a no financeiro antes de cancelar o recebimento.", "procurement_receipt_finance_already_processed");
        }

        var movements = (await c.QueryAsync<CancellationMovementRow>(new CommandDefinition(
            """
            select movement.warehouse_id WarehouseId,movement.product_id ProductId,movement.quantity,movement.unit_cost UnitCost,
                   movement.lot_number LotNumber,movement.expires_on ExpiresOn
            from agro360.procurement_receipt_stock_links link
            join agro360.inventory_stock_movements movement on movement.tenant_id=link.tenant_id and movement.id=link.stock_movement_id
            join agro360.procurement_receipt_items item on item.tenant_id=link.tenant_id and item.id=link.receipt_item_id
            where link.tenant_id=@TenantId and item.receipt_id=@Id order by movement.id for update of movement
            """, new { tenant.TenantId, Id = id }, t, cancellationToken: ct))).ToArray();
        foreach (var movement in movements)
            await c.ExecuteScalarAsync<Guid>(new CommandDefinition(
                "select agro360.inventory_apply_stock_movement(@TenantId,@WarehouseId,@ProductId,-@Quantity,@UnitCost,'ADJUST',@ReferenceId,@Lot,@ExpiresOn,@UserId,@Reason)",
                new { tenant.TenantId, movement.WarehouseId, movement.ProductId, movement.Quantity, movement.UnitCost, ReferenceId = id, Lot = movement.LotNumber, movement.ExpiresOn, tenant.UserId, Reason = $"Estorno do recebimento {receipt.Number}: {command.Reason.Trim()}" }, t, cancellationToken: ct));

        await c.ExecuteAsync(new CommandDefinition(
            """
            update agro360.finance_payables payable set status='CANCELLED',cancel_reason=@Reason,updated_at=now(),updated_by=@UserId
            where @CancelForecast and payable.tenant_id=@TenantId and payable.status='OPEN' and exists(
                select 1 from agro360.procurement_order_financial_links link where link.tenant_id=payable.tenant_id and link.payable_id=payable.id and link.purchase_order_id=@OrderId);
            update agro360.procurement_purchase_order_items order_item
            set received_quantity=greatest(0,order_item.received_quantity-received.quantity),updated_at=now(),updated_by=@UserId
            from (select purchase_order_item_id,sum(quantity) quantity from agro360.procurement_receipt_items where tenant_id=@TenantId and receipt_id=@Id group by purchase_order_item_id) received
            where order_item.tenant_id=@TenantId and order_item.id=received.purchase_order_item_id;
            update agro360.procurement_receipt_quarantines set status='CANCELLED',reason=@Reason,decided_at=now(),decided_by=@UserId where tenant_id=@TenantId and receipt_item_id in(select id from agro360.procurement_receipt_items where tenant_id=@TenantId and receipt_id=@Id) and status='PENDING';
            update agro360.procurement_receipt_divergences set status='CANCELLED',reason=@Reason,routing='CANCELLED',resolved_at=now(),resolved_by=@UserId where tenant_id=@TenantId and receipt_id=@Id and status='OPEN';
            update agro360.procurement_receipts set status='CANCELLED',stock_integration_status='NOT_APPLICABLE',finance_integration_status='NOT_APPLICABLE',cancelled_at=now(),cancelled_by=@UserId,cancellation_reason=@Reason,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id;
            update agro360.procurement_purchase_orders set status=case when exists(select 1 from agro360.procurement_purchase_order_items where tenant_id=@TenantId and purchase_order_id=@OrderId and received_quantity>0) then 'PARTIALLY_RECEIVED' else 'APPROVED' end,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@OrderId;
            """, new { tenant.TenantId, Id = id, OrderId = receipt.PurchaseOrderId, CancelForecast = !hasOtherReceipt, Reason = command.Reason.Trim(), tenant.UserId }, t, cancellationToken: ct));
        await Audit(c, t, "RECEIPT", id, "CANCELLED", new { Reason = command.Reason.Trim(), ReversedMovements = movements.Length }, ct);
    }, ct);
    public Task<IReadOnlyList<dynamic>> ReceiptsAsync(ProcurementQuery q, CancellationToken ct) => List("select r.id,r.number,o.number order_number,s.legal_name supplier_name,r.received_at,r.status,r.stock_integration_status,r.finance_integration_status,r.invoice_document,(select count(*) from agro360.procurement_receipt_items i where i.tenant_id=r.tenant_id and i.receipt_id=r.id) item_count,exists(select 1 from agro360.procurement_receipt_items i where i.tenant_id=r.tenant_id and i.receipt_id=r.id and i.quality_status='PENDING') quality_pending from agro360.procurement_receipts r join agro360.procurement_purchase_orders o on o.tenant_id=r.tenant_id and o.id=r.purchase_order_id join agro360.procurement_suppliers s on s.tenant_id=o.tenant_id and s.id=o.supplier_id where r.tenant_id=@TenantId and r.deleted_at is null and (@Search is null or r.number ilike '%'||@Search||'%' or o.number ilike '%'||@Search||'%' or s.legal_name ilike '%'||@Search||'%') and (@Status is null or r.status=@Status) order by r.received_at desc limit @Take offset @Skip", q, ct);
    public Task<IReadOnlyList<dynamic>> PendingOrderItemsAsync(Guid orderId, CancellationToken ct) => db.InTenantTransactionAsync<IReadOnlyList<dynamic>>(async (c, t) => (await c.QueryAsync(new CommandDefinition("select oi.id,c.name,oi.unit,oi.quantity,oi.received_quantity,oi.quantity-oi.received_quantity pending_quantity,c.requires_lot,c.requires_expiry,c.requires_inspection,c.item_type,c.related_product_id from agro360.procurement_purchase_order_items oi join agro360.procurement_item_catalog c on c.tenant_id=oi.tenant_id and c.id=oi.catalog_item_id where oi.tenant_id=@TenantId and oi.purchase_order_id=@OrderId and oi.received_quantity<oi.quantity order by c.name", new { tenant.TenantId, OrderId = orderId }, t, cancellationToken: ct))).AsList(), ct);
    public Task<dynamic> ReceiptOptionsAsync(CancellationToken ct) => db.InTenantTransactionAsync<dynamic>(async (c, t) =>
    {
        using var grid = await c.QueryMultipleAsync(new CommandDefinition("select id,code,name from agro360.inventory_warehouses where tenant_id=@TenantId and deleted_at is null order by name; select id,code,name,type from agro360.finance_chart_of_accounts where tenant_id=@TenantId and active and type in('EXPENSE','COST','LIABILITY') order by code,name; select id,sku,name,base_unit from agro360.inventory_products where tenant_id=@TenantId and deleted_at is null order by name", new { tenant.TenantId }, t, cancellationToken: ct));
        var warehouses = (await grid.ReadAsync()).AsList();
        var financeAccounts = (await grid.ReadAsync()).AsList();
        var products = (await grid.ReadAsync()).AsList();
        return new { warehouses, financeAccounts, products };
    }, ct);
    public async Task<byte[]> ExportAsync(string report, ProcurementQuery q, CancellationToken ct) { IReadOnlyList<dynamic> rows = report switch { "suppliers" => await SuppliersAsync(q, ct), "requisitions" => await RequisitionsAsync(q, ct), "orders" => await OrdersAsync(q, ct), _ => throw new DomainException("Relatório inválido.", "agro360.procurement_report_invalid") }; var sb = new StringBuilder("sep=;\n"); if (report == "suppliers") { sb.AppendLine("Razão social;Fantasia;Categoria;Status;E-mail"); foreach (var r in rows) sb.AppendLine(CultureInfo.InvariantCulture, $"{Csv(r.legal_name)};{Csv(r.trade_name)};{Csv(r.main_category)};{Csv(r.status)};{Csv(r.email)}"); } else if (report == "requisitions") { sb.AppendLine("Número;Prioridade;Necessidade;Status;Itens"); foreach (var r in rows) sb.AppendLine(CultureInfo.InvariantCulture, $"{r.number};{r.priority};{r.needed_on:yyyy-MM-dd};{r.status};{r.item_count}"); } else { sb.AppendLine("Número;Fornecedor;Entrega;Total;Status"); foreach (var r in rows) sb.AppendLine(CultureInfo.InvariantCulture, $"{r.number};{Csv(r.supplier_name)};{r.delivery_on:yyyy-MM-dd};{r.total};{r.status}"); } return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray(); }
    private Task<T> Tx<T>(Func<Npgsql.NpgsqlConnection, Npgsql.NpgsqlTransaction, Task<T>> action) => db.InTenantTransactionAsync(action, CancellationToken.None);
    private Task Tx(Func<Npgsql.NpgsqlConnection, Npgsql.NpgsqlTransaction, Task> action) => db.InTenantTransactionAsync(action, CancellationToken.None);
    private Task<IReadOnlyList<dynamic>> List(string sql, ProcurementQuery q, CancellationToken ct) => db.InTenantTransactionAsync<IReadOnlyList<dynamic>>(async (c, t) => (await c.QueryAsync(new CommandDefinition(sql, new { tenant.TenantId, Search = string.IsNullOrWhiteSpace(q.Search) ? null : q.Search, Status = string.IsNullOrWhiteSpace(q.Status) ? null : q.Status, Category = string.IsNullOrWhiteSpace(q.Category) ? null : q.Category, Take = Math.Clamp(q.PageSize, 1, 100), Skip = (Math.Max(q.Page, 1) - 1) * Math.Clamp(q.PageSize, 1, 100) }, t, cancellationToken: ct))).AsList(), ct);
    private async Task<bool> Exists(Npgsql.NpgsqlConnection c, Npgsql.NpgsqlTransaction t, string table, Guid id, CancellationToken ct) => await c.ExecuteScalarAsync<bool>(new CommandDefinition($"select exists(select 1 from agro360.procurement_{table} where tenant_id=@TenantId and id=@Id and deleted_at is null)", new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
    private static async Task<string> Number(Npgsql.NpgsqlConnection c, Npgsql.NpgsqlTransaction t, string prefix, CancellationToken ct) { var n = await c.ExecuteScalarAsync<long>(new CommandDefinition("select nextval('agro360.procurement_document_number_seq')", transaction: t, cancellationToken: ct)); return $"{prefix}-{DateTime.UtcNow:yyyy}-{n:000000}"; }
    private async Task Audit(Npgsql.NpgsqlConnection c, Npgsql.NpgsqlTransaction t, string entity, Guid id, string action, object data, CancellationToken ct) => await c.ExecuteAsync(new CommandDefinition("insert into agro360.procurement_audit_events(id,tenant_id,entity_type,entity_id,action,changed_fields,created_by,updated_by) values(gen_random_uuid(),@TenantId,@Entity,@Id,@Action,@Data::jsonb,@UserId,@UserId)", new { tenant.TenantId, tenant.UserId, Entity = entity, Id = id, Action = action, Data = System.Text.Json.JsonSerializer.Serialize(data) }, t, cancellationToken: ct));
    private static string Csv(object? value) => $"\"{value?.ToString()?.Replace("\"", "\"\"")}\"";
    private sealed class ReceiptOrderRow { public decimal Total { get; init; } public Guid? CostCenterId { get; init; } public string SupplierName { get; init; } = string.Empty; }
    private sealed class ReceiptItemRow { public Guid Id { get; init; } public decimal Quantity { get; init; } public decimal ReceivedQuantity { get; init; } public decimal UnitPrice { get; init; } public string Unit { get; init; } = string.Empty; public string? ProductUnit { get; init; } public string Name { get; init; } = string.Empty; public bool RequiresLot { get; init; } public bool RequiresExpiry { get; init; } public bool RequiresInspection { get; init; } public string ItemType { get; init; } = string.Empty; public Guid? RelatedProductId { get; init; } }
    private sealed class ExistingReceipt { public Guid Id { get; init; } public string? PayloadHash { get; init; } }
    private sealed class ReceiptControlRow { public Guid Id { get; init; } public string Number { get; init; } = string.Empty; public Guid PurchaseOrderId { get; init; } public Guid? WarehouseId { get; init; } public string Status { get; init; } = string.Empty; }
    private sealed class InspectionRow { public Guid ReceiptItemId { get; init; } public decimal Quantity { get; init; } public decimal UnitCost { get; init; } public string ItemType { get; init; } = string.Empty; public Guid? ProductId { get; init; } public string? SupplierLot { get; init; } public DateOnly? ExpiresOn { get; init; } }
    private sealed class CancellationMovementRow { public Guid WarehouseId { get; init; } public Guid ProductId { get; init; } public decimal Quantity { get; init; } public decimal UnitCost { get; init; } public string? LotNumber { get; init; } public DateOnly? ExpiresOn { get; init; } }
}
