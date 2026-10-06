using System.Globalization;
using System.Text.Json;
using Agro360.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Agro360.Web")
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("MNSOFT.Agro360");
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

builder.Services.AddRazorPages();
builder.Services.AddHealthChecks();
builder.Services.AddHttpClient();

var pageIssuer = builder.Configuration["Jwt:Issuer"] ?? "MNSOFT.Agro360";
var pageAudience = builder.Configuration["Jwt:Audience"] ?? "MNSOFT.Agro360.Clients";
var pageSigningKey = builder.Configuration["Jwt:SigningKey"] ?? "agro360-dev-insecure-jwt-key-not-for-prod-32b";

builder.Services
    .AddAuthentication(PageTokenAuthHandler.SchemeName)
    .AddScheme<PageTokenAuthOptions, PageTokenAuthHandler>(PageTokenAuthHandler.SchemeName, options =>
    {
        options.Issuer = pageIssuer;
        options.Audience = pageAudience;
        options.SigningKey = pageSigningKey;
    });

builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

builder.Services.Configure<RazorPagesOptions>(options =>
{
    // Páginas públicas: o portal de parceiro tem login próprio e as demais estão listadas por decisão de escopo.
    options.Conventions.AllowAnonymousToPage("/");
    options.Conventions.AllowAnonymousToPage("/Error");
    options.Conventions.AllowAnonymousToPage("/Saas/Accept");
    options.Conventions.AllowAnonymousToFolder("/Portal");
});

var app = builder.Build();
var configuredApiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:8081";
var apiOrigin = Uri.TryCreate(configuredApiBaseUrl, UriKind.Absolute, out var apiUri)
    ? apiUri.GetLeftPart(UriPartial.Authority)
    : "http://localhost:8081";

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers["Content-Security-Policy"] =
        $"default-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self'; img-src 'self' data:; connect-src 'self' {apiOrigin}; font-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next().ConfigureAwait(false);
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthChecks("/health").AllowAnonymous();

// Sincroniza a credencial da página: o cliente envia o mesmo accessToken usado na API.
// Vazio remove o cookie (logout); inválido responde 400 sem criar sessão de página.
Func<HttpContext, Task<IResult>> pageTokenSync = HandlePageTokenSync;
app.MapPost("/auth/page", pageTokenSync).AllowAnonymous();

app.MapRazorPages();
foreach (var route in new[] { "/Tasks", "/Tasks/Dashboard", "/Tasks/New", "/Tasks/Details", "/Alerts", "/Alerts/Details", "/Rules", "/Rules/New", "/Rules/Edit", "/Workflows", "/Workflows/Details", "/Workflows/Decision", "/Notifications", "/Calendar/Operational", "/Outbox" })
{
    app.MapGet(route, (string? id) => Results.Redirect($"/Work?view={Uri.EscapeDataString(route)}{(string.IsNullOrWhiteSpace(id) ? "" : $"&id={Uri.EscapeDataString(id)}")}")).AllowAnonymous();
}

await app.RunAsync();

static IResult InvalidPageToken(HttpContext context, string? detail) => Results.Json(new
{
    type = "invalid_page_token",
    title = "Credencial inválida.",
    detail = string.IsNullOrWhiteSpace(detail) ? "A credencial enviada não pôde ser validada." : detail,
    status = 400,
    traceId = context.TraceIdentifier
}, statusCode: 400);

async Task<IResult> HandlePageTokenSync(HttpContext context)
{
    string rawBody;
    try
    {
        using var reader = new StreamReader(context.Request.Body);
        rawBody = await reader.ReadToEndAsync();
    }
    catch (IOException)
    {
        return InvalidPageToken(context, "Credencial em formato inválido.");
    }

    var accessToken = string.Empty;
    if (!string.IsNullOrWhiteSpace(rawBody))
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            if (document.RootElement.TryGetProperty("accessToken", out var value) && value.ValueKind == JsonValueKind.String)
            {
                accessToken = value.GetString() ?? string.Empty;
            }
        }
        catch (JsonException)
        {
            return InvalidPageToken(context, "Credencial em formato inválido.");
        }
    }

    if (string.IsNullOrEmpty(accessToken))
    {
        context.Response.Cookies.Delete(PageTokenAuthHandler.CookieName, new CookieOptions { HttpOnly = true, Path = "/" });
        return Results.Json(new { ok = true });
    }

    var (principal, error, expiresAt) = PageTokenAuthHandler.ValidateToken(accessToken, pageIssuer, pageAudience, pageSigningKey);
    if (principal is null || expiresAt is null)
    {
        return InvalidPageToken(context, error);
    }

    var maxAge = expiresAt.Value - DateTime.UtcNow;
    if (maxAge.TotalSeconds < 1)
    {
        return InvalidPageToken(context, "Sessão expirada.");
    }

    // Validação com o backend confiável: usuário/tenant ativos e sessão não revogada
    var httpClientFactory = context.RequestServices.GetRequiredService<IHttpClientFactory>();
    var httpClient = httpClientFactory.CreateClient();
    using var sessionReq = new HttpRequestMessage(HttpMethod.Get, $"{apiOrigin}/api/v1/auth/session");
    sessionReq.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
    try
    {
        using var sessionResp = await httpClient.SendAsync(sessionReq, context.RequestAborted).ConfigureAwait(false);
        if (!sessionResp.IsSuccessStatusCode)
        {
            var problem = await sessionResp.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: context.RequestAborted).ConfigureAwait(false);
            var detail = problem.TryGetProperty("detail", out var d) ? d.GetString() : "Usuário ou tenant inativo ou bloqueado.";
            return Results.Json(new
            {
                type = "session_rejected",
                title = "Acesso negado.",
                detail,
                status = (int)sessionResp.StatusCode,
                traceId = context.TraceIdentifier
            }, statusCode: (int)sessionResp.StatusCode);
        }
    }
    catch (HttpRequestException)
    {
        // Se a API estiver temporariamente inacessível durante o sync, a validação criptográfica do token já garantiu a integridade
    }

    var protector = context.RequestServices.GetRequiredService<IDataProtectionProvider>().CreateProtector(PageTokenAuthHandler.Purpose);
    context.Response.Cookies.Append(PageTokenAuthHandler.CookieName, protector.Protect(accessToken), new CookieOptions
    {
        HttpOnly = true,
        Path = "/",
        Secure = context.Request.IsHttps,
        SameSite = SameSiteMode.Lax,
        MaxAge = maxAge
    });
    return Results.Json(new { ok = true });
}

