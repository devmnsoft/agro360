using Agro360.Domain.Finance;
using Agro360.SharedKernel;

namespace Agro360.UnitTests;

public sealed class FinanceControllershipRulesTests
{
    [Fact] public void Final_amount_rejects_negative_original() => Assert.Throws<DomainException>(() => FinanceRules.FinalAmount(-1, 0, 0, 0));
    [Fact] public void Final_amount_applies_adjustments() => Assert.Equal(105m, FinanceRules.FinalAmount(100, 5, 7, 3));
    [Fact] public void Allocation_must_total_one_hundred() => Assert.Throws<DomainException>(() => FinanceRules.Allocation([50m, 49m]));
    [Fact] public void Valid_allocation_is_accepted() => FinanceRules.Allocation([40m, 35m, 25m]);
    [Fact] public void Settlement_cannot_exceed_balance() => Assert.Throws<DomainException>(() => FinanceRules.SettlementBalance(100, 101));
    [Fact] public void Partial_settlement_returns_balance() => Assert.Equal(60m, FinanceRules.SettlementBalance(100, 40));
    [Fact] public void Profitability_handles_division_by_zero() => Assert.Equal(0m, FinanceRules.MarginPercent(0, 20, 10));
    [Fact] public void Profitability_calculates_full_margin() => Assert.Equal(70m, FinanceRules.MarginPercent(100, 20, 10));
    [Fact] public void Budget_requires_positive_value() => Assert.Throws<DomainException>(() => FinanceRules.Budget(0, new(2026,1,1), new(2026,12,31)));
    [Fact] public void Budget_requires_valid_period() => Assert.Throws<DomainException>(() => FinanceRules.Budget(10, new(2026,2,1), new(2026,1,1)));
}
