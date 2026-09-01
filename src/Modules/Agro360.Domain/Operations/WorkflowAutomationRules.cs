using System.Text.Json;
using System.Text.RegularExpressions;
using Agro360.SharedKernel;

namespace Agro360.Domain.Operations;

/// <summary>Invariantes puras do motor de processos. Persistência e autorização continuam nas bordas.</summary>
public static partial class WorkflowAutomationRules
{
    public static readonly string[] StepTypes = ["TASK", "APPROVAL", "REVIEW", "CHECK", "NOTIFICATION", "EVIDENCE", "MANAGER_SIGNATURE", "INTEGRATION", "VALIDATION", "CLOSURE"];
    public static readonly string[] AutomationTriggers = ["RECORD_CREATED", "STATUS_CHANGED", "DEADLINE_EXPIRED", "DOCUMENT_EXPIRED", "STOCK_BELOW_MINIMUM", "BILLING_OVERDUE", "SLA_EXPIRED", "LOT_BLOCKED", "APPROVAL_PENDING", "TASK_COMPLETED"];
    public static readonly string[] AutomationActions = ["CREATE_TASK", "CREATE_ALERT", "REQUEST_APPROVAL", "SEND_INTERNAL_NOTIFICATION", "REGISTER_EVENT", "BLOCK_ACTION", "ESCALATE_MANAGER", "ENQUEUE_OUTBOX"];

    public static void ValidateActivation(int stepCount)
    {
        if (stepCount <= 0) throw new DomainException("Workflow sem etapa não pode ser ativado.", "workflow.steps_required");
    }

    public static void ValidateDefinitionChange(bool active)
    {
        if (active) throw new DomainException("Workflow ativo é imutável; crie uma nova versão.", "workflow.version_required");
    }

    public static void ValidateStep(string type, int order, Guid? responsibleId, string? responsibleRole, bool evidenceRequired, string? evidenceKey, bool commentRequired, string? comment)
    {
        var normalized = type?.Trim().ToUpperInvariant();
        if (order < 1) throw new DomainException("Ordem da etapa deve ser positiva.", "workflow.step_order_invalid");
        if (!StepTypes.Contains(normalized)) throw new DomainException("Tipo de etapa inválido.", "workflow.step_type_invalid");
        if (normalized == "APPROVAL" && responsibleId is null && string.IsNullOrWhiteSpace(responsibleRole)) throw new DomainException("Aprovação exige responsável ou perfil.", "workflow.approver_required");
        if (evidenceRequired && string.IsNullOrWhiteSpace(evidenceKey)) throw new DomainException("A evidência obrigatória não foi anexada.", "workflow.evidence_required");
        if (commentRequired && string.IsNullOrWhiteSpace(comment)) throw new DomainException("Comentário obrigatório não informado.", "workflow.comment_required");
    }

    public static void ValidateAutomation(string trigger, string action, string conditionJson, bool active)
    {
        if (!AutomationTriggers.Contains(trigger?.Trim().ToUpperInvariant())) throw new DomainException("Gatilho de automação inválido.", "automation.trigger_invalid");
        if (!AutomationActions.Contains(action?.Trim().ToUpperInvariant())) throw new DomainException("Ação de automação inválida.", "automation.action_invalid");
        if (SqlToken().IsMatch(conditionJson ?? string.Empty)) throw new DomainException("Condições não aceitam SQL.", "automation.sql_forbidden");
        try { using var json = JsonDocument.Parse(conditionJson); if (json.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException(); }
        catch (JsonException) { throw new DomainException("Condição JSON inválida.", "automation.condition_invalid"); }
        if (active && conditionJson.Trim() == "{}") throw new DomainException("Automação ativa exige condição configurada.", "automation.condition_required");
    }

    public static void ValidateTemplate(string channel, string subject, string body, IEnumerable<string> allowedVariables)
    {
        if (string.IsNullOrWhiteSpace(body) || (!channel.Equals("INTERNAL", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(subject)))
            throw new DomainException("Assunto e corpo são obrigatórios para o canal.", "notification.template_content_required");
        if (HtmlRisk().IsMatch(body)) throw new DomainException("HTML inseguro não é permitido.", "notification.unsafe_html");
        var allowed = allowedVariables.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknown = TemplateVariable().Matches(body).Select(x => x.Groups[1].Value).FirstOrDefault(x => !allowed.Contains(x));
        if (unknown is not null) throw new DomainException($"Variável não permitida: {unknown}.", "notification.variable_invalid");
    }

    [GeneratedRegex(@"(?i)(;|--|/\*|\b(select|insert|update|delete|drop|alter|execute|copy)\b)")] private static partial Regex SqlToken();
    [GeneratedRegex(@"(?i)<\s*(script|iframe|object)|\bon\w+\s*=|javascript:")] private static partial Regex HtmlRisk();
    [GeneratedRegex(@"\{\{\s*([a-zA-Z][a-zA-Z0-9_.]*)\s*\}\}")] private static partial Regex TemplateVariable();
}
