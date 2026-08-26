using Agro360.SharedKernel;

namespace Agro360.Domain.Commercial;

public static class CommercialRules
{
    public static void CustomerCanOrder(string status, bool mayOverrideBlock)
    {
        if (status == "INACTIVE") throw new DomainException("Reative o cliente antes de criar um pedido.", "sales.customer_inactive");
        if (status == "BLOCKED" && !mayOverrideBlock) throw new DomainException("Cliente bloqueado exige autorização superior.", "sales.customer_blocked");
    }

    public static void ValidateOpportunity(string stage, decimal value, string? lossReason)
    {
        if (value < 0) throw new DomainException("O valor estimado não pode ser negativo.");
        if (stage == "LOST" && string.IsNullOrWhiteSpace(lossReason)) throw new DomainException("Informe o motivo da perda.");
    }

    public static decimal ValidateOrder(IEnumerable<(decimal Quantity, decimal Price, decimal Discount, decimal MaximumDiscount)> items)
    {
        var rows = items.ToArray();
        if (rows.Length == 0) throw new DomainException("O pedido precisa ter ao menos um item.", "sales.order_without_items");
        foreach (var item in rows)
        {
            if (item.Quantity <= 0 || item.Price < 0) throw new DomainException("Quantidade e preço do item são inválidos.");
            if (item.Discount is < 0 or > 100 || item.Discount > item.MaximumDiscount) throw new DomainException("Desconto acima do limite comercial.", "sales.discount_exceeded");
        }
        return decimal.Round(rows.Sum(x => x.Quantity * x.Price * (1 - x.Discount / 100)), 2);
    }

    public static decimal Commission(decimal basis, decimal? percentage, decimal? fixedValue)
    {
        if (basis < 0 || (percentage is null) == (fixedValue is null) || percentage is < 0 or > 100 || fixedValue < 0)
            throw new DomainException("Regra de comissão inválida.");
        return decimal.Round(fixedValue ?? basis * percentage!.Value / 100, 2);
    }

    public static void ValidateSplit(IEnumerable<(Guid ParticipantId, decimal? Percentage, decimal? FixedValue)> participants)
    {
        var rows = participants.ToArray();
        if (rows.Length == 0 || rows.GroupBy(x => x.ParticipantId).Any(x => x.Count() > 1)) throw new DomainException("Participantes do split devem ser únicos.");
        if (rows.Any(x => (x.Percentage is null) == (x.FixedValue is null) || x.Percentage < 0 || x.FixedValue < 0)) throw new DomainException("Use percentual ou valor fixo por participante, nunca ambos.");
        if (rows.Sum(x => x.Percentage ?? 0) > 100) throw new DomainException("A soma percentual do split não pode ultrapassar 100%.", "sales.split_over_100");
    }
}
