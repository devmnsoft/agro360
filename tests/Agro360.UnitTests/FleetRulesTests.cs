using Agro360.Domain.Fleet;

namespace Agro360.UnitTests;

public sealed class FleetRulesTests
{
 [Fact] public void Asset_requires_code_and_allowed_status(){Assert.Throws<ArgumentException>(()=>FleetRules.ValidateAsset("","AVAILABLE",0,0));Assert.Throws<ArgumentException>(()=>FleetRules.ValidateAsset("TR-1","UNKNOWN",0,0));}
 [Fact] public void Meter_cannot_decrease_without_justification_and_permission(){Assert.Throws<InvalidOperationException>(()=>FleetRules.ValidateMeterChange(100,90,"ajuste",false));Assert.Throws<InvalidOperationException>(()=>FleetRules.ValidateMeterChange(100,90,null,true));FleetRules.ValidateMeterChange(100,90,"troca auditada",true);}
 [Fact] public void Work_order_completion_and_cancellation_require_explanation(){Assert.Throws<InvalidOperationException>(()=>FleetRules.ValidateWorkOrderTransition("COMPLETED",null,null));Assert.Throws<InvalidOperationException>(()=>FleetRules.ValidateWorkOrderTransition("CANCELLED",null,null));FleetRules.ValidateWorkOrderTransition("COMPLETED","Reparo e teste concluídos",null);}
 [Theory][InlineData(0,1)][InlineData(-1,1)] public void Refueling_rejects_invalid_quantity(decimal quantity,decimal price)=>Assert.Throws<ArgumentException>(()=>FleetRules.RefuelingTotal(quantity,price));
 [Fact] public void Refueling_calculates_traceable_total()=>Assert.Equal(57.38m,FleetRules.RefuelingTotal(12.5m,4.590m));
 [Theory][InlineData(1440,0,100)][InlineData(1440,360,75)][InlineData(0,0,0)] public void Availability_is_calculated(int period,int stopped,decimal expected)=>Assert.Equal(expected,FleetRules.Availability(period,stopped));
}
