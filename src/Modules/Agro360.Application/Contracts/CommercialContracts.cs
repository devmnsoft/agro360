namespace Agro360.Application.Contracts;

public sealed record CreateSaleCommand(
    Guid FarmId,
    string ProductType,
    Guid OriginId,
    Guid? WarehouseId,
    decimal Quantity,
    string Unit,
    decimal UnitPrice,
    string Currency,
    string BuyerName,
    string? BuyerDocument,
    DateOnly DueDate,
    string? IdempotencyKey);

public sealed record SaleResult(
    Guid SaleId,
    Guid ReceivableId,
    decimal TotalAmount,
    string Currency,
    string Status,
    Guid TraceabilityNodeId);

public interface ICommercialService
{
    Task<SaleResult> CreateAndConfirmSaleAsync(CreateSaleCommand command, CancellationToken cancellationToken);
}
