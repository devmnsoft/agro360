using Agro360.Domain.Commercial;
using Agro360.Domain.Support;

namespace Agro360.UnitTests;

public sealed class Sprint47CommercialRulesTests
{
    [Fact] public void Backend_calculates_proposal_total() => Assert.Equal(1250m, SaasCommercialRules.ProposalTotal(1000m, 300m, 50m, 100m));
    [Fact] public void Discount_cannot_make_total_negative() => Assert.Throws<ArgumentException>(() => SaasCommercialRules.ProposalTotal(100m, 0m, 0m, 101m));
    [Fact] public void Negative_discount_is_rejected() => Assert.Throws<ArgumentOutOfRangeException>(() => SaasCommercialRules.ProposalTotal(100m, 0m, 0m, -1m));
    [Fact] public void Lost_opportunity_requires_reason() => Assert.Throws<ArgumentException>(() => SaasCommercialRules.ValidateOpportunityTransition("LOST", null));
    [Fact] public void Won_opportunity_is_valid() => SaasCommercialRules.ValidateOpportunityTransition("WON", null);
    [Fact] public void Proposal_without_plan_is_blocked() => Assert.Throws<ArgumentException>(() => SaasCommercialRules.ValidateProposalForSending(null, Guid.NewGuid(), null));
    [Fact] public void Proposal_without_customer_or_lead_is_blocked() => Assert.Throws<ArgumentException>(() => SaasCommercialRules.ValidateProposalForSending(null, null, Guid.NewGuid()));
    [Fact] public void Expired_proposal_cannot_be_accepted() => Assert.Throws<InvalidOperationException>(() => SaasCommercialRules.ValidateProposalAcceptance(new(2026, 8, 30), new(2026, 8, 31)));
    [Fact] public void Critical_ticket_requires_details() => Assert.Throws<ArgumentException>(() => SaasCommercialRules.ValidateCriticalTicket("CRITICAL", "Financeiro", "Falha"));
    [Theory]
    [InlineData(100, 0, 0, 10, 100)]
    [InlineData(20, 2, 1, 5, 0)]
    [InlineData(50, 1, 0, null, 55)]
    public void Health_uses_transparent_inputs(decimal adoption, int critical, int overdue, int? nps, int expected) => Assert.Equal(expected, SaasCommercialRules.HealthScore(adoption, critical, overdue, nps));
    [Fact] public void Go_live_with_pending_step_is_blocked() => Assert.Throws<InvalidOperationException>(() => SupportRules.ValidatePhaseCompletion("GO_LIVE", false, true, null));
}
