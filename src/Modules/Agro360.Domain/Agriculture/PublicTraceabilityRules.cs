using Agro360.SharedKernel;

namespace Agro360.Domain.Agriculture;

public static class PublicTraceabilityRules
{
    public static void EnsurePublishable(string qualityStatus, bool hasOrigin)
    {
        if (!string.Equals(qualityStatus, "APPROVED", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Somente lote aprovado pela qualidade pode ser publicado como liberado.", "traceability.quality_not_approved");
        if (!hasOrigin)
            throw new ConflictException("O lote não possui origem rastreável suficiente para publicação.", "traceability.origin_missing");
    }

    public static string RequireRevocationReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 3)
            throw new DomainException("Informe o motivo da revogação.", "traceability.revocation_reason_required");
        return reason.Trim();
    }
}
