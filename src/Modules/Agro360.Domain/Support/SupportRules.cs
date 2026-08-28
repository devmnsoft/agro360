namespace Agro360.Domain.Support;

public static class SupportRules
{
    public static readonly string[] TicketStatuses = ["OPEN", "TRIAGE", "IN_PROGRESS", "WAITING_CUSTOMER", "WAITING_THIRD_PARTY", "RESOLVED", "CLOSED", "CANCELLED", "REOPENED"];
    public static readonly string[] Priorities = ["LOW", "NORMAL", "HIGH", "CRITICAL"];

    public static void ValidateTransition(string status, string? reason, string? resolution)
    {
        if (!TicketStatuses.Contains(status, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Status de chamado inválido.");
        if ((status is "CANCELLED" or "REOPENED") && string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("O motivo é obrigatório para cancelar ou reabrir o chamado.");
        if (status == "RESOLVED" && string.IsNullOrWhiteSpace(resolution)) throw new ArgumentException("A resposta de resolução é obrigatória.");
    }

    public static (DateTimeOffset FirstResponse, DateTimeOffset Resolution) CalculateSla(DateTimeOffset openedAt, int firstResponseMinutes, int resolutionMinutes)
    {
        if (firstResponseMinutes <= 0 || resolutionMinutes <= 0) throw new ArgumentOutOfRangeException(nameof(firstResponseMinutes), "Os tempos de SLA devem ser positivos.");
        return (openedAt.AddMinutes(firstResponseMinutes), openedAt.AddMinutes(resolutionMinutes));
    }

    public static void ValidateScore(int score, int min, int max)
    {
        if (score < min || score > max) throw new ArgumentOutOfRangeException(nameof(score), $"A nota deve estar entre {min} e {max}.");
    }

    public static void ValidatePhaseCompletion(string phase, bool homologationCompleted, bool checklistCompleted, string? evidence)
    {
        if (!checklistCompleted && string.IsNullOrWhiteSpace(evidence)) throw new ArgumentException("Informe evidência textual ou conclua o checklist.");
        if (phase == "GO_LIVE" && !homologationCompleted) throw new InvalidOperationException("Go-live exige homologação concluída.");
    }
}
