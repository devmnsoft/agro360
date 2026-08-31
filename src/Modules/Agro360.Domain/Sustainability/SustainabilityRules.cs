namespace Agro360.Domain.Sustainability;

public static class SustainabilityRules
{
    public static void ValidateAreas(decimal total, decimal productive, decimal preservation, decimal app, decimal legalReserve)
    {
        if (total <= 0) throw new ArgumentOutOfRangeException(nameof(total), "A área total deve ser positiva.");
        if (productive < 0 || preservation < 0 || app < 0 || legalReserve < 0)
            throw new ArgumentOutOfRangeException(nameof(preservation), "Áreas não podem ser negativas.");
        if (productive > total) throw new ArgumentException("A área produtiva não pode superar a área total.", nameof(productive));
    }

    public static decimal? EstimateEmission(decimal quantity, decimal? factor)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "A quantidade deve ser positiva.");
        if (factor is null) return null;
        if (factor <= 0) throw new ArgumentOutOfRangeException(nameof(factor), "O fator informado deve ser positivo.");
        return decimal.Round(quantity * factor.Value, 6, MidpointRounding.AwayFromZero);
    }

    public static void ValidateIndicator(string source, string unit, decimal? target, decimal? value)
    {
        if (string.IsNullOrWhiteSpace(source)) throw new ArgumentException("A fonte de dados é obrigatória.", nameof(source));
        if (unit.Equals("PERCENT", StringComparison.OrdinalIgnoreCase) &&
            (target is < 0 or > 100 || value is < 0 or > 100))
            throw new ArgumentOutOfRangeException(nameof(value), "Percentuais devem estar entre 0 e 100.");
    }

    public static void ValidateLot(Guid? farmId, string status, string? justification, bool farmBlocked, bool canOverride)
    {
        if (farmId is null && status == "COMPLIANT") throw new InvalidOperationException("Lote sem origem não pode ser conforme.");
        if (status == "RELEASED_WITH_RESERVATION" && string.IsNullOrWhiteSpace(justification))
            throw new ArgumentException("Liberação com ressalva exige justificativa.", nameof(justification));
        if (farmBlocked && !(canOverride && !string.IsNullOrWhiteSpace(justification)))
            throw new InvalidOperationException("A propriedade de origem está bloqueada.");
    }

    public static void ValidateActionPlan(string priority, Guid? responsibleId, string status, string? result, string? cancellationReason)
    {
        if (priority == "CRITICAL" && responsibleId is null) throw new ArgumentException("Ação crítica exige responsável.");
        if (status == "COMPLETED" && string.IsNullOrWhiteSpace(result)) throw new ArgumentException("Conclusão exige comentário.");
        if (status == "CANCELLED" && string.IsNullOrWhiteSpace(cancellationReason)) throw new ArgumentException("Cancelamento exige motivo.");
    }
}
