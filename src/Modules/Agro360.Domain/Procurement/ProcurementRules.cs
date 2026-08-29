using Agro360.SharedKernel;

namespace Agro360.Domain.Procurement;

public static class ProcurementRules
{
    public static readonly string[] SupplierStatuses = ["ACTIVE", "INACTIVE", "BLOCKED", "UNDER_REVIEW", "APPROVED", "REJECTED"];
    public static decimal OrderTotal(IEnumerable<(decimal Quantity, decimal UnitPrice, decimal Discount)> items, decimal freight, decimal taxes)
    {
        var lines = items.ToArray();
        if (lines.Length == 0) throw new DomainException("O pedido deve possuir ao menos um item.", "procurement.items_required");
        if (lines.Any(x => x.Quantity <= 0 || x.UnitPrice <= 0 || x.Discount < 0 || x.Discount > x.Quantity * x.UnitPrice))
            throw new DomainException("Quantidade, preço ou desconto inválido.", "procurement.line_invalid");
        if (freight < 0 || taxes < 0) throw new DomainException("Frete e impostos não podem ser negativos.", "procurement.cost_invalid");
        return lines.Sum(x => x.Quantity * x.UnitPrice - x.Discount) + freight + taxes;
    }

    public static void Supplier(string name, string? email, string status, string? rejectionReason)
    {
        Guard.Required(name, nameof(name), 200);
        if (!SupplierStatuses.Contains(status.ToUpperInvariant())) throw new DomainException("Status de fornecedor inválido.", "procurement.supplier_status_invalid");
        if (!string.IsNullOrWhiteSpace(email) && !System.Net.Mail.MailAddress.TryCreate(email, out _)) throw new DomainException("E-mail inválido.", "procurement.email_invalid");
        if (status.Equals("REJECTED", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(rejectionReason)) throw new DomainException("Informe o motivo da reprovação.", "procurement.rejection_reason_required");
    }

    public static void Requisition(string priority, DateOnly neededOn, int itemCount, string justification)
    {
        if (itemCount == 0) throw new DomainException("A requisição deve possuir ao menos um item.", "procurement.items_required");
        if (neededOn < DateOnly.FromDateTime(DateTime.UtcNow.Date)) throw new DomainException("A data necessária não pode estar no passado.", "procurement.needed_date_invalid");
        if (priority.Equals("URGENT", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(justification)) throw new DomainException("Compras urgentes exigem justificativa.", "procurement.urgent_reason_required");
    }

    public static void Receipt(decimal ordered, decimal previouslyReceived, decimal received, bool overrideExcess, string? justification, bool requiresLot, string? lot, bool requiresExpiry, DateOnly? expiry)
    {
        if (received <= 0) throw new DomainException("Quantidade recebida deve ser positiva.", "procurement.receipt_quantity_invalid");
        if (previouslyReceived + received > ordered && (!overrideExcess || string.IsNullOrWhiteSpace(justification))) throw new DomainException("Recebimento excedente exige permissão e justificativa.", "procurement.receipt_excess");
        if (requiresLot && string.IsNullOrWhiteSpace(lot)) throw new DomainException("Lote é obrigatório para este item.", "procurement.lot_required");
        if (requiresExpiry && expiry is null) throw new DomainException("Validade é obrigatória para este item.", "procurement.expiry_required");
    }

    public static void Decision(decimal selectedTotal, decimal lowestTotal, string? justification)
    {
        if (selectedTotal > lowestTotal && string.IsNullOrWhiteSpace(justification)) throw new DomainException("Escolha acima do menor preço exige justificativa.", "procurement.decision_reason_required");
    }
}
