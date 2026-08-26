using Agro360.Domain.Documents;
using Agro360.SharedKernel;

namespace Agro360.UnitTests;

public sealed class DocumentRulesTests
{
    [Fact] public void Valid_upload_accepts_safe_pdf()=>Assert.Equal(".pdf",DocumentRules.ValidateFile("laudo.pdf","application/pdf",1024));
    [Fact] public void Dangerous_extension_is_rejected()=>Assert.Throws<DomainException>(()=>DocumentRules.ValidateFile("payload.exe","application/octet-stream",10));
    [Fact] public void Oversized_upload_is_rejected()=>Assert.Throws<DomainException>(()=>DocumentRules.ValidateFile("foto.jpg","image/jpeg",DocumentRules.DefaultMaximumBytes+1));
    [Fact] public void Path_traversal_name_is_rejected()=>Assert.Throws<DomainException>(()=>DocumentRules.ValidateFile("../laudo.pdf","application/pdf",10));
    [Fact] public async Task Hash_is_sha256(){await using var data=new MemoryStream("agro360"u8.ToArray());Assert.Equal("96b5bbd701a451dbed4a25b91fabc8e81f0ae09e45a61abe3d7d6b8ad4da1897",await DocumentRules.Sha256Async(data));}
    [Fact] public void Rejected_evidence_requires_reason()=>Assert.Throws<DomainException>(()=>DocumentRules.ValidateDecision("REJECTED",null));
    [Fact] public void Validated_evidence_accepts_no_reason()=>DocumentRules.ValidateDecision("VALIDATED",null);
    [Fact] public void Version_and_revocation_require_reason()=>Assert.Throws<DomainException>(()=>DocumentRules.RequireReason("","revogar"));
}
