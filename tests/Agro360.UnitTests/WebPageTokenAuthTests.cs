using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agro360.Web.Security;
using Microsoft.AspNetCore.Http;
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
    public void CookieRoundTripCompressesLargeTokenUnderBrowserLimit()
    {
        var protector = new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()
            .CreateProtector(PageTokenAuthHandler.Purpose);
        var token = CreateJwt(SigningKey) + "." + string.Concat(Enumerable.Repeat("inventory.receive.permission.", 200));
        Assert.True(token.Length > 4096);

        var cookie = PageTokenAuthHandler.ProtectCookie(protector, token);

        Assert.True(cookie.Length < 4096, $"Cookie com {cookie.Length} bytes excede o limite de 4096 do navegador.");
        Assert.Equal(token, PageTokenAuthHandler.UnprotectCookie(protector, cookie));
    }

    [Fact]
    public void UnprotectCookieRejectsGarbageWithoutThrowing()
    {
        var protector = new Microsoft.AspNetCore.DataProtection.EphemeralDataProtectionProvider()
            .CreateProtector(PageTokenAuthHandler.Purpose);

        Assert.Null(PageTokenAuthHandler.UnprotectCookie(protector, "not-a-protected-payload"));
        Assert.Null(PageTokenAuthHandler.UnprotectCookie(protector, Convert.ToBase64String(new byte[] { 1, 2, 3 })));
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

    [Fact]
    public void MissingProductionSigningKeyIsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PageTokenAuthOptions.ResolveSigningKey(null, isDevelopment: false));

        Assert.Contains("precisa ser configurada", exception.Message);
    }

    [Fact]
    public void DevelopmentSigningKeyIsNotAcceptedOutsideDevelopment()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PageTokenAuthOptions.ResolveSigningKey("agro360-dev-insecure-jwt-key-not-for-prod-32b", isDevelopment: false));

        Assert.Contains("insegura", exception.Message);
    }

    [Fact]
    public void DevelopmentSigningKeyCannotBeExtendedForProduction()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            PageTokenAuthOptions.ResolveSigningKey("agro360-dev-insecure-jwt-key-not-for-prod-32b-extra", isDevelopment: false));

        Assert.Contains("insegura", exception.Message);
    }

    [Fact]
    public void ShortSigningKeyIsRejected()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => PageTokenAuthOptions.ResolveSigningKey("short", isDevelopment: true));

        Assert.Contains("32 bytes", exception.Message);
    }

    [Fact]
    public async Task SessionValidationRefreshesRolesAndPermissionsFromApi()
    {
        var token = CreateJwt(SigningKey, role: "operator");
        var (principal, _, _) = PageTokenAuthHandler.ValidateToken(token, ExpectedIssuer, ExpectedAudience, SigningKey);
        var responseBody = """
            {
              "isValid": true,
              "tenantId": "22222222-2222-2222-2222-222222222222",
              "userId": "11111111-1111-1111-1111-111111111111",
              "name": "Updated User",
              "email": "updated@agro360.local",
              "roles": ["manager"],
              "permissions": ["work.admin"]
            }
            """;
        var factory = new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
        });

        var result = await PageTokenAuthHandler.ValidateSessionAsync(
            factory,
            "https://api.agro360.local/api/v1/auth/session",
            token,
            principal!,
            CancellationToken.None);

        Assert.NotNull(result.Principal);
        Assert.Null(result.Error);
        Assert.True(result.Principal!.IsInRole("manager"));
        Assert.False(result.Principal.IsInRole("operator"));
        Assert.Contains(result.Principal.Claims, claim => claim.Type == "permission" && claim.Value == "work.admin");
        Assert.Equal("Updated User", result.Principal.Identity!.Name);
        Assert.Equal("updated@agro360.local", result.Principal.FindFirst(ClaimTypes.Email)?.Value);
    }

    [Fact]
    public async Task SessionValidationFailsClosedWhenApiIsUnavailable()
    {
        var token = CreateJwt(SigningKey);
        var (principal, _, _) = PageTokenAuthHandler.ValidateToken(token, ExpectedIssuer, ExpectedAudience, SigningKey);
        var factory = new StubHttpClientFactory(_ => throw new HttpRequestException("API unavailable"));

        var result = await PageTokenAuthHandler.ValidateSessionAsync(
            factory,
            "https://api.agro360.local/api/v1/auth/session",
            token,
            principal!,
            CancellationToken.None);

        Assert.Null(result.Principal);
        Assert.Equal(StatusCodes.Status503ServiceUnavailable, result.StatusCode);
        Assert.Contains("acesso negado", result.Error);
    }

    [Fact]
    public async Task MalformedSuccessfulSessionResponseIsRejected()
    {
        var token = CreateJwt(SigningKey);
        var (principal, _, _) = PageTokenAuthHandler.ValidateToken(token, ExpectedIssuer, ExpectedAudience, SigningKey);
        var factory = new StubHttpClientFactory(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"isValid":true}""", Encoding.UTF8, "application/json")
        });

        var result = await PageTokenAuthHandler.ValidateSessionAsync(
            factory,
            "https://api.agro360.local/api/v1/auth/session",
            token,
            principal!,
            CancellationToken.None);

        Assert.Null(result.Principal);
        Assert.Equal(StatusCodes.Status502BadGateway, result.StatusCode);
        Assert.Contains("resposta", result.Error);
    }

    private sealed class StubHttpClientFactory(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) =>
            new(new StubHttpMessageHandler(responseFactory));
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(responseFactory(request));
    }
}
