using Agro360.Domain.Support;

namespace Agro360.UnitTests;

public sealed class SupportRulesTests
{
 [Fact] public void Cancellation_requires_reason()=>Assert.Throws<ArgumentException>(()=>SupportRules.ValidateTransition("CANCELLED",null,null));
 [Fact] public void Cancellation_with_reason_is_valid()=>SupportRules.ValidateTransition("CANCELLED","Solicitação duplicada",null);
 [Fact] public void Resolution_requires_customer_response()=>Assert.Throws<ArgumentException>(()=>SupportRules.ValidateTransition("RESOLVED",null,null));
 [Fact] public void Reopening_requires_reason()=>Assert.Throws<ArgumentException>(()=>SupportRules.ValidateTransition("REOPENED"," ",null));
 [Fact] public void Sla_is_calculated_from_opening_time(){var opened=DateTimeOffset.Parse("2026-08-28T10:00:00Z");var due=SupportRules.CalculateSla(opened,60,240);Assert.Equal(opened.AddHours(1),due.FirstResponse);Assert.Equal(opened.AddHours(4),due.Resolution);}
 [Fact] public void Non_positive_sla_is_rejected()=>Assert.Throws<ArgumentOutOfRangeException>(()=>SupportRules.CalculateSla(DateTimeOffset.UtcNow,0,30));
 [Fact] public void Invalid_feedback_score_is_rejected()=>Assert.Throws<ArgumentOutOfRangeException>(()=>SupportRules.ValidateScore(11,0,10));
 [Fact] public void Phase_requires_evidence_or_checklist()=>Assert.Throws<ArgumentException>(()=>SupportRules.ValidatePhaseCompletion("TRAINING",true,false,null));
 [Fact] public void Go_live_requires_homologation()=>Assert.Throws<InvalidOperationException>(()=>SupportRules.ValidatePhaseCompletion("GO_LIVE",false,true,null));
}
