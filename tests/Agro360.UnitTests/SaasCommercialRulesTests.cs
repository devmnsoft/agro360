using Agro360.Domain.Tenancy;
using Xunit;

namespace Agro360.UnitTests;

public sealed class SaasCommercialRulesTests
{
    [Fact]
    public void BillingTotalUsesDecimalAndRejectsDiscountAboveCharge()
    {
        Assert.Equal(112.35m, SaasGovernanceRules.CalculateChargeTotal(100.10m, 20.25m, 8m));
        Assert.Throws<ArgumentException>(() => SaasGovernanceRules.CalculateChargeTotal(10m, 0m, 10.01m));
    }

    [Fact]
    public void ManualPaymentAndCancellationRequireEvidence()
    {
        Assert.Throws<InvalidOperationException>(() => SaasGovernanceRules.EnsureChargeCanChange("PAID", 50m, null, "recebido"));
        Assert.Throws<InvalidOperationException>(() => SaasGovernanceRules.EnsureChargeCanChange("CANCELLED", 50m, null, " "));
    }

    [Theory]
    [InlineData(100, 0, 40, 40, 60, 0, "PARTIALLY_PAID")]
    [InlineData(100, 40, 60, 60, 0, 0, "PAID")]
    [InlineData(100, 80, 30, 20, 0, 10, "PAID")]
    public void PaymentAllocationPreservesPartialPaymentsAndTurnsExcessIntoCredit(
        decimal charge, decimal paid, decimal received, decimal applied, decimal outstanding, decimal credit, string status)
    {
        var allocation = SaasGovernanceRules.AllocatePayment(charge, paid, received);
        Assert.Equal(applied, allocation.AppliedAmount);
        Assert.Equal(outstanding, allocation.OutstandingAmount);
        Assert.Equal(credit, allocation.CreditAmount);
        Assert.Equal(status, allocation.ChargeStatus);
    }

    [Fact]
    public void OnboardingProgressComesOnlyFromRequiredRealSteps()
    {
        var steps = new[] { new OnboardingStepState(true, true), new OnboardingStepState(true, false), new OnboardingStepState(false, true) };
        Assert.Equal(50m, SaasGovernanceRules.CalculateOnboardingProgress(steps));
        Assert.Throws<InvalidOperationException>(() => SaasGovernanceRules.EnsureOnboardingCanComplete(steps));
    }

    [Fact]
    public void ModuleDependenciesAreValidatedByBackendRule()
    {
        Assert.Throws<InvalidOperationException>(() => SaasGovernanceRules.EnsureModuleCanBeEnabled("finance", ["platform-base"], ["properties"], "Contrato aprovado"));
        SaasGovernanceRules.EnsureModuleCanBeEnabled("finance", ["platform-base", "properties"], ["properties"], "Contrato aprovado");
    }

    [Theory]
    [InlineData("=1+1", "\"'=1+1\"")]
    [InlineData("+cmd", "\"'+cmd\"")]
    [InlineData("normal", "\"normal\"")]
    [InlineData("a\"b", "\"a\"\"b\"")]
    public void CsvCellsCannotStartFormulas(string input, string expected) =>
        Assert.Equal(expected, SaasGovernanceRules.ProtectCsvCell(input));
}
