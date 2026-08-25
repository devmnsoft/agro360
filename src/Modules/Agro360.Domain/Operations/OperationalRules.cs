using Agro360.SharedKernel;

namespace Agro360.Domain.Operations;

public enum PurchaseStatus { Draft, Requested, Quoting, AwaitingApproval, Approved, Issued, PartiallyReceived, Received, Cancelled }
public enum FleetStatus { Available, InUse, InMaintenance, Stopped, Sold, Inactive }

public static class OperationalRules
{
    public static decimal PurchaseTotal(IEnumerable<(decimal Quantity, decimal UnitPrice)> items, decimal discount, decimal freight, decimal taxes)
    {
        var materialized = items.ToArray();
        if (materialized.Length == 0) throw new DomainException("A compra deve possuir itens.", "purchase.items_required");
        if (materialized.Any(x => x.Quantity <= 0)) throw new DomainException("A quantidade deve ser positiva.", "purchase.quantity_invalid");
        if (materialized.Any(x => x.UnitPrice < 0) || discount < 0 || freight < 0 || taxes < 0)
            throw new DomainException("Valores monetários não podem ser negativos.", "purchase.value_invalid");
        var total = materialized.Sum(x => x.Quantity * x.UnitPrice) - discount + freight + taxes;
        if (total < 0) throw new DomainException("O desconto excede o valor da compra.", "purchase.discount_invalid");
        return total;
    }

    public static PurchaseStatus Receive(PurchaseStatus status, decimal ordered, decimal previouslyReceived, decimal receiving)
    {
        if (status == PurchaseStatus.Cancelled) throw new DomainException("Pedido cancelado não pode ser recebido.", "purchase.cancelled");
        if (receiving <= 0 || previouslyReceived + receiving > ordered) throw new DomainException("Quantidade de recebimento inválida.", "purchase.receipt_invalid");
        return previouslyReceived + receiving == ordered ? PurchaseStatus.Received : PurchaseStatus.PartiallyReceived;
    }

    public static decimal RequireMovement(decimal current, decimal quantity, bool allowNegative = false)
    {
        if (quantity <= 0) throw new DomainException("A quantidade deve ser positiva.", "inventory.quantity_invalid");
        var result = current - quantity;
        if (result < 0 && !allowNegative) throw new DomainException("Saldo insuficiente.", "inventory.insufficient_stock");
        return result;
    }

    public static decimal MaintenanceTotal(decimal parts, decimal labor)
    {
        if (parts < 0 || labor < 0) throw new DomainException("Custos não podem ser negativos.", "maintenance.cost_invalid");
        return parts + labor;
    }

    public static decimal FuelTotal(decimal quantity, decimal unitPrice)
    {
        if (quantity <= 0) throw new DomainException("A quantidade deve ser positiva.", "fuel.quantity_invalid");
        if (unitPrice < 0) throw new DomainException("O valor não pode ser negativo.", "fuel.value_invalid");
        return quantity * unitPrice;
    }
}
