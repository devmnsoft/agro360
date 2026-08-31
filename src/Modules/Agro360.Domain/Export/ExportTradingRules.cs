using Agro360.SharedKernel;

namespace Agro360.Domain.Export;

public static class ExportTradingRules
{
    public static decimal ContractTotal(IEnumerable<(decimal Quantity, decimal UnitPrice)> items)
    {
        var lines = items.ToArray();
        if (lines.Length == 0) throw new DomainException("Contrato deve possuir ao menos um item.", "export.items_required");
        if (lines.Any(x => x.Quantity <= 0 || x.UnitPrice <= 0)) throw new DomainException("Quantidade e valor unitário devem ser positivos.", "export.value_invalid");
        return lines.Sum(x => decimal.Round(x.Quantity * x.UnitPrice, 2, MidpointRounding.ToEven));
    }

    public static decimal Convert(decimal value, decimal rate, DateOnly? rateDate)
    {
        if (rate <= 0) throw new DomainException("Taxa de câmbio deve ser positiva.", "export.rate_invalid");
        if (rateDate is null) throw new DomainException("Data da taxa de câmbio é obrigatória.", "export.rate_date_required");
        return decimal.Round(value * rate, 2, MidpointRounding.ToEven);
    }

    public static decimal Margin(decimal revenue, IEnumerable<decimal> costs)
    {
        var values = costs.ToArray();
        if (values.Any(x => x < 0)) throw new DomainException("Custos não podem ser negativos.", "export.cost_negative");
        return decimal.Round(revenue - values.Sum(), 2, MidpointRounding.ToEven);
    }
}
