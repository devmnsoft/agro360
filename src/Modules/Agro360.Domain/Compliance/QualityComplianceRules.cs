namespace Agro360.Domain.Compliance;

/// <summary>Pure business rules shared by quality use cases. Regulatory limits remain tenant-configured.</summary>
public static class QualityComplianceRules
{
    private static readonly HashSet<string> HoldStatuses = new(StringComparer.OrdinalIgnoreCase)
        { "BLOCKED", "QUARANTINE", "REJECTED", "AUDIT_HOLD", "CANCELLED" };

    public static bool IsWithinLimits(decimal value, decimal? minimum, decimal? maximum, decimal tolerance = 0)
    {
        if (tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance), "A tolerância não pode ser negativa.");
        if (minimum.HasValue && maximum.HasValue && minimum > maximum)
            throw new ArgumentException("O valor mínimo deve ser menor ou igual ao máximo.");
        return (!minimum.HasValue || value >= minimum.Value - tolerance)
            && (!maximum.HasValue || value <= maximum.Value + tolerance);
    }

    public static void EnsureInspectionCanComplete(IEnumerable<InspectionParameterCheck> parameters, string decision, string? reason)
    {
        var checks = parameters.ToArray();
        if (checks.Any(x => x.Required && !x.HasResult))
            throw new InvalidOperationException("Todos os parâmetros obrigatórios devem possuir resultado.");
        if (checks.Any(x => x.Critical && x.HasResult && !x.Conforming) && decision.Equals("APPROVE", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Parâmetro crítico fora do limite impede a aprovação.");
        if (new[] { "REJECT", "BLOCK", "QUARANTINE", "APPROVE_WITH_RESTRICTION" }.Contains(decision, StringComparer.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("A decisão informada exige motivo.");
    }

    public static void EnsureLotCanBeUsed(string qualityStatus, LotOperation operation)
    {
        if (HoldStatuses.Contains(qualityStatus))
            throw new InvalidOperationException($"Lote com status {qualityStatus} não pode seguir para {operation}.");
        if (operation == LotOperation.Certificate && qualityStatus.Equals("APPROVED_WITH_RESTRICTION", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Certificado exige resolução das ressalvas do lote.");
    }

    public static void EnsureNonConformityCanClose(string? rootCause, bool hasEvidence, string? justification, IEnumerable<bool> mandatoryActionsClosed)
    {
        if (string.IsNullOrWhiteSpace(rootCause)) throw new InvalidOperationException("A causa raiz é obrigatória.");
        if (!hasEvidence && string.IsNullOrWhiteSpace(justification)) throw new InvalidOperationException("Informe evidência ou justificativa.");
        if (mandatoryActionsClosed.Any(closed => !closed)) throw new InvalidOperationException("Há ações obrigatórias em aberto.");
    }

    public static void EnsureAuditCanClose(bool hasScope, int checklistItems, bool reportRejected, string? reason)
    {
        if (!hasScope) throw new InvalidOperationException("A auditoria deve possuir escopo.");
        if (checklistItems <= 0) throw new InvalidOperationException("A auditoria deve possuir checklist.");
        if (reportRejected && string.IsNullOrWhiteSpace(reason)) throw new InvalidOperationException("A reprovação exige motivo.");
    }
}

public readonly record struct InspectionParameterCheck(bool Required, bool Critical, bool HasResult, bool Conforming);
public enum LotOperation { Sale, Shipment, Certificate }
