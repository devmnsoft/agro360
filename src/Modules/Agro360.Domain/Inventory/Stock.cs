using Agro360.SharedKernel;

namespace Agro360.Domain.Inventory;

public enum StockMovementType
{
    Receipt = 1,
    Consumption = 2,
    TransferIn = 3,
    TransferOut = 4,
    AdjustmentIn = 5,
    AdjustmentOut = 6,
    Production = 7,
    Sale = 8
}

public sealed record StockBelowMinimum(Guid ProductId, Guid WarehouseId, decimal Balance, decimal Minimum) : DomainEvent;

public sealed class StockBalance : TenantEntity
{
    private StockBalance()
    {
    }

    private StockBalance(
        Guid id,
        Guid tenantId,
        Guid warehouseId,
        Guid productId,
        string unit,
        decimal minimum)
        : base(id, tenantId)
    {
        WarehouseId = Guard.Required(warehouseId, nameof(warehouseId));
        ProductId = Guard.Required(productId, nameof(productId));
        Unit = Guard.Required(unit, nameof(unit), 16).ToLowerInvariant();
        Minimum = Guard.NonNegative(minimum, nameof(minimum));
    }

    public Guid WarehouseId { get; private set; }

    public Guid ProductId { get; private set; }

    public string Unit { get; private set; } = string.Empty;

    public decimal Available { get; private set; }

    public decimal Reserved { get; private set; }

    public decimal Minimum { get; private set; }

    public static StockBalance Create(
        Guid tenantId,
        Guid warehouseId,
        Guid productId,
        string unit,
        decimal minimum = 0) =>
        new(Guid.CreateVersion7(), tenantId, warehouseId, productId, unit, minimum);

    public void Receive(decimal quantity)
    {
        Available += Guard.Positive(quantity, nameof(quantity));
        Touch();
    }

    public void Consume(decimal quantity)
    {
        var requested = Guard.Positive(quantity, nameof(quantity));
        if (Available - Reserved < requested)
        {
            throw new ConflictException("Saldo disponível insuficiente; estoque negativo não é permitido.", "inventory.insufficient_stock");
        }

        Available -= requested;
        Touch();

        if (Available < Minimum)
        {
            Raise(new StockBelowMinimum(ProductId, WarehouseId, Available, Minimum));
        }
    }

    public void Reserve(decimal quantity)
    {
        var requested = Guard.Positive(quantity, nameof(quantity));
        if (Available - Reserved < requested)
        {
            throw new ConflictException("Saldo disponível insuficiente para reserva.", "inventory.insufficient_available_stock");
        }

        Reserved += requested;
        Touch();
    }
}
