using Agro360.Domain.People;
namespace Agro360.UnitTests;
public sealed class RuralHrRulesTests
{
 [Fact]public void Calculates_worked_hours_and_break()=>Assert.Equal(7.5m,RuralHrRules.WorkedHours(DateTimeOffset.Parse("2026-08-25T08:00:00Z"),DateTimeOffset.Parse("2026-08-25T16:00:00Z"),30));
 [Fact]public void Invalid_shift_is_blocked()=>Assert.Throws<ArgumentException>(()=>RuralHrRules.WorkedHours(DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddHours(-1),0));
 [Theory][InlineData("HOURLY",8,20,1,160)][InlineData("DAILY",8,150,1,150)][InlineData("PIECEWORK",8,10,12,120)][InlineData("FIXED",8,900,1,900)]public void Calculates_labor_cost(string type,decimal hours,decimal rate,decimal quantity,decimal expected)=>Assert.Equal(expected,RuralHrRules.LaborCost(hours,rate,type,quantity));
 [Fact]public void Expiration_and_transport_capacity_are_enforced(){Assert.True(RuralHrRules.IsExpired(new(2026,1,1),new(2026,8,26)));RuralHrRules.EnsureCapacity(10,10);Assert.Throws<ArgumentException>(()=>RuralHrRules.EnsureCapacity(10,11));}
}
