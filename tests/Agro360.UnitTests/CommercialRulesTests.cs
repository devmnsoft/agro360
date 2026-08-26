using Agro360.Domain.Commercial;
using Agro360.SharedKernel;

namespace Agro360.UnitTests;

public sealed class CommercialRulesTests
{
 [Fact]public void Active_customer_can_order()=>CommercialRules.CustomerCanOrder("ACTIVE",false);
 [Fact]public void Inactive_customer_cannot_order()=>Assert.Throws<DomainException>(()=>CommercialRules.CustomerCanOrder("INACTIVE",false));
 [Fact]public void Blocked_customer_requires_override()=>Assert.Throws<DomainException>(()=>CommercialRules.CustomerCanOrder("BLOCKED",false));
 [Fact]public void Lost_opportunity_requires_reason()=>Assert.Throws<DomainException>(()=>CommercialRules.ValidateOpportunity("LOST",100,null));
 [Fact]public void Order_requires_items()=>Assert.Throws<DomainException>(()=>CommercialRules.ValidateOrder(Array.Empty<(decimal,decimal,decimal,decimal)>()));
 [Fact]public void Order_enforces_discount_limit()=>Assert.Throws<DomainException>(()=>CommercialRules.ValidateOrder([(1,100,11,10)]));
 [Fact]public void Order_calculates_discounted_total()=>Assert.Equal(180,CommercialRules.ValidateOrder([(2,100,10,15)]));
 [Fact]public void Percentage_commission_is_calculated()=>Assert.Equal(125,CommercialRules.Commission(2500,5,null));
 [Fact]public void Fixed_commission_is_calculated()=>Assert.Equal(90,CommercialRules.Commission(2500,null,90));
 [Fact]public void Split_above_one_hundred_is_rejected()=>Assert.Throws<DomainException>(()=>CommercialRules.ValidateSplit([(Guid.NewGuid(),60,null),(Guid.NewGuid(),41,null)]));
 [Fact]public void Valid_mixed_split_is_accepted()=>CommercialRules.ValidateSplit([(Guid.NewGuid(),70,null),(Guid.NewGuid(),null,100)]);
 [Fact]public void Duplicate_split_participant_is_rejected(){var id=Guid.NewGuid();Assert.Throws<DomainException>(()=>CommercialRules.ValidateSplit([(id,50,null),(id,50,null)]));}
}
