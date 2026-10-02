using Agro360.Domain.Production;
using Agro360.SharedKernel;
using Xunit;

namespace Agro360.UnitTests;

public sealed class IndustrialProductionRulesTests
{
    [Fact]
    public void ConsumptionValidatesQuantityAndUnit()
    {
        Assert.Throws<DomainException>(() =>
            IndustrialProductionRules.Consumption(0, "kg", "key-1", false, null));

        Assert.Throws<DomainException>(() =>
            IndustrialProductionRules.Consumption(10, "", "key-1", false, null));

        Assert.Throws<DomainException>(() =>
            IndustrialProductionRules.Consumption(10, "kg", "", false, null));

        Assert.Throws<DomainException>(() =>
            IndustrialProductionRules.Consumption(10, "kg", new string('x', 161), false, null));
    }

    [Fact]
    public void ConsumptionExpiredOverrideRequiresJustification()
    {
        Assert.Throws<DomainException>(() =>
            IndustrialProductionRules.Consumption(10, "kg", "key-1", true, null));

        Assert.Throws<DomainException>(() =>
            IndustrialProductionRules.Consumption(10, "kg", "key-1", true, "   "));

        // Should not throw when justification provided
        IndustrialProductionRules.Consumption(10, "kg", "key-1", true, "Laudo laboratorial autorizou");
    }

    [Fact]
    public void ReversalRequiresReasonAndValidStatus()
    {
        Assert.Throws<DomainException>(() =>
            IndustrialProductionRules.Reversal("POSTED", null));

        Assert.Throws<DomainException>(() =>
            IndustrialProductionRules.Reversal("POSTED", "  "));

        Assert.Throws<ConflictException>(() =>
            IndustrialProductionRules.Reversal("REVERSED", "Correção"));

        Assert.Throws<DomainException>(() =>
            IndustrialProductionRules.Reversal("DRAFT", "Correção"));

        IndustrialProductionRules.Reversal("POSTED", "Erro de pesagem na dosagem");
    }

    [Fact]
    public void TransitionEnforcesStateAndIntegrity()
    {
        // Cancelled order cannot transition
        Assert.Throws<DomainException>(() =>
            IndustrialProductionRules.Transition("CANCELLED", "IN_PRODUCTION", "Reabrir", true, true, true, false, true, true));

        // Cancellation requires reason
        Assert.Throws<DomainException>(() =>
            IndustrialProductionRules.Transition("PLANNED", "CANCELLED", null, true, true, true, false, true, true));

        // Release requires recipe items
        Assert.Throws<DomainException>(() =>
            IndustrialProductionRules.Transition("PLANNED", "RELEASED", null, false, false, false, false, false, false));

        // Start requires stock reserved if reservation required
        Assert.Throws<DomainException>(() =>
            IndustrialProductionRules.Transition("RELEASED", "IN_PRODUCTION", null, true, false, false, true, false, false));

        // Valid transition
        IndustrialProductionRules.Transition("RELEASED", "IN_PRODUCTION", null, true, false, true, true, false, false);
    }

    [Fact]
    public void QualityDecisionValidatesResultAndEvidence()
    {
        Assert.Throws<DomainException>(() =>
            IndustrialProductionRules.QualityDecision("INVALID", null, null, null));

        // Rejection or block requires reason
        Assert.Throws<DomainException>(() =>
            IndustrialProductionRules.QualityDecision("REJECTED", null, null, null));

        Assert.Throws<DomainException>(() =>
            IndustrialProductionRules.QualityDecision("BLOCKED", "   ", null, null));

        // Approval requires report and evidence
        Assert.Throws<DomainException>(() =>
            IndustrialProductionRules.QualityDecision("APPROVED", null, null, null));

        var decision = IndustrialProductionRules.QualityDecision("APPROVED", null, "LAUDO-2026-001", "FOTO-PAINEL-LOTE");
        Assert.Equal("APPROVED", decision);
    }
}
