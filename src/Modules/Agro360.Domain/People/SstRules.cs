namespace Agro360.Domain.People;

public static class SstRules
{
    public static int RiskLevel(int severity, int probability)
    {
        if (severity is < 1 or > 5 || probability is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(severity), "Severidade e probabilidade devem estar entre 1 e 5.");
        return severity * probability;
    }

    public static bool IsCritical(int level) => level >= 16;

    public static void ValidateEpiDelivery(string workerStatus, bool epiActive, decimal quantity, DateOnly deliveredOn, DateOnly? replaceOn)
    {
        if (workerStatus != "ACTIVE") throw new InvalidOperationException("Somente trabalhador ativo pode receber EPI.");
        if (!epiActive) throw new InvalidOperationException("O EPI precisa estar ativo.");
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantidade deve ser positiva.");
        if (replaceOn is not null && replaceOn <= deliveredOn) throw new ArgumentException("A troca prevista deve ser posterior à entrega.");
    }

    public static void ValidateIncident(string description, int severity, bool investigationRequired)
    {
        if (string.IsNullOrWhiteSpace(description)) throw new ArgumentException("Descrição é obrigatória.");
        if (severity is < 1 or > 5) throw new ArgumentOutOfRangeException(nameof(severity));
        if (severity >= 4 && !investigationRequired) throw new InvalidOperationException("Incidente grave exige investigação.");
    }

    public static void ValidateChecklist(IEnumerable<(bool Required, bool Answered, bool EvidenceRequired, bool HasEvidence)> answers)
    {
        if (answers.Any(x => x.Required && !x.Answered)) throw new InvalidOperationException("Responda todos os itens obrigatórios.");
        if (answers.Any(x => x.EvidenceRequired && !x.HasEvidence)) throw new InvalidOperationException("Anexe as evidências obrigatórias.");
    }

    public static void ValidateInvestigationClosure(string? rootCause, bool hasOpenMandatoryAction, bool hasEvidence, string? justification)
    {
        if (string.IsNullOrWhiteSpace(rootCause)) throw new InvalidOperationException("Informe a causa raiz.");
        if (hasOpenMandatoryAction) throw new InvalidOperationException("Conclua as ações obrigatórias.");
        if (!hasEvidence && string.IsNullOrWhiteSpace(justification)) throw new InvalidOperationException("Inclua evidência ou justificativa.");
    }
}
