using Agro360.Domain.Compliance;
namespace Agro360.UnitTests;
public sealed class ComplianceRulesTests
{
 [Fact] public void Expired_document_is_detected()=>Assert.Equal("EXPIRED",ComplianceRules.DocumentStatus(new(2026,1,1),new(2026,1,2)));
 [Fact] public void Product_rule_blocks_lot_when_requirement_is_missing()=>Assert.False(ComplianceRules.CanTrade([true,false,true]));
 [Fact] public void Product_rule_allows_compliant_lot()=>Assert.True(ComplianceRules.CanTrade([true,true]));
 [Fact] public void Expired_certification_is_invalid()=>Assert.False(ComplianceRules.IsCertificationValid(new(2025,12,31),new(2026,1,1),"APPROVED"));
 [Fact] public void Audit_score_exposes_non_conformity()=>Assert.Equal(75m,ComplianceRules.AuditScore([(3m,true),(1m,false)]));
 [Fact] public void Late_action_plan_is_detected()=>Assert.True(ComplianceRules.IsActionPlanLate(new(2026,1,1),new(2026,1,2),"OPEN"));
 [Fact] public void Esg_calculation_uses_documented_weights()=>Assert.Equal(77m,SustainabilityCalculations.EsgScore(80,70,80));
 [Fact] public void Carbon_calculation_is_deterministic_and_net_of_sequestration()=>Assert.Equal(0.1500m,SustainabilityCalculations.CarbonTonnes(100,2,50));
 [Fact] public void Carbon_rejects_negative_activity()=>Assert.Throws<ArgumentOutOfRangeException>(()=>SustainabilityCalculations.CarbonTonnes(-1,2));
}
