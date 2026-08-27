using Agro360.Domain.Tenancy;

namespace Agro360.UnitTests;

public sealed class SaasGovernanceRulesTests
{
    [Fact] public void Tenant_status_change_requires_reason() => Assert.Throws<InvalidOperationException>(() => SaasGovernanceRules.EnsureStatusTransition("ACTIVE", "SUSPENDED", null));
    [Fact] public void Inactive_plan_cannot_be_subscribed() => Assert.Throws<InvalidOperationException>(() => SaasGovernanceRules.EnsureSubscriptionCanStart(false, new(2026, 1, 1), null, 100, 0, null));
    [Fact] public void Paid_charge_requires_payment_date() => Assert.Throws<InvalidOperationException>(() => SaasGovernanceRules.EnsureChargeCanChange("PAID", 100, null, null));
    [Fact] public void Cancelled_charge_requires_reason() => Assert.Throws<InvalidOperationException>(() => SaasGovernanceRules.EnsureChargeCanChange("CANCELLED", 100, null, null));
    [Fact] public void Usage_warns_at_eighty_percent() => Assert.True(SaasGovernanceRules.EvaluateUsage(8, 10).Warning);
    [Fact] public void Usage_blocks_at_limit_without_deleting_data() => Assert.True(SaasGovernanceRules.EvaluateUsage(10, 10).BlockNewRecords);
    [Fact] public void Valid_override_temporarily_expands_limit() => Assert.Equal(20, SaasGovernanceRules.EvaluateUsage(10, 10, 20, DateTimeOffset.UtcNow.AddDays(1)).EffectiveLimit);
    [Fact] public void Administrative_block_wins_over_plan() => Assert.False(SaasGovernanceRules.ResolveFeature(true, true, "ADMIN_BLOCK"));
    [Fact] public void Onboarding_requires_all_mandatory_steps() => Assert.Throws<InvalidOperationException>(() => SaasGovernanceRules.EnsureOnboardingCanComplete([new(true, false), new(false, false)]));
    [Fact] public void Branding_rejects_unsafe_logo() => Assert.Throws<ArgumentException>(() => SaasGovernanceRules.EnsureBranding("#174C3C", "#102A25", "#D6A84B", "logo.exe", 100));
    [Fact] public void Branding_accepts_valid_palette_and_logo() => SaasGovernanceRules.EnsureBranding("#174C3C", "#102A25", "#D6A84B", "logo.webp", 1024);
}
