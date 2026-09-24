namespace Agro360.Application.Contracts;

public sealed record StorageStructureCommand(string Code, string Name, string Type, string Location, decimal TotalCapacity, string Unit, Guid? AllowedProductId, string Status, Guid? PropertyId, string? Responsible, string? Notes, bool AllowOverflow = false);
public sealed record ReceiptCommandV9(string Number, string EntryType, Guid ProductId, Guid? SeasonId, Guid? PlotId, Guid? OriginPropertyId, string? Supplier, string? Carrier, string? Driver, string? Vehicle, string? Plate, Guid DestinationStructureId, string? UnloadingLocation, string? Notes);
public sealed record WeighReceiptCommand(decimal GrossWeight, decimal Tare);
public sealed record ClassificationCommand(decimal Moisture, decimal Impurity, decimal Damaged, decimal Burnt, decimal Broken, decimal? Green, decimal? HectoliterWeight, decimal? Protein, decimal? Acidity, decimal Temperature, string Report, string? Notes);
public sealed record QualityParameterCommand(Guid ProductId, string Name, decimal? WarningValue, decimal? RejectValue, decimal DiscountPercent, bool Active);
public sealed record TransferLotCommand(Guid DestinationStructureId, decimal Quantity, string? Notes, bool AllowOverflow = false);
public sealed record StorageReasonCommand(string Reason);
public sealed record ProcessingOrderCommand(Guid InputLotId, Guid OutputProductId, decimal InputQuantity, string Process, decimal Cost, string? Responsible, string? Notes);
public sealed record CompleteProcessingCommand(decimal OutputQuantity, decimal TechnicalLoss, string? Justification);
public sealed record ShipmentCommand(string Number, Guid? ContractId, string Customer, Guid ProductId, Guid LotId, decimal RequestedQuantity, string Destination, string? Carrier, string? Driver, string? Vehicle, string? Plate);
public sealed record LoadShipmentCommand(decimal LoadedQuantity, decimal GrossWeight, decimal Tare);
public sealed record TripCommand(string Number, Guid? ShipmentId, string Origin, string Destination, decimal EstimatedDistance, string? Carrier, string? Driver, string? Vehicle, string FreightType, decimal FreightValue, decimal Tonnes, string Status, string TransportMode = "ROAD");
public sealed record TripOccurrenceCommand(string Description);
public sealed record TripStopCommand(int Sequence, string Type, string Name, string? OperationalWindow, DateTimeOffset? PlannedArrival, DateTimeOffset? PlannedDeparture);
public sealed record TripLegCommand(int Sequence, int OriginStopSequence, int DestinationStopSequence, string Mode, Guid? AssetId, decimal? CapacityTotal, string? CapacityUnit, string? NavigationSource, DateTimeOffset? NavigationValidUntil, Guid? NavigationResponsibleId);
public sealed record TripAllocationCommand(Guid ShipmentItemId, decimal Quantity, string Unit, int LoadingStopSequence, int UnloadingStopSequence, decimal? Weight, string? WeightUnit, decimal? Volume, string? VolumeUnit);
public sealed record PlanTripCommand(string Number, string Origin, string Destination, DateTimeOffset PlannedStart, DateTimeOffset PlannedEnd, Guid? ResponsibleId, string? Carrier, string IdempotencyKey, IReadOnlyList<TripStopCommand> Stops, IReadOnlyList<TripLegCommand> Legs, IReadOnlyList<TripAllocationCommand> Allocations);
public sealed record FulfillmentItemCommand(Guid OrderItemId, Guid StockLotId, decimal Quantity, decimal PickedQuantity, decimal CheckedQuantity, string Unit, string? DivergenceReason);
public sealed record CreateFulfillmentCommand(string Number, Guid OriginWarehouseId, Guid CustomerId, string Destination, string IdempotencyKey, IReadOnlyList<FulfillmentItemCommand> Items);
public sealed record DispatchFulfillmentCommand(long Version, string IdempotencyKey);
public sealed record PrepareFulfillmentCommand(decimal PickedQuantity, decimal CheckedQuantity, string? DivergenceReason, long Version, string IdempotencyKey);
public sealed record ReleaseReservationCommand(decimal Quantity, string Reason, long Version, string IdempotencyKey);
public sealed record CancelOrderItemCommand(decimal Quantity, string Reason, long Version, string IdempotencyKey);
public sealed record FulfillmentQueueQuery(string? Customer = null, Guid? UnitId = null, DateOnly? DueUntil = null, string? Status = null, Guid? WarehouseId = null, string? Number = null, int Page = 1, int PageSize = 20);
public sealed record FulfillmentQueuePage(IReadOnlyList<dynamic> Items, int Page, int PageSize, long Total);
public sealed record DeliveryAttemptItemCommand(Guid ShipmentItemId, decimal AcceptedQuantity, decimal RefusedQuantity, string? Reason);
public sealed record DeliveryAttemptCommand(DateTimeOffset OccurredAt, string Destination, Guid ResponsibleId, string Status, string? Reason, Guid? EvidenceDocumentId, bool EvidencePending, string? PendingNotes, string IdempotencyKey, IReadOnlyList<DeliveryAttemptItemCommand> Items);
public sealed record ReturnCommand(Guid ShipmentItemId, decimal Quantity, string Reason, string IdempotencyKey);
public sealed record ReceiveReturnCommand(decimal Quantity, string Unit, string Condition, Guid WarehouseId, string? LotNumber, Guid? EvidenceDocumentId, string? Notes, long ExpectedVersion, string IdempotencyKey);
public sealed record DecideReturnCommand(string Decision, decimal Quantity, string Reason, long ExpectedVersion, string IdempotencyKey, string? Unit = null, decimal? Cost = null);
public sealed record FulfillmentIndicators(long AwaitingPicking, long Ready, long TripsInProgress, long Late, long Partial, long Refusals, long ReturnsAwaitingQuality, long UntreatedDivergences);
public sealed record DeliveryContractCommand(string Number, string Customer, Guid ProductId, decimal ContractedQuantity, decimal ContractedPrice, string Unit, DateOnly DeliveryDeadline, string PaymentTerms, string Status, string? CancellationReason, bool AllowOverdelivery = false);
public sealed record StorageDashboard(
    decimal TotalCapacity,
    decimal OccupiedCapacity,
    decimal AvailableCapacity,
    long BlockedLots,
    long PendingReceipts,
    long UnloadedThisMonth,
    decimal TechnicalLosses,
    long PendingShipments,
    long ShipmentsThisMonth,
    long OpenContracts,
    long TripsInTransit,
    decimal FreightThisMonth,
    long QualityAlerts,
    long CapacityAlerts);

public interface IStorageService
{
    Task<IReadOnlyList<dynamic>> ListAsync(string resource, CancellationToken ct); Task<dynamic?> GetAsync(string resource, Guid id, CancellationToken ct);
    Task<Guid> SaveStructureAsync(Guid? id, StorageStructureCommand command, CancellationToken ct); Task<Guid> SaveReceiptAsync(Guid? id, ReceiptCommandV9 command, CancellationToken ct);
    Task WeighAsync(Guid id, WeighReceiptCommand command, CancellationToken ct); Task ClassifyAsync(Guid id, ClassificationCommand command, CancellationToken ct); Task ReceiptStatusAsync(Guid id, string status, string? reason, CancellationToken ct);
    Task<Guid> SaveQualityParameterAsync(Guid? id, QualityParameterCommand command, CancellationToken ct); Task TransferLotAsync(Guid id, TransferLotCommand command, CancellationToken ct); Task SetLotBlockedAsync(Guid id, bool blocked, string? reason, CancellationToken ct);
    Task<Guid> CreateProcessingAsync(ProcessingOrderCommand command, CancellationToken ct); Task ProcessingStatusAsync(Guid id, string status, CompleteProcessingCommand? completion, string? reason, CancellationToken ct);
    Task<Guid> CreateShipmentAsync(ShipmentCommand command, CancellationToken ct); Task LoadShipmentAsync(Guid id, LoadShipmentCommand command, CancellationToken ct); Task ShipmentStatusAsync(Guid id, string status, string? reason, CancellationToken ct);
    Task<StorageDashboard> DashboardAsync(CancellationToken ct);
}

public sealed record AfterSalesQuery(int Page = 1, int PageSize = 20, string? Search = null, Guid? CustomerId = null, string? Status = null, Guid? AssigneeId = null, string? Type = null, DateOnly? From = null, DateOnly? To = null);
public sealed record CreateOccurrenceCommand(Guid CustomerId, Guid OrderId, Guid ShipmentId, Guid? ShipmentItemId, Guid? StockLotId, string Type, string Description, decimal? AffectedQuantity, string? Unit, DateTimeOffset OccurredAt, Guid? AssigneeId, DateTimeOffset? DueAt, Guid? EvidenceDocumentId, bool EvidencePending, string IdempotencyKey);
public sealed record TransitionOccurrenceCommand(string Status, string Reason, long ExpectedVersion);
public sealed record ProposeSolutionCommand(string Type, string Description, bool Required, DateTimeOffset? DueAt, string IdempotencyKey);
public sealed record CommercialAdjustmentCommand(string Type, string Currency, decimal ProposedAmount, string Reason, string IdempotencyKey);
public sealed record AfterSalesPage(IReadOnlyList<dynamic> Items, int Page, int PageSize, long Total);

public interface ILogisticsService
{
    Task<IReadOnlyList<dynamic>> ListAsync(CancellationToken ct); Task<Guid> SaveAsync(Guid? id, TripCommand command, CancellationToken ct); Task AddOccurrenceAsync(Guid id, TripOccurrenceCommand command, CancellationToken ct); Task CompleteAsync(Guid id, CancellationToken ct);
    Task<FulfillmentQueuePage> FulfillmentQueueAsync(FulfillmentQueueQuery query, CancellationToken ct);
    Task<dynamic?> OrderFulfillmentDetailAsync(Guid orderId, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> EligibleLotsAsync(Guid orderItemId, CancellationToken ct);
    Task<FulfillmentIndicators> FulfillmentIndicatorsAsync(CancellationToken ct);
    Task<dynamic?> FulfillmentDetailAsync(Guid id, CancellationToken ct);
    Task<Guid> CreateFulfillmentAsync(CreateFulfillmentCommand command, CancellationToken ct);
    Task DispatchFulfillmentAsync(Guid id, DispatchFulfillmentCommand command, CancellationToken ct);
    Task PrepareFulfillmentAsync(Guid shipmentItemId, PrepareFulfillmentCommand command, CancellationToken ct);
    Task ReleaseReservationAsync(Guid reservationId, ReleaseReservationCommand command, CancellationToken ct);
    Task CancelOrderItemAsync(Guid orderItemId, CancelOrderItemCommand command, CancellationToken ct);
    Task<Guid> RecordDeliveryAttemptAsync(Guid id, DeliveryAttemptCommand command, CancellationToken ct);
    Task<Guid> RegisterReturnAsync(ReturnCommand command, CancellationToken ct);
    Task<Guid> ReceiveReturnAsync(Guid id, ReceiveReturnCommand command, CancellationToken ct);
    Task<Guid> DecideReturnAsync(Guid id, DecideReturnCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListReturnsAsync(CancellationToken ct);
    Task<dynamic?> ReturnDetailAsync(Guid id, CancellationToken ct);
    Task<AfterSalesPage> OccurrencesAsync(AfterSalesQuery query, CancellationToken ct);
    Task<dynamic?> OccurrenceAsync(Guid id, CancellationToken ct);
    Task<Guid> CreateOccurrenceAsync(CreateOccurrenceCommand command, CancellationToken ct);
    Task TransitionOccurrenceAsync(Guid id, TransitionOccurrenceCommand command, CancellationToken ct);
    Task<Guid> ProposeSolutionAsync(Guid id, ProposeSolutionCommand command, CancellationToken ct);
    Task<Guid> RegisterAdjustmentAsync(Guid id, CommercialAdjustmentCommand command, CancellationToken ct);
    Task<Guid> PlanTripAsync(PlanTripCommand command, CancellationToken ct);
    Task<dynamic?> TripDetailAsync(Guid id, CancellationToken ct);
}
public interface IDeliveryContractService { Task<IReadOnlyList<dynamic>> ListAsync(CancellationToken ct); Task<Guid> SaveAsync(Guid? id, DeliveryContractCommand command, CancellationToken ct); }
