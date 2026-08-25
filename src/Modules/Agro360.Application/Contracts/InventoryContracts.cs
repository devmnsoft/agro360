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
