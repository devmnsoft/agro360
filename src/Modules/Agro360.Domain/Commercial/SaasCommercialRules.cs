namespace Agro360.Domain.Commercial;

/// <summary>Regras determinísticas da Sprint 47; totais monetários nunca são confiados ao cliente.</summary>
public static class SaasCommercialRules
{
    public static readonly string[] PipelineStatuses = ["NEW", "QUALIFYING", "QUALIFIED", "PROPOSAL_SENT", "NEGOTIATING", "WON", "LOST", "SUSPENDED", "CANCELLED"];

    public static decimal ProposalTotal(decimal monthly, decimal implementation, decimal support, decimal discount)
    {
        if (monthly < 0 || implementation < 0 || support < 0 || discount < 0) throw new ArgumentOutOfRangeException(nameof(discount), "Valores e desconto não podem ser negativos.");
        var subtotal = monthly + implementation + support;
        if (discount > subtotal) throw new ArgumentException("O desconto não pode tornar o total negativo.", nameof(discount));
        return decimal.Round(subtotal - discount, 2, MidpointRounding.AwayFromZero);
    }

    public static void ValidateOpportunityTransition(string status, string? reason)
    {
        if (!PipelineStatuses.Contains(status, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Status comercial inválido.");
        if (status.Equals("LOST", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("O motivo da perda é obrigatório.");
    }

    public static void ValidateProposalForSending(Guid? tenantId, Guid? leadId, Guid? planId)
    {
        if (tenantId is null && leadId is null) throw new ArgumentException("Informe um cliente ou lead.");
        if (planId is null) throw new ArgumentException("Informe o plano sugerido.");
    }

    public static void ValidateProposalAcceptance(DateOnly validUntil, DateOnly today)
    {
        if (validUntil < today) throw new InvalidOperationException("A proposta expirou e precisa ser revalidada antes do aceite.");
    }

    public static void ValidateCriticalTicket(string priority, string? module, string description)
    {
        if (!priority.Equals("CRITICAL", StringComparison.OrdinalIgnoreCase)) return;
        if (string.IsNullOrWhiteSpace(module) || description.Trim().Length < 80) throw new ArgumentException("Chamado crítico exige módulo e descrição detalhada de pelo menos 80 caracteres.");
    }

    public static int HealthScore(decimal adoptionPercent, int openCriticalTickets, int overdueInvoices, int? nps)
    {
        var score = (int)Math.Round(Math.Clamp(adoptionPercent, 0, 100) * 0.6m, MidpointRounding.AwayFromZero) + 40;
        score -= Math.Min(openCriticalTickets * 15, 30);
        score -= Math.Min(overdueInvoices * 20, 40);
        if (nps is < 7) score -= 10;
        return Math.Clamp(score, 0, 100);
    }
}
