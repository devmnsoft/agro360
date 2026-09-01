namespace Agro360.Application.Contracts;

public sealed record ProcurementSupplierCommand(string LegalName,string? TradeName,string? TaxDocument,string? StateRegistration,string Type,string Category,string? Email,string? Phone,string? Address,string? City,string? State,string Country,string? MainContact,string? PaymentTerms,int AverageDeliveryDays,string Status,string? RejectionReason,string? Notes,string[] Tags);
public sealed record HomologationCommand(DateOnly ValidUntil,bool Fiscal,bool Sanitary,bool Environmental,bool Certifications,bool DeliveryCapacity,bool QualityHistory,bool Commercial,bool Compliance,bool OperationalRisk,string? Reason);
public sealed record CatalogItemCommand(string Name,string Code,string Category,string Unit,string Type,string? Description,bool Active,decimal? MinimumStock,Guid? CostCenterId,bool RequiresLot,bool RequiresExpiry,bool RequiresDocument,bool RequiresInspection,bool RequiresApprovedSupplier,string? Notes);
public sealed record RequisitionItemCommand(Guid CatalogItemId,decimal Quantity,string Unit,string? Notes);
public sealed record RequisitionCommand(Guid? CostCenterId,Guid? PropertyId,string Origin,string Justification,string Priority,DateOnly NeededOn,IReadOnlyList<RequisitionItemCommand> Items);
public sealed record PurchaseOrderLineCommand(Guid CatalogItemId,decimal Quantity,string Unit,decimal UnitPrice,decimal Discount);
public sealed record PurchaseOrderCommand(Guid SupplierId,Guid? RequisitionId,Guid? QuotationId,Guid? CostCenterId,Guid? PropertyId,string PaymentTerms,DateOnly DeliveryOn,string DeliveryAddress,decimal Freight,decimal Taxes,IReadOnlyList<PurchaseOrderLineCommand> Items);
public sealed record ReceiptLineCommand(Guid PurchaseOrderItemId,decimal Quantity,string? SupplierLot,DateOnly? ExpiresOn,string? Notes);
public sealed record ProcurementReceiptCommand(Guid PurchaseOrderId,DateTimeOffset ReceivedAt,string? InvoiceDocument,bool OverrideExcess,string? ExcessJustification,IReadOnlyList<ReceiptLineCommand> Items);
public sealed record ProcurementQuery(string? Search=null,string? Status=null,string? Category=null,DateOnly? From=null,DateOnly? To=null,int Page=1,int PageSize=30);
public interface IProcurementService
{
 Task<dynamic> DashboardAsync(CancellationToken ct); Task<IReadOnlyList<dynamic>> SuppliersAsync(ProcurementQuery q,CancellationToken ct); Task<Guid> SaveSupplierAsync(Guid? id,ProcurementSupplierCommand x,CancellationToken ct); Task HomologateAsync(Guid supplierId,bool approve,HomologationCommand x,CancellationToken ct);
 Task<IReadOnlyList<dynamic>> CatalogAsync(ProcurementQuery q,CancellationToken ct); Task<Guid> SaveCatalogItemAsync(Guid? id,CatalogItemCommand x,CancellationToken ct); Task<Guid> CreateRequisitionAsync(RequisitionCommand x,CancellationToken ct); Task<IReadOnlyList<dynamic>> RequisitionsAsync(ProcurementQuery q,CancellationToken ct);
 Task<Guid> CreateOrderAsync(PurchaseOrderCommand x,CancellationToken ct); Task<IReadOnlyList<dynamic>> OrdersAsync(ProcurementQuery q,CancellationToken ct); Task ApproveOrderAsync(Guid id,string? comment,CancellationToken ct); Task<Guid> ReceiveAsync(ProcurementReceiptCommand x,CancellationToken ct); Task<byte[]> ExportAsync(string report,ProcurementQuery q,CancellationToken ct);
}
public interface IProcurementStockGateway { Task RegisterPurchaseReceiptAsync(Guid receiptId,CancellationToken ct); }
public interface IProcurementFinanceGateway { Task RegisterPayableForecastAsync(Guid purchaseOrderId,CancellationToken ct); }
public interface IProcurementDocumentGateway { Task RequireEvidenceAsync(string entity,Guid entityId,string requirement,CancellationToken ct); }
public interface IProcurementQualityGateway { Task RequestInspectionAsync(Guid receiptItemId,CancellationToken ct); }
