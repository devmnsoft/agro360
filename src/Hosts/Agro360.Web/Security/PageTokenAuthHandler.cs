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
}

/// <summary>
/// Autentica páginas do host Web por um cookie HttpOnly protegido com DataProtection
/// que carrega o mesmo JWT Bearer usado pela API. A assinatura do token não é
/// verificada neste host (a chave de assinatura fica na API); a integridade é garantida
/// pela proteção criptográfica do cookie e exp/iss/aud continuam sendo validados aqui.
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

        var (principal, error, _) = TryDecode(rawToken, Options.Issuer, Options.Audience);
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

    /// <summary>Decodifica o payload base64url do JWT sem verificar a assinatura e valida exp/nbf/iss/aud.</summary>
    public static (ClaimsPrincipal? Principal, string? Error, DateTime? ExpiresAtUtc) TryDecode(string token, string expectedIssuer, string expectedAudience)
    {
        try
        {
            var segments = token.Split('.');
            if (segments.Length != 3)
            {
                return (null, "Credencial em formato inválido.", null);
            }

            var payload = segments[1].Replace('-', '+').Replace('_', '/');
            switch (payload.Length % 4)
            {
                case 2:
                    payload += "==";
                    break;
                case 3:
                    payload += "=";
                    break;
                case 1:
                    return (null, "Credencial em formato inválido.", null);
            }

            using var document = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
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
}
