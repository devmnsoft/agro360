using Agro360.SharedKernel;

namespace Agro360.Application.Contracts;

public sealed record CreateProductCommand(
    string Sku,
    string Name,
    string Category,
    string BaseUnit,
    bool RequiresLot,
    bool IsPerishable);

public sealed record ProductDto(
    Guid Id,
    string Sku,
    string Name,
    string Category,
    string BaseUnit,
    bool RequiresLot,
    bool IsPerishable);

public sealed record CreateWarehouseCommand(Guid FarmId, string Code, string Name, string Type);

public sealed record WarehouseDto(Guid Id, Guid FarmId, string Code, string Name, string Type);

public sealed record StockMovementCommand(
    Guid WarehouseId,
    Guid ProductId,
    decimal Quantity,
    string Unit,
    decimal UnitCost,
    string? LotNumber,
    DateOnly? ExpiresOn,
    string ReferenceType,
    Guid? ReferenceId,
    string? Notes,
    string? IdempotencyKey);

public sealed record StockBalanceDto(
    Guid WarehouseId,
    Guid ProductId,
    string Sku,
    string ProductName,
    string Unit,
    decimal Available,
    decimal Reserved,
    decimal Minimum,
    decimal AverageCost,
    long Version);

public sealed record StockMovementResult(Guid MovementId, decimal NewBalance, decimal AverageCost, long Version);

public interface IInventoryService
{
    Task<ProductDto> CreateProductAsync(CreateProductCommand command, CancellationToken cancellationToken);

    Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseCommand command, CancellationToken cancellationToken);

    Task<StockMovementResult> ReceiveAsync(StockMovementCommand command, CancellationToken cancellationToken);

    Task<StockMovementResult> ConsumeAsync(StockMovementCommand command, CancellationToken cancellationToken);

    Task<PagedResult<StockBalanceDto>> ListBalancesAsync(int page, int pageSize, string? search, CancellationToken cancellationToken);
}

public sealed record MaterialRequestItemCommand(Guid ProductId, decimal Quantity, string Unit);
public sealed record CreateMaterialRequestCommand(Guid FarmId, Guid? WarehouseId, Guid? CostCenterId,
    string Purpose, string? RelatedType, Guid? RelatedId, DateOnly NeededOn, string Priority,
    string? Notes, string? UrgencyReason, IReadOnlyCollection<MaterialRequestItemCommand> Items, bool Submit);
public sealed record MaterialRequestDecisionCommand(string Decision, string Reason, long Version);
public sealed record MaterialReservationCommand(Guid ItemId, Guid WarehouseId, decimal Quantity, string IdempotencyKey, long Version);
public sealed record MaterialDeliveryLotCommand(string LotNumber, decimal Quantity);
public sealed record MaterialDeliveryCommand(Guid ItemId, decimal Quantity, string Unit,
    IReadOnlyCollection<MaterialDeliveryLotCommand> Lots, string Destination, string Receiver,
    bool DirectConsumption, string IdempotencyKey, long Version);
public sealed record MaterialConsumptionCommand(Guid DeliveryId, decimal Quantity, string DestinationType,
    Guid DestinationId, Guid? CostCenterId, string IdempotencyKey);
public sealed record MaterialReturnCommand(Guid DeliveryId, decimal Quantity, string LotNumber,
    string Condition, Guid WarehouseId, string Reason, string Responsible, string IdempotencyKey);
public sealed record MaterialRequestActionCommand(string Reason, long Version);
public sealed record InventoryLookupDto(Guid Id, string Label, string? Unit = null);
public sealed record MaterialRequestListDto(Guid Id, string Number, string Requester, string Farm,
    DateOnly NeededOn, string Priority, string Status, decimal FulfillmentPercent, long Version);
public sealed record MaterialAvailabilityDto(Guid WarehouseId, string Warehouse, string? LotNumber,
    DateOnly? ExpiresOn, decimal Physical, decimal Blocked, decimal Reserved, decimal Available);
public sealed record MaterialRequestItemDto(Guid Id, Guid ProductId, string Product, decimal Requested,
    decimal Reserved, decimal Delivered, decimal Consumed, decimal Returned, string Unit,
    IReadOnlyCollection<MaterialAvailabilityDto> Availability);
public sealed record MaterialRequestEventDto(string Type, string Status, string Actor, string? Reason, DateTimeOffset OccurredAt);
public sealed record MaterialRequestDeliveryDto(Guid Id, Guid ItemId, decimal Quantity, string Unit,
    string Receiver, bool DirectConsumption, DateTimeOffset DeliveredAt);
public sealed record MaterialRequestDetailDto(Guid Id, string Number, Guid FarmId, string Farm,
    Guid? WarehouseId, string Requester, Guid? CostCenterId, string Purpose, string? RelatedType,
    Guid? RelatedId, DateOnly NeededOn, string Priority, string? Notes, string Status, long Version,
    IReadOnlyCollection<MaterialRequestItemDto> Items, IReadOnlyCollection<MaterialRequestDeliveryDto> Deliveries,
    IReadOnlyCollection<MaterialRequestEventDto> History);

public interface IMaterialRequestService
{
    Task<Guid> CreateAsync(CreateMaterialRequestCommand command, CancellationToken ct);
    Task<PagedResult<MaterialRequestListDto>> ListAsync(int page, int pageSize, string? search, string? status, CancellationToken ct);
    Task<MaterialRequestDetailDto> GetAsync(Guid id, CancellationToken ct);
    Task DecideAsync(Guid id, MaterialRequestDecisionCommand command, CancellationToken ct);
    Task ReserveAsync(Guid id, MaterialReservationCommand command, CancellationToken ct);
    Task<Guid> DeliverAsync(Guid id, MaterialDeliveryCommand command, CancellationToken ct);
    Task<Guid> ConsumeAsync(Guid id, MaterialConsumptionCommand command, CancellationToken ct);
    Task<Guid> ReturnAsync(Guid id, MaterialReturnCommand command, CancellationToken ct);
    Task CancelAsync(Guid id, MaterialRequestActionCommand command, CancellationToken ct);
    Task<IReadOnlyCollection<InventoryLookupDto>> LookupsAsync(string type, CancellationToken ct);
}
