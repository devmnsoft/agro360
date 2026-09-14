using Agro360.SharedKernel;

namespace Agro360.Domain.Finance;

public static class SeasonCostRules
{
    public static decimal RoundMoney(decimal value) => decimal.Round(value, 4, MidpointRounding.AwayFromZero);

    public static IReadOnlyList<(decimal Percentage, decimal Amount, decimal Adjustment)> Allocate(
        decimal amount, string method, IReadOnlyList<(decimal Base, decimal? Percentage)> destinations)
    {
        if (amount <= 0 || destinations.Count == 0) throw new DomainException("Informe valor e destinos válidos.", "cost.allocation_invalid");
        var normalized = method.Trim().ToUpperInvariant();
        if (normalized is not ("PERCENTAGE" or "AREA" or "PRODUCTION" or "HOURS" or "EQUAL" or "DIRECT"))
            throw new DomainException("Método de rateio não suportado.", "cost.method_invalid");
        decimal[] weights;
        if (normalized == "PERCENTAGE")
        {
            weights = destinations.Select(x => x.Percentage ?? -1).ToArray();
            if (weights.Any(x => x < 0) || Math.Abs(weights.Sum() - 100m) > 0.0001m)
                throw new DomainException("Os percentuais devem totalizar 100%.", "cost.percentage_invalid");
        }
        else if (normalized is "EQUAL" or "DIRECT") weights = destinations.Select(_ => 1m).ToArray();
        else
        {
            weights = destinations.Select(x => x.Base).ToArray();
            if (weights.Any(x => x <= 0)) throw new DomainException("A base do método escolhido deve estar preenchida e ser maior que zero.", "cost.base_invalid");
        }
        var total = weights.Sum();
        var result = new List<(decimal, decimal, decimal)>(weights.Length);
        decimal distributed = 0;
        for (var i = 0; i < weights.Length; i++)
        {
            var percentage = decimal.Round(weights[i] / total * 100m, 6, MidpointRounding.AwayFromZero);
            var raw = amount * weights[i] / total;
            var value = i == weights.Length - 1 ? amount - distributed : RoundMoney(raw);
            var adjustment = RoundMoney(value - raw);
            distributed += value;
            result.Add((percentage, value, adjustment));
        }
        return result;
    }
}
