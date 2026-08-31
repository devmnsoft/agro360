using Agro360.SharedKernel;

namespace Agro360.Domain.Mobile;

public enum FieldPriority { Low, Medium, High, Critical }
public enum FieldRunStatus { Draft, InProgress, Completed, Pending, Rejected, Cancelled, SyncPending, SyncConflict }

public sealed record FieldChecklistItem(
    string Text, bool Required, bool EvidenceRequired, bool ObservationRequired,
    string ResponseType, string? Answer = null, Guid? EvidenceId = null, string? Observation = null);

/// <summary>Pure business rules shared by the online API and the offline materializer.</summary>
public static class FieldMobileRules
{
    public static void ValidateChecklist(string name, IReadOnlyCollection<FieldChecklistItem> items, bool active)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("O nome do checklist é obrigatório.", "field.checklist.name_required");
        if (active && items.Count == 0)
            throw new DomainException("Checklist ativo deve possuir ao menos um item.", "field.checklist.items_required");
        if (items.Any(item => string.IsNullOrWhiteSpace(item.Text)))
            throw new DomainException("Item de checklist não pode estar vazio.", "field.checklist.empty_item");
    }

    public static void ValidateCompletion(
        IReadOnlyCollection<FieldChecklistItem> items, bool signatureRequired, Guid? signatureId,
        bool locationRequired, decimal? latitude, decimal? longitude)
    {
        var missing = items.FirstOrDefault(item => item.Required &&
            string.IsNullOrWhiteSpace(item.Answer) && item.EvidenceId is null);
        if (missing is not null)
            throw new DomainException($"O item obrigatório '{missing.Text}' não foi respondido.", "field.checklist.answer_required");
        var noEvidence = items.FirstOrDefault(item => item.EvidenceRequired && item.EvidenceId is null);
        if (noEvidence is not null)
            throw new DomainException($"O item '{noEvidence.Text}' exige evidência.", "field.checklist.evidence_required");
        var noObservation = items.FirstOrDefault(item => item.ObservationRequired &&
            IsNonConforming(item.Answer) && string.IsNullOrWhiteSpace(item.Observation));
        if (noObservation is not null)
            throw new DomainException($"O item não conforme '{noObservation.Text}' exige observação.", "field.checklist.observation_required");
        if (signatureRequired && signatureId is null)
            throw new DomainException("A assinatura gerencial é obrigatória.", "field.signature.required");
        if (locationRequired && (latitude is null || longitude is null))
            throw new DomainException("A localização autorizada é obrigatória.", "field.location.required");
        MobileRules.ValidateLocation(latitude, longitude, null);
    }

    public static void EnsureNewVersion(bool approved, int currentVersion, int requestedVersion)
    {
        if (approved && requestedVersion <= currentVersion)
            throw new DomainException("Versão aprovada é imutável; crie uma nova versão.", "field.checklist.version_required");
    }

    public static void ValidateOccurrence(FieldPriority priority, Guid? responsibleId, string? transition, string? reason)
    {
        if (priority == FieldPriority.Critical && responsibleId is null)
            throw new DomainException("Ocorrência crítica exige responsável.", "field.occurrence.responsible_required");
        if (transition is "RESOLVED" && string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Resolução exige comentário.", "field.occurrence.resolution_comment_required");
        if (transition is "CANCELLED" && string.IsNullOrWhiteSpace(reason))
            throw new DomainException("Cancelamento exige motivo.", "field.cancellation.reason_required");
    }

    public static void ValidateConflictResolution(string? comment, Guid resolvedBy, DateTimeOffset resolvedAt)
    {
        if (string.IsNullOrWhiteSpace(comment) || resolvedBy == Guid.Empty || resolvedAt == default)
            throw new DomainException("Resolver conflito exige usuário, data e comentário.", "field.sync.resolution_required");
    }

    public static bool SignatureWasAltered(string signedHash, string currentHash) =>
        !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(signedHash.ToUpperInvariant()),
            System.Text.Encoding.UTF8.GetBytes(currentHash.ToUpperInvariant()));

    private static bool IsNonConforming(string? value) =>
        value is not null && (value.Equals("NO", StringComparison.OrdinalIgnoreCase) ||
                              value.Equals("NON_CONFORMING", StringComparison.OrdinalIgnoreCase));
}
