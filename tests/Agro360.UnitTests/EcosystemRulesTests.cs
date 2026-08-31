using Agro360.Domain.Ecosystem;
namespace Agro360.UnitTests;
public sealed class EcosystemRulesTests
{
 [Fact]public void Api_requires_explicit_known_scope(){Assert.Throws<ArgumentException>(()=>EcosystemRules.Scopes([]));Assert.Throws<ArgumentException>(()=>EcosystemRules.Scopes(["unknown"]));EcosystemRules.Scopes(["properties.read","webhooks"]);}
 [Fact]public void Webhook_requires_public_https(){Assert.Throws<ArgumentException>(()=>EcosystemRules.Webhook("http://example.com/hook"));Assert.Throws<ArgumentException>(()=>EcosystemRules.Webhook("https://localhost/hook"));Assert.Equal("https",EcosystemRules.Webhook("https://partner.example/hook").Scheme);}
 [Theory,InlineData("123.456.789-01","12345678901"),InlineData("12.345.678/0001-90","12345678000190")]public void Partner_document_is_normalized(string input,string expected)=>Assert.Equal(expected,EcosystemRules.Document(input));
}
