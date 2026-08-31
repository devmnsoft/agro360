using System.ComponentModel.DataAnnotations;

namespace Agro360.Application.Contracts;

public sealed record FiscalQuery(string? Search=null,string? Status=null,string? Type=null,DateOnly? From=null,DateOnly? To=null,int Page=1,int PageSize=25);
public sealed record FiscalOperationCommand([Required,MaxLength(30)]string Code,[Required,MaxLength(160)]string Name,[Required]string Type,[Required]string Purpose,string? SuggestedCfop,bool MovesStock,bool GeneratesFinancial,bool RequiresTransport,bool RequiresDocument,bool RequiresCostCenter,bool Active,string? Notes);
public sealed record FiscalRuleCommand([Required]Guid OperationId,Guid? ProductId,Guid? CategoryId,[Required,MaxLength(2)]string OriginState,[Required,MaxLength(2)]string DestinationState,[Required,RegularExpression("^[0-9]{4}$")]string Cfop,string? Cst,string? Csosn,decimal? IcmsRate,decimal? PisRate,decimal? CofinsRate,decimal? IssRate,decimal? BaseReduction,bool Active,DateOnly ValidFrom,DateOnly? ValidUntil,string? Notes);
public sealed record FiscalInvoiceItemCommand([Required]Guid ProductId,Guid? LotId,[Required]string Unit,decimal Quantity,decimal UnitPrice,decimal Discount,decimal InformedTaxes,bool IsService);
public sealed record FiscalInstallmentCommand(DateOnly DueDate,decimal Amount);
public sealed record FiscalInvoiceCommand([Required]Guid CustomerId,[Required]Guid OperationId,Guid? SaleOrderId,Guid? ExportContractId,string? PaymentTerms,Guid? CarrierId,string? DeliveryAddress,decimal Freight,decimal Insurance,decimal OtherExpenses,string? Notes,IReadOnlyList<FiscalInvoiceItemCommand> Items,IReadOnlyList<FiscalInstallmentCommand> Installments,string? StockOverrideJustification);
public sealed record FiscalDocumentCommand([Required]string Type,[Required]string Number,[Required]string Series,string? AccessKey,string? ExternalProtocol,DateTimeOffset IssuedAt,Guid? CustomerId,Guid? SupplierId,[Required]Guid OperationId,decimal Total,string? DocumentReference,string? Notes);
public sealed record FiscalPurchaseCheckCommand([Required]Guid PurchaseOrderId,Guid? ReceiptId,[Required]Guid SupplierId,[Required]Guid FiscalDocumentId,decimal ExpectedTotal,decimal ReceivedTotal,string? Justification);

public interface IFiscalStockIntegration { Task IntegrateInvoiceAsync(Guid tenantId,Guid invoiceId,CancellationToken ct); }
public interface IFiscalFinancialIntegration { Task CreateReceivablesAsync(Guid tenantId,Guid invoiceId,IReadOnlyList<FiscalInstallmentCommand> installments,CancellationToken ct); Task CreatePayablesAsync(Guid tenantId,Guid purchaseCheckId,CancellationToken ct); }
public interface IFiscalIssuanceProvider { bool IsConfigured { get; } Task<string> RequestIssuanceAsync(Guid tenantId,Guid documentId,CancellationToken ct); }
public interface IFiscalService
{
 Task<dynamic> DashboardAsync(CancellationToken ct); Task<IReadOnlyList<dynamic>> OperationsAsync(FiscalQuery query,CancellationToken ct); Task<Guid> SaveOperationAsync(Guid? id,FiscalOperationCommand command,CancellationToken ct); Task<IReadOnlyList<dynamic>> RulesAsync(FiscalQuery query,CancellationToken ct); Task<Guid> SaveRuleAsync(Guid? id,FiscalRuleCommand command,CancellationToken ct); Task<IReadOnlyList<dynamic>> InvoicesAsync(FiscalQuery query,CancellationToken ct); Task<Guid> CreateInvoiceAsync(FiscalInvoiceCommand command,CancellationToken ct); Task ConfirmInvoiceAsync(Guid id,CancellationToken ct); Task CancelInvoiceAsync(Guid id,string reason,CancellationToken ct); Task<IReadOnlyList<dynamic>> DocumentsAsync(FiscalQuery query,CancellationToken ct); Task<Guid> CreateDocumentAsync(FiscalDocumentCommand command,CancellationToken ct); Task ChangeDocumentStatusAsync(Guid id,string status,string reason,CancellationToken ct); Task<Guid> CheckPurchaseAsync(FiscalPurchaseCheckCommand command,CancellationToken ct); Task<byte[]> ExportCsvAsync(string report,FiscalQuery query,CancellationToken ct);
}
