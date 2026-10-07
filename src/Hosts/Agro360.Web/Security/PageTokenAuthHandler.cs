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
    private const string DevelopmentSigningKey = "agro360-dev-insecure-jwt-key-not-for-prod-32b";

    public string Issuer { get; set; } = "MNSOFT.Agro360";
    public string Audience { get; set; } = "MNSOFT.Agro360.Clients";
    public string SigningKey { get; set; } = string.Empty;
    public string SessionValidationUrl { get; set; } = string.Empty;

    public static string ResolveSigningKey(string? configuredKey, bool isDevelopment)
    {
        var key = string.IsNullOrWhiteSpace(configuredKey)
            ? isDevelopment ? DevelopmentSigningKey : null
            : configuredKey;

        if (key is null)
        {
            throw new InvalidOperationException("Jwt:SigningKey precisa ser configurada fora do ambiente de desenvolvimento.");
        }
        if (Encoding.UTF8.GetByteCount(key) < 32)
        {
            throw new InvalidOperationException("Jwt:SigningKey deve possuir pelo menos 32 bytes.");
        }
        if (!isDevelopment && key.Contains(DevelopmentSigningKey, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Jwt:SigningKey insegura não é permitida fora do ambiente de desenvolvimento.");
        }

        return key;
    }
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
    private static readonly Action<ILogger, int, string?, Exception?> LogSessionValidationRejected =
        LoggerMessage.Define<int, string?>(
            LogLevel.Warning,
            new EventId(1, nameof(LogSessionValidationRejected)),
            "A validação da sessão da página foi negada com status {StatusCode}: {Reason}");

    private readonly IDataProtector _protector;
    private readonly IHttpClientFactory _httpClientFactory;
    private int? _sessionValidationFailureStatusCode;
    private string? _sessionValidationFailureMessage;

    public PageTokenAuthHandler(IOptionsMonitor<PageTokenAuthOptions> options, ILoggerFactory logger, UrlEncoder encoder, IDataProtectionProvider dataProtection, IHttpClientFactory httpClientFactory)
        : base(options, logger, encoder)
    {
        _protector = dataProtection.CreateProtector(Purpose);
        _httpClientFactory = httpClientFactory;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var cookie = Request.Cookies[CookieName];
        if (string.IsNullOrWhiteSpace(cookie))
        {
            return AuthenticateResult.NoResult();
        }

        string rawToken;
        try
        {
            rawToken = _protector.Unprotect(cookie);
        }
        catch (CryptographicException)
        {
            return AuthenticateResult.Fail("Credencial da página é inválida.");
        }

        var (principal, error, _) = ValidateToken(rawToken, Options.Issuer, Options.Audience, Options.SigningKey);
        if (principal is null)
        {
            return AuthenticateResult.Fail(error ?? "Credencial da página é inválida.");
        }

        var validation = await ValidateSessionAsync(_httpClientFactory, Options.SessionValidationUrl, rawToken, principal, Context.RequestAborted).ConfigureAwait(false);
        if (validation.Principal is null)
        {
            _sessionValidationFailureStatusCode = validation.StatusCode;
            _sessionValidationFailureMessage = validation.Error;
            if (validation.StatusCode is StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden)
            {
                Response.Cookies.Delete(CookieName, new CookieOptions { HttpOnly = true, Path = "/" });
            }
            LogSessionValidationRejected(Logger, validation.StatusCode, validation.Error, null);
            return AuthenticateResult.Fail(validation.Error ?? "Sessão não validada.");
        }

        return AuthenticateResult.Success(new AuthenticationTicket(validation.Principal, Scheme.Name));
    }

    public static async Task<(ClaimsPrincipal? Principal, string? Error, int StatusCode)> ValidateSessionAsync(
        IHttpClientFactory httpClientFactory,
        string validationUrl,
        string accessToken,
        ClaimsPrincipal principal,
        CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(validationUrl, UriKind.Absolute, out var sessionUri))
        {
            return (null, "A validação da sessão não está configurada.", StatusCodes.Status503ServiceUnavailable);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, sessionUri);
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        try
        {
            using var response = await httpClientFactory.CreateClient("PageSessionValidation")
                .SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                var detail = await ReadProblemDetailAsync(response, cancellationToken).ConfigureAwait(false);
                return (null, detail, (int)response.StatusCode);
            }

            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("isValid", out var isValid) || isValid.ValueKind != JsonValueKind.True ||
                !TryGetGuid(root, "userId", out var userId) ||
                !TryGetGuid(root, "tenantId", out var tenantId) ||
                !Guid.TryParse(principal.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var expectedUserId) ||
                !Guid.TryParse(principal.FindFirst("tenant_id")?.Value, out var expectedTenantId) ||
                userId != expectedUserId || tenantId != expectedTenantId ||
                !TryGetString(root, "name", out var userName) ||
                !TryGetString(root, "email", out var email) ||
                !TryGetStringArray(root, "roles", out var roles) ||
                !TryGetStringArray(root, "permissions", out var permissions))
            {
                return (null, "A resposta da validação da sessão é inválida.", StatusCodes.Status502BadGateway);
            }

            var refreshedIdentity = new ClaimsIdentity(
                principal.Claims.Where(claim => claim.Type is not (ClaimTypes.Name or ClaimTypes.Email or ClaimTypes.Role or "permission")),
                SchemeName,
                ClaimTypes.Name,
                ClaimTypes.Role);
            refreshedIdentity.AddClaim(new Claim(ClaimTypes.Name, userName));
            refreshedIdentity.AddClaim(new Claim(ClaimTypes.Email, email));
            foreach (var role in roles)
            {
                refreshedIdentity.AddClaim(new Claim(ClaimTypes.Role, role));
            }
            foreach (var permission in permissions)
            {
                refreshedIdentity.AddClaim(new Claim("permission", permission));
            }

            return (new ClaimsPrincipal(refreshedIdentity), null, StatusCodes.Status200OK);
        }
        catch (HttpRequestException)
        {
            return (null, "A API de sessão está indisponível; acesso negado até a validação ser restabelecida.", StatusCodes.Status503ServiceUnavailable);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (null, "A API de sessão excedeu o tempo limite; acesso negado até a validação ser restabelecida.", StatusCodes.Status503ServiceUnavailable);
        }
        catch (JsonException)
        {
            return (null, "A resposta da validação da sessão é inválida.", StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<string> ReadProblemDetailAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            using var document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
                cancellationToken: cancellationToken).ConfigureAwait(false);
            if (document.RootElement.ValueKind == JsonValueKind.Object &&
                document.RootElement.TryGetProperty("detail", out var detail) &&
                detail.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(detail.GetString()))
            {
                return detail.GetString()!;
            }
        }
        catch (JsonException)
        {
            // HTTP status remains authoritative even when an upstream error body is not JSON.
        }

        return "Sessão inválida, revogada ou usuário/tenant inativo.";
    }

    private static bool TryGetGuid(JsonElement root, string propertyName, out Guid value)
    {
        value = default;
        return root.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            Guid.TryParse(property.GetString(), out value);
    }

    private static bool TryGetString(JsonElement root, string propertyName, out string value)
    {
        value = string.Empty;
        if (!root.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            return false;
        }

        value = property.GetString()!;
        return true;
    }

    private static bool TryGetStringArray(JsonElement root, string propertyName, out string[] values)
    {
        values = [];
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var items = new List<string>();
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                return false;
            }
            items.Add(item.GetString()!);
        }

        values = items.ToArray();
        return true;
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        if (_sessionValidationFailureStatusCode is >= StatusCodes.Status500InternalServerError)
        {
            Response.StatusCode = _sessionValidationFailureStatusCode.Value;
            Response.ContentType = "application/json";
            await Response.WriteAsJsonAsync(new
            {
                type = "session_validation_unavailable",
                title = "Não foi possível validar a sessão.",
                detail = _sessionValidationFailureMessage,
                status = _sessionValidationFailureStatusCode.Value,
                traceId = Context.TraceIdentifier
            }, Context.RequestAborted).ConfigureAwait(false);
            return;
        }

        Response.Redirect("/");
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
