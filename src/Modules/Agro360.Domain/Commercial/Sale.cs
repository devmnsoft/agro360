using Agro360.SharedKernel;

namespace Agro360.Domain.Commercial;

public enum SaleStatus
{
    Draft = 1,
    Confirmed = 2,
    Fulfilled = 3,
    Cancelled = 4
}

public sealed record SaleConfirmed(Guid SaleId, Money Total) : DomainEvent;

public sealed class Sale : TenantEntity
{
    private Sale()
    {
    }

    private Sale(
        Guid id,
        Guid tenantId,
        Guid farmId,
        string productType,
        Guid originId,
        decimal quantity,
        string unit,
        Money unitPrice,
        DateOnly dueDate)
        : base(id, tenantId)
    {
        FarmId = Guard.Required(farmId, nameof(farmId));
        ProductType = Guard.Required(productType, nameof(productType), 40);
        OriginId = Guard.Required(originId, nameof(originId));
        Quantity = Guard.Positive(quantity, nameof(quantity));
        Unit = Guard.Required(unit, nameof(unit), 16);
        UnitPrice = unitPrice.Amount;
        Currency = unitPrice.Currency;
        TotalAmount = decimal.Round(Quantity * UnitPrice, 4);
        DueDate = dueDate;
        Status = SaleStatus.Draft;
    }

    public Guid FarmId { get; private set; }

    public string ProductType { get; private set; } = string.Empty;

    public Guid OriginId { get; private set; }

    public decimal Quantity { get; private set; }

    public string Unit { get; private set; } = string.Empty;

    public decimal UnitPrice { get; private set; }

    public decimal TotalAmount { get; private set; }

    public string Currency { get; private set; } = "BRL";

    public DateOnly DueDate { get; private set; }

    public SaleStatus Status { get; private set; }

    public static Sale Create(
        Guid tenantId,
        Guid farmId,
        string productType,
        Guid originId,
        decimal quantity,
        string unit,
        Money unitPrice,
        DateOnly dueDate) =>
        new(Guid.CreateVersion7(), tenantId, farmId, productType, originId, quantity, unit, unitPrice, dueDate);

    public void Confirm()
    {
        if (Status != SaleStatus.Draft)
        {
            throw new ConflictException("A venda não está em rascunho.", "commercial.sale_not_draft");
        }

        Status = SaleStatus.Confirmed;
        Touch();
        Raise(new SaleConfirmed(Id, new Money(TotalAmount, Currency)));
    }
}
