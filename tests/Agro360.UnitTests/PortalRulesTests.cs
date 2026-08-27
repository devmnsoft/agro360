using Agro360.Domain.Portal;
using Agro360.SharedKernel;

namespace Agro360.UnitTests;
public sealed class PortalRulesTests
{
 [Fact] public void Invitation_accepts_valid_pending_token()=>PortalRules.Invitation(DateTimeOffset.UtcNow.AddHours(1),DateTimeOffset.UtcNow,null,null);
 [Fact] public void Invitation_rejects_expired()=>Assert.Throws<DomainException>(()=>PortalRules.Invitation(DateTimeOffset.UtcNow.AddMinutes(-1),DateTimeOffset.UtcNow,null,null));
 [Fact] public void Invitation_rejects_revoked()=>Assert.Throws<DomainException>(()=>PortalRules.Invitation(DateTimeOffset.UtcNow.AddHours(1),DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null));
 [Fact] public void Quote_requires_an_item()=>Assert.Throws<DomainException>(()=>PortalRules.Quote([]));
 [Fact] public void Quote_requires_positive_quantity()=>Assert.Throws<DomainException>(()=>PortalRules.Quote([(Guid.NewGuid(),0)]));
 [Fact] public void Request_requires_a_useful_description()=>Assert.Throws<DomainException>(()=>PortalRules.Request("Ajuda","curta"));
 [Theory,InlineData("PRODUCER"),InlineData("SUPPLIER"),InlineData("TRANSPORTER"),InlineData("EXTERNAL_AUDITOR")] public void External_profiles_are_supported(string profile)=>Assert.Equal(profile,PortalRules.Profile(profile));
}
