using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agro360.Web.Security;
using Xunit;

namespace Agro360.UnitTests;

public sealed class WebPageTokenAuthTests
{
    private const string ExpectedIssuer = "MNSOFT.Agro360";
    private const string ExpectedAudience = "MNSOFT.Agro360.Clients";
    private const string SigningKey = "test-signing-key-with-more-than-32-bytes-secure!";

    private static string Base64UrlEncode(byte[] input) =>
        Convert.ToBase64String(input).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string CreateJwt(
        string signingKey,
        string issuer = ExpectedIssuer,
        string audience = ExpectedAudience,
        string alg = "HS256",
        DateTimeOffset? expires = null,
        DateTimeOffset? notBefore = null,
        string role = "operator",
        string sub = "11111111-1111-1111-1111-111111111111",
        string email = "user@agro360.local",
        string tenantId = "22222222-2222-2222-2222-222222222222")
    {
        var header = JsonSerializer.Serialize(new { alg, typ = "JWT" });
        var now = DateTimeOffset.UtcNow;
        var exp = expires ?? now.AddMinutes(30);
        var payloadObj = new Dictionary<string, object>
        {
            ["sub"] = sub,
            ["email"] = email,
            ["tenant_id"] = tenantId,
            ["iss"] = issuer,
            ["aud"] = audience,
            ["exp"] = exp.ToUnixTimeSeconds(),
            ["role"] = new[] { role },
            ["permission"] = new[] { "work.read" }
        };
        if (notBefore.HasValue)
        {
            payloadObj["nbf"] = notBefore.Value.ToUnixTimeSeconds();
        }

        var payload = JsonSerializer.Serialize(payloadObj);
        var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(header));
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        var signatureBytes = HMACSHA256.HashData(Encoding.UTF8.GetBytes(signingKey), Encoding.UTF8.GetBytes($"{headerB64}.{payloadB64}"));
        var signatureB64 = Base64UrlEncode(signatureBytes);

        return $"{headerB64}.{payloadB64}.{signatureB64}";
    }

    [Fact]
    public void ValidTokenAuthenticatesSuccessfully()
    {
        var token = CreateJwt(SigningKey);
        var (principal, error, expiresAt) = PageTokenAuthHandler.ValidateToken(token, ExpectedIssuer, ExpectedAudience, SigningKey);

        Assert.NotNull(principal);
        Assert.Null(error);
        Assert.NotNull(expiresAt);
        Assert.Equal("11111111-1111-1111-1111-111111111111", principal.FindFirst(ClaimTypes.NameIdentifier)?.Value);
        Assert.Equal("user@agro360.local", principal.FindFirst(ClaimTypes.Email)?.Value);
        Assert.Equal("22222222-2222-2222-2222-222222222222", principal.FindFirst("tenant_id")?.Value);
        Assert.True(principal.IsInRole("operator"));
    }

    [Fact]
    public void TamperedPayloadFailsCryptographicVerification()
    {
        var token = CreateJwt(SigningKey, role: "operator");
        var parts = token.Split('.');

        // Tamper payload: elevate to SUPER_ADMIN without valid signature
        var tamperedPayloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1].Replace('-', '+').Replace('_', '/') + "=="))
            .Replace("operator", "SUPER_ADMIN");
        var tamperedPayloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(tamperedPayloadJson));
        var tamperedToken = $"{parts[0]}.{tamperedPayloadB64}.{parts[2]}"; // Keeps original signature

        var (principal, error, _) = PageTokenAuthHandler.ValidateToken(tamperedToken, ExpectedIssuer, ExpectedAudience, SigningKey);

        Assert.Null(principal);
        Assert.Equal("Assinatura da credencial é inválida.", error);
    }

    [Fact]
    public void WrongSigningKeyFailsVerification()
    {
        var token = CreateJwt("wrong-secret-key-32-bytes-minimum-length!");
        var (principal, error, _) = PageTokenAuthHandler.ValidateToken(token, ExpectedIssuer, ExpectedAudience, SigningKey);

        Assert.Null(principal);
        Assert.Equal("Assinatura da credencial é inválida.", error);
    }

    [Fact]
    public void DisallowedAlgorithmFailsVerification()
    {
        var token = CreateJwt(SigningKey, alg: "none");
        var (principal, error, _) = PageTokenAuthHandler.ValidateToken(token, ExpectedIssuer, ExpectedAudience, SigningKey);

        Assert.Null(principal);
        Assert.Equal("Algoritmo de assinatura não permitido.", error);
    }

    [Fact]
    public void ExpiredTokenFailsVerification()
    {
        var expiredTime = DateTimeOffset.UtcNow.AddMinutes(-10);
        var token = CreateJwt(SigningKey, expires: expiredTime);
        var (principal, error, _) = PageTokenAuthHandler.ValidateToken(token, ExpectedIssuer, ExpectedAudience, SigningKey);

        Assert.Null(principal);
        Assert.Equal("Sessão expirada.", error);
    }

    [Fact]
    public void DivergentIssuerFailsVerification()
    {
        var token = CreateJwt(SigningKey, issuer: "Other.Issuer");
        var (principal, error, _) = PageTokenAuthHandler.ValidateToken(token, ExpectedIssuer, ExpectedAudience, SigningKey);

        Assert.Null(principal);
        Assert.Equal("Credencial emitida por outra origem.", error);
    }

    [Fact]
    public void DivergentAudienceFailsVerification()
    {
        var token = CreateJwt(SigningKey, audience: "Other.Audience");
        var (principal, error, _) = PageTokenAuthHandler.ValidateToken(token, ExpectedIssuer, ExpectedAudience, SigningKey);

        Assert.Null(principal);
        Assert.Equal("Credencial destinada a outra aplicação.", error);
    }

    [Fact]
    public void MalformedTokenFailsVerification()
    {
        var (principal, error, _) = PageTokenAuthHandler.ValidateToken("invalid.token", ExpectedIssuer, ExpectedAudience, SigningKey);

        Assert.Null(principal);
        Assert.Equal("Credencial em formato inválido.", error);
    }
}
