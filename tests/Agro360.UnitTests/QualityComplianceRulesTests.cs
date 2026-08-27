using Agro360.Domain.Compliance;

namespace Agro360.UnitTests;

public sealed class QualityComplianceRulesTests
{
    [Fact] public void Parameter_limits_include_configured_tolerance() => Assert.True(QualityComplianceRules.IsWithinLimits(9.8m, 10m, 12m, .2m));
    [Fact] public void Invalid_range_is_rejected() => Assert.Throws<ArgumentException>(() => QualityComplianceRules.IsWithinLimits(10m, 12m, 10m));
    [Fact] public void Required_parameter_without_result_blocks_completion() => Assert.Throws<InvalidOperationException>(() => QualityComplianceRules.EnsureInspectionCanComplete([new(true, false, false, false)], "APPROVE", null));
    [Fact] public void Critical_failure_blocks_approval() => Assert.Throws<InvalidOperationException>(() => QualityComplianceRules.EnsureInspectionCanComplete([new(true, true, true, false)], "APPROVE", null));
    [Theory, InlineData("BLOCKED"), InlineData("QUARANTINE"), InlineData("REJECTED")]
    public void Held_lot_cannot_be_sold(string status) => Assert.Throws<InvalidOperationException>(() => QualityComplianceRules.EnsureLotCanBeUsed(status, LotOperation.Sale));
    [Fact] public void Non_conformity_with_open_action_cannot_close() => Assert.Throws<InvalidOperationException>(() => QualityComplianceRules.EnsureNonConformityCanClose("Falha de processo", true, null, [true, false]));
    [Fact] public void Audit_without_checklist_cannot_close() => Assert.Throws<InvalidOperationException>(() => QualityComplianceRules.EnsureAuditCanClose(true, 0, false, null));
}
