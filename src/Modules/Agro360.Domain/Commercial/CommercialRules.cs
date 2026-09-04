using Agro360.SharedKernel;

namespace Agro360.Domain.Commercial;

public static class CommercialRules
{
    public static void ValidateTaxDocument(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        var digits = new string(value.Where(char.IsDigit).ToArray());
        var valid = digits.Length switch { 11 => IsValidCpf(digits), 14 => IsValidCnpj(digits), _ => false };
        if (!valid) throw new DomainException("CPF/CNPJ inválido.", "sales.invalid_tax_document");
    }

    public static void CustomerCanOrder(string status, bool mayOverrideBlock)
    {
        if (status == "INACTIVE") throw new DomainException("Reative o cliente antes de criar um pedido.", "sales.customer_inactive");
        if (status == "BLOCKED" && !mayOverrideBlock) throw new DomainException("Cliente bloqueado exige autorização superior.", "sales.customer_blocked");
    }

    public static void ValidateOpportunity(string stage, decimal value, string? lossReason)
    {
        if (value <= 0) throw new DomainException("O valor estimado deve ser positivo.");
        if (string.Equals(stage, "LOST", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(lossReason)) throw new DomainException("Informe o motivo da perda.");
    }

    public static decimal ValidateOrder(IEnumerable<(decimal Quantity, decimal Price, decimal Discount, decimal MaximumDiscount)> items)
    {
        var rows = items.ToArray();
        if (rows.Length == 0) throw new DomainException("O pedido precisa ter ao menos um item.", "sales.order_without_items");
        foreach (var item in rows)
        {
            if (item.Quantity <= 0 || item.Price <= 0) throw new DomainException("Quantidade e preço devem ser positivos.");
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

    private static bool IsValidCpf(string value)
    {
        if (value.Distinct().Count() == 1) return false;
        var first = Mod11(value, 9, 10);
        var second = Mod11(value, 10, 11);
        return value[9] - '0' == first && value[10] - '0' == second;
    }

    private static bool IsValidCnpj(string value)
    {
        if (value.Distinct().Count() == 1) return false;
        int Digit(int length, int[] weights)
        {
            var sum = 0;
            for (var i = 0; i < length; i++) sum += (value[i] - '0') * weights[i];
            var remainder = sum % 11;
            return remainder < 2 ? 0 : 11 - remainder;
        }
        return value[12] - '0' == Digit(12, [5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2])
            && value[13] - '0' == Digit(13, [6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2]);
    }

    private static int Mod11(string value, int length, int initialWeight)
    {
        var sum = 0;
        for (var i = 0; i < length; i++) sum += (value[i] - '0') * (initialWeight - i);
        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
