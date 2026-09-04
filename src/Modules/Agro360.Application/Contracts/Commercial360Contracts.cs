using System.ComponentModel.DataAnnotations;

namespace Agro360.Application.Contracts;

public sealed record CommercialPage<T>(IReadOnlyList<T> Items,int Page,int PageSize,int Total);
public sealed record CommercialRecord(Guid Id,string Name,string Status,string? Detail,decimal Amount,DateTimeOffset UpdatedAt);
public sealed record CommercialLookup(Guid Id,string Label,string? Status=null);
public sealed record CustomerCommand([Required,MaxLength(180)]string Name,[Required]Guid SegmentId,[MaxLength(14),RegularExpression("^[0-9]{11,14}$")]string? TaxDocument,[EmailAddress] string? Email,[Phone]string? Phone,[Required]string Type="CUSTOMER",Guid? RepresentativeId=null,[MaxLength(2000)]string? Notes=null);
public sealed record OpportunityCommand([Required]Guid CustomerId,[Required,MaxLength(180)]string Name,[Range(0.01,double.MaxValue)]decimal EstimatedValue,[Required]string Stage,[Range(0,100)]int Probability,Guid? ProductId,Guid? RepresentativeId,DateOnly? ExpectedClose,string? Source,string? NextAction,string? LossReason);
public sealed record ActivityCommand([Required]Guid CustomerId,[Required]string Type,[Required]DateTimeOffset ScheduledAt,[Required]string Status,Guid? RepresentativeId,string? Channel,string? Result,string? NextAction,string? Notes,string? CancellationReason);
public sealed record OrderItemCommand([Required]Guid ProductId,Guid? LotId,[Range(0.000001,double.MaxValue)]decimal Quantity,[Required]string Unit,[Range(0.000001,double.MaxValue)]decimal UnitPrice,[Range(0,100)]decimal DiscountPercentage,[Range(0,100)]decimal MaximumDiscount);
public sealed record SalesOrderCommand([Required]Guid CustomerId,Guid? RepresentativeId,Guid? PropertyId,Guid? ContractId,[Required,MinLength(1)]IReadOnlyList<OrderItemCommand> Items,string? PaymentTerms,DateOnly? ExpectedDelivery,[Range(0,double.MaxValue)]decimal Freight,string? Notes);
public sealed record CommissionCommand([Required]Guid OrderId,[Required]Guid RuleId,[Required]Guid RepresentativeId,[Range(0,double.MaxValue)]decimal Basis,[Range(0,100)]decimal? Percentage,[Range(0,double.MaxValue)]decimal? FixedValue);
public sealed record SplitParticipantCommand([Required]Guid ParticipantId,[Required]string ParticipantType,[Range(0,100)]decimal? Percentage,[Range(0,double.MaxValue)]decimal? FixedValue,[Range(0,int.MaxValue)]int Priority);
public sealed record SplitAgreementCommand([Required,MaxLength(180)]string Name,Guid? OrderId,Guid? ContractId,[Required,MinLength(1)]IReadOnlyList<SplitParticipantCommand> Participants,string? ReleaseRule);
public sealed record StatusCommand([Required]string Status,[MaxLength(1000)]string? Reason);
public sealed record CommercialDashboard(int ActiveCustomers,int BlockedCustomers,int ActiveContracts,decimal PipelineValue,decimal ForecastRevenue,decimal ExpectedCommissions,decimal PaidCommissions,decimal PendingSplits,IReadOnlyList<CommercialRecord> Opportunities,IReadOnlyList<CommercialRecord> Orders);

public interface ICommercial360Service
{
 Task<CommercialPage<CommercialRecord>> ListAsync(string resource,string? search,string? status,int page,int pageSize,CancellationToken ct);
 Task<IReadOnlyList<CommercialLookup>> LookupAsync(string resource,string? search,CancellationToken ct);
 Task<Guid> SaveCustomerAsync(Guid? id,CustomerCommand command,CancellationToken ct);
 Task<Guid> SaveOpportunityAsync(Guid? id,OpportunityCommand command,CancellationToken ct);
 Task<Guid> SaveActivityAsync(Guid? id,ActivityCommand command,CancellationToken ct);
 Task<Guid> CreateOrderAsync(SalesOrderCommand command,CancellationToken ct);
 Task ChangeOrderStatusAsync(Guid id,StatusCommand command,bool mayOverrideBlock,CancellationToken ct);
 Task<Guid> CalculateCommissionAsync(CommissionCommand command,CancellationToken ct);
 Task ChangeCommissionStatusAsync(Guid id,StatusCommand command,CancellationToken ct);
 Task<Guid> SaveSplitAsync(SplitAgreementCommand command,CancellationToken ct);
 Task ChangeSplitStatusAsync(Guid id,StatusCommand command,CancellationToken ct);
 Task<CommercialDashboard> DashboardAsync(CancellationToken ct);
}
