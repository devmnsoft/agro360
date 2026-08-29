using Agro360.Domain.People;
namespace Agro360.UnitTests;
public sealed class SstRulesTests
{
 [Theory] [InlineData(1,1,1)] [InlineData(4,4,16)] [InlineData(5,5,25)] public void Calculates_traceable_risk_level(int s,int p,int expected)=>Assert.Equal(expected,SstRules.RiskLevel(s,p));
 [Fact] public void Rejects_delivery_to_inactive_worker()=>Assert.Throws<InvalidOperationException>(()=>SstRules.ValidateEpiDelivery("INACTIVE",true,1,new(2026,8,1),new(2026,9,1)));
 [Fact] public void Rejects_invalid_delivery_quantity()=>Assert.Throws<ArgumentOutOfRangeException>(()=>SstRules.ValidateEpiDelivery("ACTIVE",true,0,new(2026,8,1),null));
 [Fact] public void Serious_incident_requires_investigation()=>Assert.Throws<InvalidOperationException>(()=>SstRules.ValidateIncident("Ocorrência",4,false));
 [Fact] public void Incomplete_checklist_is_rejected()=>Assert.Throws<InvalidOperationException>(()=>SstRules.ValidateChecklist([(true,false,false,false)]));
 [Fact] public void Investigation_with_open_action_cannot_close()=>Assert.Throws<InvalidOperationException>(()=>SstRules.ValidateInvestigationClosure("Falha de barreira",true,true,null));
 [Fact] public void Complete_investigation_can_close()=>SstRules.ValidateInvestigationClosure("Falha de barreira",false,true,null);
}
