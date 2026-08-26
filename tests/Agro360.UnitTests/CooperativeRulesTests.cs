using Agro360.Domain.Cooperatives;
namespace Agro360.UnitTests;
public sealed class CooperativeRulesTests
{
 [Fact]public void Collective_allocation_must_close_total(){CooperativeRules.ValidateAllocation(100,[40,60]);Assert.Throws<ArgumentException>(()=>CooperativeRules.ValidateAllocation(100,[40,50]));}
 [Fact]public void Quality_bonus_is_calculated(){Assert.Equal(75m,CooperativeRules.QualityBonus(1500,5));}
 [Theory][InlineData("ACTIVE")][InlineData("APPROVED")][InlineData("CANCELLED")][InlineData("CLOSED")]public void Contract_transitions_are_explicit(string status)=>CooperativeRules.ValidateTransition(status);
}
