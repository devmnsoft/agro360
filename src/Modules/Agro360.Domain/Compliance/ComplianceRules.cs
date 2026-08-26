namespace Agro360.Domain.Compliance;

public static class ComplianceRules
{
    public static string DocumentStatus(DateOnly expiresOn, DateOnly today, int warningDays = 30) =>
        expiresOn < today ? "EXPIRED" : expiresOn <= today.AddDays(warningDays) ? "EXPIRING" : "VALID";

    public static bool IsCertificationValid(DateOnly validUntil, DateOnly today, string status) =>
        status == "APPROVED" && validUntil >= today;

    public static bool CanTrade(IEnumerable<bool> mandatoryRequirements) => mandatoryRequirements.All(value => value);

    public static decimal AuditScore(IEnumerable<(decimal Weight, bool Compliant)> answers)
    {
        var materialized = answers.ToArray();
        var total = materialized.Sum(answer => answer.Weight);
        return total == 0 ? 0 : decimal.Round(materialized.Where(answer => answer.Compliant).Sum(answer => answer.Weight) * 100 / total, 2);
    }

    public static bool IsActionPlanLate(DateOnly dueOn, DateOnly today, string status) =>
        dueOn < today && status is not ("CLOSED" or "CANCELLED");
}

public static class SustainabilityCalculations
{
    public static decimal EsgScore(decimal environmental, decimal social, decimal governance) =>
        decimal.Round((ValidateScore(environmental) * 0.4m) + (ValidateScore(social) * 0.3m) + (ValidateScore(governance) * 0.3m), 2);

    public static decimal CarbonTonnes(decimal activityAmount, decimal emissionFactorKgCo2e, decimal sequestrationKgCo2e = 0)
    {
        if (activityAmount < 0 || emissionFactorKgCo2e < 0 || sequestrationKgCo2e < 0)
            throw new ArgumentOutOfRangeException(nameof(activityAmount), "Atividade, fator e sequestro não podem ser negativos.");
        return decimal.Round(((activityAmount * emissionFactorKgCo2e) - sequestrationKgCo2e) / 1000m, 4);
    }

    private static decimal ValidateScore(decimal score) => score is >= 0 and <= 100
        ? score
        : throw new ArgumentOutOfRangeException(nameof(score), "Indicadores ESG devem estar entre 0 e 100.");
}
