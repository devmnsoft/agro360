using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;

namespace Agro360.Web.Security;

public sealed class PageTokenAuthOptions : AuthenticationSchemeOptions
{
    public string Issuer { get; set; } = "MNSOFT.Agro360";
    public string Audience { get; set; } = "MNSOFT.Agro360.Clients";
    public string SigningKey { get; set; } = string.Empty;
}

/// <summary>
/// Autentica páginas do host Web por um cookie HttpOnly protegido com DataProtection
/// que carrega o JWT Bearer emitido pela API. A assinatura criptográfica HMAC-SHA256
/// do token é estritamente validada com a chave segura configurada (Jwt:SigningKey)
/// e os prazos exp/nbf/iss/aud são verificados.
/// </summary>
public sealed class PageTokenAuthHandler : AuthenticationHandler<PageTokenAuthOptions>
{
    public const string SchemeName = "Agro360.Web.Page";
    public const string CookieName = "agro360.page_token";
    public const string Purpose = "Agro360.Web.PageToken.v1";
    private static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    private readonly IDataProtector _protector;

    public PageTokenAuthHandler(IOptionsMonitor<PageTokenAuthOptions> options, ILoggerFactory logger, UrlEncoder encoder, IDataProtectionProvider dataProtection)
        : base(options, logger, encoder)
    {
        _protector = dataProtection.CreateProtector(Purpose);
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cookie = Request.Cookies[CookieName];
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        string rawToken;
        try
        {
            rawToken = _protector.Unprotect(cookie);
        }
        catch (CryptographicException)
        {
            return Task.FromResult(AuthenticateResult.Fail("Credencial da página é inválida."));
        }

        var (principal, error, _) = ValidateToken(rawToken, Options.Issuer, Options.Audience, Options.SigningKey);
        return principal is null
            ? Task.FromResult(AuthenticateResult.Fail(error ?? "Credencial da página é inválida."))
            : Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Redirect("/");
        return Task.CompletedTask;
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/json";
        await Response.WriteAsJsonAsync(new
        {
            type = "forbidden_page",
            title = "Acesso não autorizado.",
            detail = "Seu perfil não permite abrir esta página.",
            status = 403,
            traceId = Context.TraceIdentifier
        });
    }

    /// <summary>Valida o token criptograficamente verificando assinatura HMAC-SHA256, algoritmo e claims exp/nbf/iss/aud.</summary>
    public static (ClaimsPrincipal? Principal, string? Error, DateTime? ExpiresAtUtc) ValidateToken(string token, string expectedIssuer, string expectedAudience, string? signingKey)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return (null, "Credencial não informada.", null);
        }

        var segments = token.Split('.');
        if (segments.Length != 3)
        {
            return (null, "Credencial em formato inválido.", null);
        }

        // Validação do Header
        var headerBytes = Base64UrlDecode(segments[0]);
        if (headerBytes is null)
        {
            return (null, "Credencial em formato inválido.", null);
        }

        try
        {
            using var headerDoc = JsonDocument.Parse(headerBytes);
            if (!headerDoc.RootElement.TryGetProperty("alg", out var algElem) ||
                !string.Equals(algElem.GetString(), "HS256", StringComparison.OrdinalIgnoreCase))
            {
                return (null, "Algoritmo de assinatura não permitido.", null);
            }
        }
        catch (JsonException)
        {
            return (null, "Credencial em formato inválido.", null);
        }

        // Validação criptográfica da assinatura HMAC-SHA256
        if (!string.IsNullOrWhiteSpace(signingKey))
        {
            var keyBytes = Encoding.UTF8.GetBytes(signingKey);
            var signedBytes = Encoding.UTF8.GetBytes($"{segments[0]}.{segments[1]}");
            var expectedSignature = HMACSHA256.HashData(keyBytes, signedBytes);
            var actualSignature = Base64UrlDecode(segments[2]);

            if (actualSignature is null || !CryptographicOperations.FixedTimeEquals(expectedSignature, actualSignature))
            {
                return (null, "Assinatura da credencial é inválida.", null);
            }
        }
        else
        {
            return (null, "Chave de assinatura não configurada no host Web.", null);
        }

        return TryDecodePayload(segments[1], expectedIssuer, expectedAudience);
    }

    /// <summary>Decodifica o payload base64url do JWT e valida exp/nbf/iss/aud.</summary>
    public static (ClaimsPrincipal? Principal, string? Error, DateTime? ExpiresAtUtc) TryDecode(string token, string expectedIssuer, string expectedAudience)
    {
        var segments = token.Split('.');
        if (segments.Length != 3)
        {
            return (null, "Credencial em formato inválido.", null);
        }
        return TryDecodePayload(segments[1], expectedIssuer, expectedAudience);
    }

    private static (ClaimsPrincipal? Principal, string? Error, DateTime? ExpiresAtUtc) TryDecodePayload(string payloadSegment, string expectedIssuer, string expectedAudience)
    {
        try
        {
            var payloadBytes = Base64UrlDecode(payloadSegment);
            if (payloadBytes is null)
            {
                return (null, "Credencial em formato inválido.", null);
            }

            using var document = JsonDocument.Parse(payloadBytes);
            var root = document.RootElement;
            var now = DateTimeOffset.UtcNow;

            if (!root.TryGetProperty("exp", out var expElement) || !TryGetUtcValue(expElement, out var expUtc))
            {
                return (null, "Credencial sem expiração válida.", null);
            }
            if (expUtc < now.Add(ClockSkew))
            {
                return (null, "Sessão expirada.", null);
            }
            if (root.TryGetProperty("nbf", out var nbfElement) && TryGetUtcValue(nbfElement, out var nbfUtc) && nbfUtc > now.Add(ClockSkew))
            {
                return (null, "Sessão ainda não disponível.", null);
            }
            if (!root.TryGetProperty("iss", out var issuerElement) || !string.Equals(issuerElement.GetString(), expectedIssuer, StringComparison.Ordinal))
            {
                return (null, "Credencial emitida por outra origem.", null);
            }
            if (!root.TryGetProperty("aud", out var audienceElement) || !MatchesAudience(audienceElement, expectedAudience))
            {
                return (null, "Credencial destinada a outra aplicação.", null);
            }

            var claims = new List<Claim>();
            AddStringClaim(claims, root, "sub", ClaimTypes.NameIdentifier);
            AddStringClaim(claims, root, "email", ClaimTypes.Email);
            AddStringClaim(claims, root, "email", ClaimTypes.Name);
            AddStringClaim(claims, root, "tenant_id", "tenant_id");
            AddStringArrayClaim(claims, root, "role", ClaimTypes.Role);
            AddStringArrayClaim(claims, root, "permission", "permission");

            // O e-mail já é espelhado na claim Name (exibição); RoleClaimType explícito mantém IsInRole funcional.
            var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
            return (new ClaimsPrincipal(identity), null, expUtc);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or OverflowException or InvalidOperationException or NotSupportedException or ArgumentException)
        {
            return (null, "Credencial em formato inválido.", null);
        }
    }

    private static bool TryGetUtcValue(JsonElement element, out DateTime utc)
    {
        utc = default;
        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                if (!element.TryGetInt64(out var seconds))
                {
                    return false;
                }
                try
                {
                    utc = DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return false;
                }
                return true;
            case JsonValueKind.String:
                if (!element.TryGetDateTime(out var parsed))
                {
                    return false;
                }
                utc = AsUtc(parsed);
                return true;
            default:
                return false;
        }
    }

    private static DateTime AsUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private static bool MatchesAudience(JsonElement audienceElement, string expectedAudience)
    {
        if (audienceElement.ValueKind == JsonValueKind.String)
        {
            return string.Equals(audienceElement.GetString(), expectedAudience, StringComparison.Ordinal);
        }
        if (audienceElement.ValueKind == JsonValueKind.Array)
        {
            return audienceElement.EnumerateArray().Any(item => item.ValueKind == JsonValueKind.String && string.Equals(item.GetString(), expectedAudience, StringComparison.Ordinal));
        }
        return false;
    }

    private static void AddStringClaim(List<Claim> claims, JsonElement root, string property, string claimType)
    {
        if (root.TryGetProperty(property, out var element) && element.ValueKind == JsonValueKind.String)
        {
            var value = element.GetString();
            if (!string.IsNullOrWhiteSpace(value))
            {
                claims.Add(new Claim(claimType, value));
            }
        }
    }

    private static void AddStringArrayClaim(List<Claim> claims, JsonElement root, string property, string claimType)
    {
        if (!root.TryGetProperty(property, out var element))
        {
            return;
        }
        if (element.ValueKind == JsonValueKind.String)
        {
            AddStringClaim(claims, root, property, claimType);
            return;
        }
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                {
                    claims.Add(new Claim(claimType, item.GetString()!));
                }
            }
        }
    }

    private static byte[]? Base64UrlDecode(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
            case 1: return null;
        }
        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
