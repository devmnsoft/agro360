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
public sealed record TripCommand(string Number, Guid? ShipmentId, string Origin, string Destination, decimal EstimatedDistance, string? Carrier, string? Driver, string? Vehicle, string FreightType, decimal FreightValue, decimal Tonnes, string Status);
public sealed record TripOccurrenceCommand(string Description);
public sealed record FulfillmentItemCommand(Guid OrderItemId, Guid StockLotId, decimal Quantity, decimal PickedQuantity, decimal CheckedQuantity, string Unit, string? DivergenceReason);
public sealed record CreateFulfillmentCommand(string Number, Guid OriginWarehouseId, Guid CustomerId, string Destination, string IdempotencyKey, IReadOnlyList<FulfillmentItemCommand> Items);
public sealed record DispatchFulfillmentCommand(long Version, string IdempotencyKey);
public sealed record DeliveryAttemptItemCommand(Guid ShipmentItemId, decimal AcceptedQuantity, decimal RefusedQuantity, string? Reason);
public sealed record DeliveryAttemptCommand(DateTimeOffset OccurredAt, string Destination, Guid ResponsibleId, string Status, string? Reason, Guid? EvidenceDocumentId, bool EvidencePending, string? PendingNotes, string IdempotencyKey, IReadOnlyList<DeliveryAttemptItemCommand> Items);
public sealed record ReturnCommand(Guid ShipmentItemId, decimal Quantity, string Reason, string IdempotencyKey);
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
public interface ILogisticsService
{
    Task<IReadOnlyList<dynamic>> ListAsync(CancellationToken ct); Task<Guid> SaveAsync(Guid? id, TripCommand command, CancellationToken ct); Task AddOccurrenceAsync(Guid id, TripOccurrenceCommand command, CancellationToken ct); Task CompleteAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> FulfillmentQueueAsync(string? customer, Guid? unitId, DateOnly? dueUntil, string? status, CancellationToken ct);
    Task<FulfillmentIndicators> FulfillmentIndicatorsAsync(CancellationToken ct);
    Task<dynamic?> FulfillmentDetailAsync(Guid id, CancellationToken ct);
    Task<Guid> CreateFulfillmentAsync(CreateFulfillmentCommand command, CancellationToken ct);
    Task DispatchFulfillmentAsync(Guid id, DispatchFulfillmentCommand command, CancellationToken ct);
    Task<Guid> RecordDeliveryAttemptAsync(Guid id, DeliveryAttemptCommand command, CancellationToken ct);
    Task<Guid> RegisterReturnAsync(ReturnCommand command, CancellationToken ct);
}
public interface IDeliveryContractService { Task<IReadOnlyList<dynamic>> ListAsync(CancellationToken ct); Task<Guid> SaveAsync(Guid? id, DeliveryContractCommand command, CancellationToken ct); }
