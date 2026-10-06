using System.ComponentModel.DataAnnotations;

namespace Agro360.Application.Contracts;

public sealed record CommercialPage<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);
public sealed record CommercialRecord(Guid Id, string Name, string Status, string? Detail, decimal Amount, DateTimeOffset UpdatedAt, string? CustomerName = null, string? Currency = null);
public sealed record CommercialLookup(Guid Id, string Label, string? Status = null, string? Unit = null, string? Sku = null, decimal? BasePrice = null, Guid? PriceTableId = null);
public sealed record CustomerCommand([Required, MaxLength(180)] string Name, [Required] Guid SegmentId, [MaxLength(14), RegularExpression("^[0-9]{11,14}$")] string? TaxDocument, [EmailAddress] string? Email, [Phone] string? Phone, [Required] string Type = "CUSTOMER", Guid? RepresentativeId = null, [MaxLength(2000)] string? Notes = null);
public sealed record OpportunityCommand([Required] Guid CustomerId, [Required, MaxLength(180)] string Name, [Range(0.01, double.MaxValue)] decimal EstimatedValue, [Required] string Stage, [Range(0, 100)] int Probability, Guid? ProductId, Guid? RepresentativeId, DateOnly? ExpectedClose, string? Source, string? NextAction, string? LossReason);
public sealed record ActivityCommand([Required] Guid CustomerId, [Required] string Type, [Required] DateTimeOffset ScheduledAt, [Required] string Status, Guid? RepresentativeId, string? Channel, string? Result, string? NextAction, string? Notes, string? CancellationReason);
public sealed record OrderItemCommand([Required] Guid ProductId, Guid? LotId, [Range(0.000001, double.MaxValue)] decimal Quantity, [Required] string Unit, [Range(0.000001, double.MaxValue)] decimal UnitPrice, [Range(0, 100)] decimal DiscountPercentage);
public sealed record SalesOrderCommand([Required] Guid CustomerId, Guid? RepresentativeId, Guid? PropertyId, Guid? ContractId, [Required, MinLength(1)] IReadOnlyList<OrderItemCommand> Items, string? PaymentTerms, DateOnly? ExpectedDelivery, [Range(0, double.MaxValue)] decimal Freight, string? Notes);
public sealed record CommercialContractCommand([Required] Guid CustomerId, [Required] Guid ProductId, Guid? RepresentativeId, [Required] string Type, [Range(0.000001, double.MaxValue)] decimal Quantity, [Required, MaxLength(20)] string Unit, [Range(0, double.MaxValue)] decimal UnitPrice, [Required, RegularExpression("^[A-Za-z]{3}$")] string Currency, DateOnly ValidFrom, DateOnly ValidTo, [Required, MaxLength(2000)] string CommercialTerms, [MaxLength(20)] string? Incoterm, [MaxLength(2000)] string? Notes, [Required, MaxLength(120)] string IdempotencyKey);
public sealed record CommissionCommand([Required] Guid OrderId, [Required] Guid RuleId, [Required] Guid RepresentativeId, [Range(0, double.MaxValue)] decimal Basis, [Range(0, 100)] decimal? Percentage, [Range(0, double.MaxValue)] decimal? FixedValue);
public sealed record SplitParticipantCommand([Required] Guid ParticipantId, [Required] string ParticipantType, [Range(0, 100)] decimal? Percentage, [Range(0, double.MaxValue)] decimal? FixedValue, [Range(0, int.MaxValue)] int Priority);
public sealed record SplitAgreementCommand([Required, MaxLength(180)] string Name, Guid? OrderId, Guid? ContractId, [Required, MinLength(1)] IReadOnlyList<SplitParticipantCommand> Participants, string? ReleaseRule);
public sealed record StatusCommand([Required] string Status, [MaxLength(1000)] string? Reason);
public sealed record ProposalItemCommand([Required] Guid ProductId, [Required, MaxLength(20)] string Unit, [Range(0.000001, double.MaxValue)] decimal Quantity, [Range(0.000001, double.MaxValue)] decimal UnitPrice, [Range(0, 100)] decimal DiscountPercentage, Guid? PriceTableId = null);
public sealed record SalesProposalCommand([Required] Guid CustomerId, Guid? OpportunityId, Guid? RepresentativeId, [Required, RegularExpression("^[A-Za-z]{3}$")] string Currency, DateOnly ValidUntil, [Range(0, double.MaxValue)] decimal Freight, [Required, MaxLength(2000)] string PaymentTerms, [Required, MinLength(1)] IReadOnlyList<ProposalItemCommand> Items, [MaxLength(1000)] string? ChangeReason = null, long? ExpectedVersion = null);
public sealed record ProposalDecisionCommand([Required] long Version, [MaxLength(1000)] string? Reason);
public sealed record ProposalAcceptanceCommand([Required] long Version, [Required, MaxLength(40)] string EvidenceType, [Required, MaxLength(1000)] string EvidenceReference, DateTimeOffset? AcceptedAt = null);
public sealed record ProposalConversionItemCommand([Required] Guid ProposalItemId, [Range(0.000001, double.MaxValue)] decimal Quantity);
public sealed record ProposalConversionCommand([Required] long Version, [Required, MaxLength(120)] string IdempotencyKey, [Required, MinLength(1)] IReadOnlyList<ProposalConversionItemCommand> Items);
public sealed record ProposalVersionView(Guid ProposalId, string Number, string Status, long Version, Guid CustomerId, string Currency, DateOnly ValidUntil, decimal Freight, decimal ItemsTotal, decimal Total, string PaymentTerms, IReadOnlyList<ProposalItemView> Items, string? CustomerName = null, long CurrentVersion = 1, long? AcceptedVersion = null, string? ChangeReason = null, Guid? OpportunityId = null, Guid? RepresentativeId = null);
public sealed record ProposalItemView(Guid Id, Guid ProductId, string Unit, decimal Quantity, decimal UnitPrice, decimal DiscountPercentage, decimal Total, decimal ConvertedQuantity, string PricingSnapshot, string? ProductName = null);
public sealed record ProposalConversionResult(Guid OrderId, bool Existing, string Currency, decimal Total, string? OrderNumber = null);
public sealed record SalesCurrencyTotal(string Currency, decimal PipelineValue, decimal ForecastRevenue, decimal OrdersTotal);
public sealed record CommercialDashboard(int ActiveCustomers, int BlockedCustomers, int ActiveContracts, decimal PipelineValue, decimal ForecastRevenue, decimal ExpectedCommissions, decimal PaidCommissions, decimal PendingSplits, IReadOnlyList<CommercialRecord> Opportunities, IReadOnlyList<CommercialRecord> Orders, IReadOnlyList<SalesCurrencyTotal>? CurrencyTotals = null);

public sealed record SalesOrderItemDetailView(Guid Id, Guid ProductId, string ProductName, string Unit, decimal Quantity, decimal UnitPrice, decimal DiscountPercentage, decimal TotalAmount, Guid? PriceTableId = null, decimal? BaseUnitPrice = null, decimal ScheduledQuantity = 0, decimal EligibleScheduleBalance = 0);
public sealed record SalesOrderFulfillmentView(Guid ShipmentId, string ShipmentNumber, string Status, DateTimeOffset CreatedAt);
public sealed record SalesOrderEventView(string EventType, string? Details, DateTimeOffset OccurredAt);

public sealed record DeliveryScheduleItemCommand([Required] Guid OrderItemId, [Range(0.000001, double.MaxValue)] decimal Quantity, [Required, MaxLength(20)] string Unit);
public sealed record CreateDeliveryScheduleCommand([Required] DateOnly PlannedDate, [Required, MaxLength(255)] string Destination, Guid? ResponsibleId, [MaxLength(1000)] string? Notes, [Required, MaxLength(120)] string IdempotencyKey, [Required, MinLength(1)] IReadOnlyList<DeliveryScheduleItemCommand> Items);
public sealed record RescheduleItemCommand([Required] Guid ScheduleItemId, [Range(0.000001, double.MaxValue)] decimal Quantity);
public sealed record RescheduleDeliveryCommand([Required] DateOnly PlannedDate, [Required] long ExpectedVersion, [Required, MaxLength(1000)] string Reason, [Required, MaxLength(120)] string IdempotencyKey, [Required, MinLength(1)] IReadOnlyList<RescheduleItemCommand> Items, [MaxLength(255)] string? Destination = null, Guid? ResponsibleId = null, bool ReplaceResponsible = false);
public sealed record CancelDeliveryScheduleCommand([Required] long ExpectedVersion, [Required, MaxLength(1000)] string Reason, [Required, MaxLength(120)] string IdempotencyKey);
public sealed record SettleDeliveryScheduleCommand([Required] long ExpectedVersion, [Required, MaxLength(1000)] string Reason, [Required, MaxLength(120)] string IdempotencyKey);
public sealed record DeliveryScheduleItemView(Guid Id, Guid OrderItemId, Guid ProductId, string ProductName, string Unit, decimal Quantity, decimal OriginalQuantity, decimal DispatchedQuantity, decimal DeliveredQuantity, decimal PendingQuantity, decimal PreparingQuantity = 0, decimal AttendableQuantity = 0);
public sealed record DeliveryScheduleRevisionView(Guid Id, long Version, string Reason, Guid ActorId, string? ActorName, DateOnly PreviousDate, DateOnly NewDate, DateTimeOffset CreatedAt);
public sealed record DeliveryScheduleView(Guid Id, Guid OrderId, string OrderNumber, string CustomerName, string ScheduleNumber, string Destination, Guid? ResponsibleId, string? ResponsibleName, DateOnly PlannedDate, DateOnly OriginalPlannedDate, string Status, string? Notes, string? CancellationReason, long Version, DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, IReadOnlyList<DeliveryScheduleItemView> Items, IReadOnlyList<DeliveryScheduleRevisionView>? Revisions = null, IReadOnlyList<string>? AllowedActions = null, string? BlockReason = null, DateTimeOffset? SettledAt = null, Guid? SettledBy = null, string? SettlementReason = null, decimal? DispatchedTotalCost = null, bool DispatchedCostAvailable = false);
public sealed record DeliveryScheduleQuery(string? Search = null, string? Status = null, Guid? CustomerId = null, Guid? OrderId = null, Guid? ProductId = null, Guid? ResponsibleId = null, DateOnly? FromDate = null, DateOnly? ToDate = null, int Page = 1, int PageSize = 20, bool LateOnly = false);
public sealed record DeliveryScheduleIndicators(long TotalOpen, long Late, long DuePeriod, long PartiallyDelivered, long Unassigned, long Completed);
public sealed record DeliverySchedulePage(IReadOnlyList<DeliveryScheduleView> Items, int Page, int PageSize, long Total, DeliveryScheduleIndicators Indicators);

public sealed record SalesOrderDetailView(
    Guid Id,
    string OrderNumber,
    Guid CustomerId,
    string CustomerName,
    string Status,
    string Currency,
    decimal ItemsTotal,
    decimal Freight,
    decimal TotalAmount,
    string? PaymentTerms,
    DateOnly? ExpectedDelivery,
    string? Notes,
    Guid? ProposalId,
    string? ProposalNumber,
    long? ProposalVersion,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<SalesOrderItemDetailView> Items,
    IReadOnlyList<SalesOrderFulfillmentView> Fulfillments,
    IReadOnlyList<SalesOrderEventView> History,
    string NextPermittedAction,
    IReadOnlyList<DeliveryScheduleView>? Schedules = null);

public interface ICommercial360Service
{
    Task<CommercialPage<CommercialRecord>> ListAsync(string resource, string? search, string? status, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<CommercialLookup>> LookupAsync(string resource, string? search, CancellationToken ct);
    Task<Guid> SaveCustomerAsync(Guid? id, CustomerCommand command, CancellationToken ct);
    Task<Guid> SaveOpportunityAsync(Guid? id, OpportunityCommand command, CancellationToken ct);
    Task<Guid> SaveActivityAsync(Guid? id, ActivityCommand command, CancellationToken ct);
    Task<Guid> CreateOrderAsync(SalesOrderCommand command, CancellationToken ct);
    Task<SalesOrderDetailView> GetOrderAsync(Guid id, CancellationToken ct);
    Task<Guid> CreateContractAsync(CommercialContractCommand command, CancellationToken ct);
    Task ChangeContractStatusAsync(Guid id, StatusCommand command, CancellationToken ct);
    Task ChangeOrderStatusAsync(Guid id, StatusCommand command, bool mayOverrideBlock, CancellationToken ct);
    Task<Guid> CalculateCommissionAsync(CommissionCommand command, CancellationToken ct);
    Task ChangeCommissionStatusAsync(Guid id, StatusCommand command, CancellationToken ct);
    Task<Guid> SaveSplitAsync(SplitAgreementCommand command, CancellationToken ct);
    Task ChangeSplitStatusAsync(Guid id, StatusCommand command, CancellationToken ct);
    Task<CommercialDashboard> DashboardAsync(CancellationToken ct);
    Task<Guid> CreateProposalAsync(SalesProposalCommand command, CancellationToken ct);
    Task<long> ReviseProposalAsync(Guid id, SalesProposalCommand command, CancellationToken ct);
    Task<ProposalVersionView> GetProposalAsync(Guid id, long? version, CancellationToken ct);
    Task DecideProposalAsync(Guid id, ProposalDecisionCommand command, string decision, CancellationToken ct);
    Task AcceptProposalAsync(Guid id, ProposalAcceptanceCommand command, CancellationToken ct);
    Task<ProposalConversionResult> ConvertProposalAsync(Guid id, ProposalConversionCommand command, CancellationToken ct);
    Task<Guid> CreateDeliveryScheduleAsync(Guid orderId, CreateDeliveryScheduleCommand command, CancellationToken ct);
    Task RescheduleDeliveryAsync(Guid scheduleId, RescheduleDeliveryCommand command, CancellationToken ct);
    Task CancelDeliveryScheduleAsync(Guid scheduleId, CancelDeliveryScheduleCommand command, CancellationToken ct);
    Task SettleDeliveryScheduleAsync(Guid scheduleId, SettleDeliveryScheduleCommand command, CancellationToken ct);
    Task<DeliverySchedulePage> ListDeliverySchedulesAsync(DeliveryScheduleQuery query, CancellationToken ct);
    Task<DeliveryScheduleView?> GetDeliveryScheduleByIdAsync(Guid scheduleId, CancellationToken ct);
    Task<IReadOnlyList<DeliveryScheduleView>> ListSchedulesForOrderAsync(Guid orderId, CancellationToken ct);
}
