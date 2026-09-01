using Agro360.SharedKernel;

namespace Agro360.Domain.Operations;

public static class WorkManagementRules
{
    public static readonly string[] TaskStatuses = ["OPEN", "IN_PROGRESS", "WAITING_THIRD_PARTY", "WAITING_CUSTOMER", "COMPLETED", "CANCELLED", "OVERDUE", "REOPENED"];
    public static readonly string[] Priorities = ["LOW", "MEDIUM", "HIGH", "CRITICAL"];
    public static readonly string[] WorkflowStatuses = ["OPEN", "IN_REVIEW", "APPROVED", "REJECTED", "CANCELLED"];

    public static void ValidateTask(string title, Guid responsibleId, DateTimeOffset dueAt, string priority)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new DomainException("Título é obrigatório.", "task.title_required");
        if (responsibleId == Guid.Empty) throw new DomainException("Responsável é obrigatório.", "task.responsible_required");
        if (dueAt == default) throw new DomainException("Prazo válido é obrigatório.", "task.due_required");
        if (!Priorities.Contains(priority, StringComparer.OrdinalIgnoreCase)) throw new DomainException("Prioridade inválida.", "task.priority_invalid");
    }

    public static void ValidateTaskTransition(string status, string? reason)
    {
        if (!TaskStatuses.Contains(status, StringComparer.OrdinalIgnoreCase)) throw new DomainException("Status inválido.", "task.status_invalid");
        if (status.Equals("COMPLETED", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(reason)) throw new DomainException("Descrição de conclusão é obrigatória.", "task.completion_required");
        if (status.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(reason)) throw new DomainException("Motivo do cancelamento é obrigatório.", "task.cancellation_reason_required");
        if (status.Equals("REOPENED", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(reason)) throw new DomainException("Motivo da reabertura é obrigatório.", "task.reopen_reason_required");
    }

    public static void ValidateDecision(string decision, string? comment)
    {
        if (decision is not ("APPROVED" or "REJECTED" or "CANCELLED")) throw new DomainException("Decisão inválida.", "workflow.decision_invalid");
        if ((decision is "REJECTED" or "CANCELLED") && string.IsNullOrWhiteSpace(comment)) throw new DomainException("Motivo é obrigatório.", "workflow.reason_required");
    }
}
