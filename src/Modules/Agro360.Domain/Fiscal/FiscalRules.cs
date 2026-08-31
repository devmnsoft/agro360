using Agro360.SharedKernel;

namespace Agro360.Domain.Fiscal;

public static class FiscalRules
{
    public static decimal InvoiceTotal(IEnumerable<(decimal Quantity, decimal UnitPrice, decimal Discount)> items,
        decimal freight, decimal insurance, decimal otherExpenses, decimal informedTaxes)
    {
        var rows = items.ToArray();
        if (rows.Length == 0) throw new DomainException("O faturamento deve possuir ao menos um item.", "fiscal.items_required");
        if (rows.Any(x => x.Quantity <= 0 || x.UnitPrice <= 0)) throw new DomainException("Quantidade e valor unitário devem ser positivos.", "fiscal.item_invalid");
        if (rows.Any(x => x.Discount < 0 || x.Discount > x.Quantity * x.UnitPrice)) throw new DomainException("Desconto não pode tornar o item negativo.", "fiscal.discount_invalid");
        if (freight < 0 || insurance < 0 || otherExpenses < 0 || informedTaxes < 0) throw new DomainException("Totais informados não podem ser negativos.", "fiscal.total_invalid");
        return decimal.Round(rows.Sum(x => x.Quantity * x.UnitPrice - x.Discount) + freight + insurance + otherExpenses + informedTaxes, 2);
    }

    public static void ValidatePercentage(params decimal?[] values)
    {
        if (values.Any(x => x is < 0 or > 100)) throw new DomainException("Percentuais devem estar entre 0 e 100.", "fiscal.percentage_invalid");
    }

    public static void ValidateAccessKey(string? accessKey)
    {
        if (accessKey is not null && (accessKey.Length != 44 || accessKey.Any(c => !char.IsAsciiDigit(c))))
            throw new DomainException("A chave de acesso deve conter exatamente 44 dígitos.", "fiscal.access_key_invalid");
    }
}
