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
    [Theory]
    [InlineData("529.982.247-25", "52998224725")]
    [InlineData("04.252.011/0001-10", "04252011000110")]
    public void Fiscal_document_is_validated_and_normalized(string input,string expected) => Assert.Equal(expected,SaasGovernanceRules.NormalizeAndValidateDocument(input));
    [Theory]
    [InlineData("111.111.111-11")]
    [InlineData("04.252.011/0001-11")]
    public void Invalid_fiscal_document_is_rejected(string input) => Assert.Throws<ArgumentException>(()=>SaasGovernanceRules.NormalizeAndValidateDocument(input));
    [Fact] public void Charge_total_is_calculated_by_backend() => Assert.Equal(105.50m,SaasGovernanceRules.CalculateChargeTotal(100m,10m,4.50m));
    [Fact] public void Charge_rejects_discount_above_amount() => Assert.Throws<ArgumentException>(()=>SaasGovernanceRules.CalculateChargeTotal(100m,0m,101m));
    [Fact] public void Unknown_culture_falls_back_to_portuguese() => Assert.Equal("pt-BR",SaasGovernanceRules.ResolveCulture("fr-FR",["pt-BR","en-US","es-ES"]));
}
