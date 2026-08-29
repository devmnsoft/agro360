using Agro360.Domain.Production;using Agro360.SharedKernel;
namespace Agro360.UnitTests;
public sealed class IndustrialProductionRulesTests
{
 [Fact]public void Create_recipe_accepts_valid_items()=>IndustrialProductionRules.Recipe(100,92,[(Guid.NewGuid(),110,"kg")]);
 [Fact]public void Recipe_requires_positive_yield()=>Assert.Throws<DomainException>(()=>IndustrialProductionRules.Recipe(100,0,[(Guid.NewGuid(),1,"kg")]));
 [Fact]public void Order_requires_product_recipe_and_quantity()=>Assert.Throws<DomainException>(()=>IndustrialProductionRules.Order(Guid.Empty,Guid.NewGuid(),1,"kg"));
 [Fact]public void Release_without_item_is_blocked()=>Assert.Throws<DomainException>(()=>IndustrialProductionRules.Transition("PLANNED","RELEASED",null,false,false,false,false,true,true));
 [Fact]public void Start_without_required_stock_is_blocked()=>Assert.Throws<DomainException>(()=>IndustrialProductionRules.Transition("RELEASED","IN_PRODUCTION",null,true,false,false,true,true,true));
 [Fact]public void Complete_without_record_is_blocked()=>Assert.Throws<DomainException>(()=>IndustrialProductionRules.Transition("IN_PRODUCTION","COMPLETED",null,true,false,true,true,true,true));
 [Fact]public void Cancel_requires_reason()=>Assert.Throws<DomainException>(()=>IndustrialProductionRules.Transition("PLANNED","CANCELLED",null,true,false,false,false,true,true));
 [Fact]public void Critical_or_quality_pending_blocks_completion()=>Assert.Throws<DomainException>(()=>IndustrialProductionRules.Transition("IN_QUALITY","CLOSED",null,true,true,true,true,false,true));
 [Fact]public void Record_rejects_negative_quantity()=>Assert.Throws<DomainException>(()=>IndustrialProductionRules.Record(Guid.NewGuid(),-1,2,0,null));
 [Fact]public void Loss_requires_reason()=>Assert.Throws<DomainException>(()=>IndustrialProductionRules.Record(Guid.NewGuid(),1,2,1,null));
 [Fact]public void Yield_is_calculated()=>Assert.Equal(80m,IndustrialProductionRules.Yield(80,100));
 [Fact]public void Basic_unit_cost_is_calculated()=>Assert.Equal(6.5m,IndustrialProductionRules.BasicCost(100,10,15,5,20));
 [Fact]public void Valid_transition_to_production_is_allowed()=>IndustrialProductionRules.Transition("RELEASED","IN_PRODUCTION",null,true,false,true,true,true,true);
}
