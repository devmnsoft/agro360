using System.ComponentModel.DataAnnotations;

namespace Agro360.Application.Contracts;

public sealed record ExportQuery(string? Search=null,string? Status=null,string? Country=null,DateOnly? From=null,DateOnly? To=null,int Page=1,int PageSize=25);
public sealed record ExportCustomerCommand([Required,MaxLength(200)]string Name,[Required,MaxLength(2)]string Country,string? City,string? TaxDocument,string? MainContact,[EmailAddress]string? Email,string? Phone,string Language,[Required,MaxLength(3)]string Currency,string? CommercialTerms,string? PreferredIncoterm,[Required]string Status,string Risk,string? RejectionReason,string? Notes,string[] Tags);
public sealed record ExportContractItemCommand([Required]Guid ProductId,Guid? LotId,[Range(typeof(decimal),"0.000001","79228162514264337593543950335")]decimal Quantity,[Required]string Unit,[Range(typeof(decimal),"0.01","79228162514264337593543950335")]decimal UnitPrice);
public sealed record ExportContractCommand([Required]Guid CustomerId,Guid? TradePartnerId,[Required,MaxLength(3)]string Currency,[Required]string Incoterm,string OriginPort,string DestinationPort,[Required,MaxLength(2)]string DestinationCountry,DateOnly ContractDate,DateOnly ExpectedShipmentDate,string PaymentTerms,string? Notes,IReadOnlyList<ExportContractItemCommand> Items);
public sealed record ExportShipmentCommand([Required]Guid ContractId,DateOnly ExpectedDate,string OriginTerminal,string Destination,string? Carrier,string? FreightAgent,string? Notes,IReadOnlyList<ExportShipmentItemCommand> Items,string? OverrideJustification);
public sealed record ExportShipmentItemCommand([Required]Guid ContractItemId,[Required]Guid LotId,[Range(typeof(decimal),"0.000001","79228162514264337593543950335")]decimal Quantity);
public interface IExportTradingService
{
 Task<dynamic> DashboardAsync(CancellationToken ct); Task<IReadOnlyList<dynamic>> CustomersAsync(ExportQuery query,CancellationToken ct); Task<Guid> SaveCustomerAsync(Guid? id,ExportCustomerCommand command,CancellationToken ct); Task<IReadOnlyList<dynamic>> ContractsAsync(ExportQuery query,CancellationToken ct); Task<Guid> CreateContractAsync(ExportContractCommand command,CancellationToken ct); Task ApproveContractAsync(Guid id,CancellationToken ct); Task CancelContractAsync(Guid id,string reason,CancellationToken ct); Task<IReadOnlyList<dynamic>> ShipmentsAsync(ExportQuery query,CancellationToken ct); Task<Guid> CreateShipmentAsync(ExportShipmentCommand command,CancellationToken ct); Task<byte[]> ExportCsvAsync(string report,ExportQuery query,CancellationToken ct);
}
