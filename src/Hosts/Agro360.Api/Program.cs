using System.Text;
using System.Threading.RateLimiting;
using Agro360.Api.Health;
using Agro360.Api.Middleware;
using Agro360.Application;
using Agro360.Infrastructure;
using Agro360.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Agro360.Api"));

var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName(DataProtectionSettings.ApplicationName);
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

builder.Services.AddAgro360Infrastructure(builder.Configuration);
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"]);

var jwt = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? throw new InvalidOperationException("A seção Jwt é obrigatória.");
if (Encoding.UTF8.GetByteCount(jwt.SigningKey) < 32)
{
    throw new InvalidOperationException("Jwt:SigningKey deve possuir pelo menos 32 bytes.");
}

if (!builder.Environment.IsDevelopment())
{
    var insecureKeys = new[] { "agro360-dev-insecure-jwt-key", "secret", "password", "123456" };
    if (insecureKeys.Any(k => jwt.SigningKey.Contains(k, StringComparison.OrdinalIgnoreCase)))
    {
        throw new InvalidOperationException("Jwt:SigningKey inseguro não é permitido em ambiente produtivo.");
    }
}

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "name",
            RoleClaimType = "role"
        };
    });

builder.Services.AddAuthorization(options =>
{
    foreach (var permission in Permissions.Administrator)
    {
        options.AddPolicy(permission, policy => policy
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission)));
    }
    options.AddPolicy(Permissions.PlatformAdmin, policy => policy
        .RequireAuthenticatedUser()
        .RequireRole("SUPER_ADMIN")
        .AddRequirements(new PermissionRequirement(Permissions.PlatformAdmin)));
    options.AddPolicy(Permissions.PortalAccess, policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("permission", Permissions.PortalAccess)
        .AddRequirements(new PermissionRequirement(Permissions.PortalAccess)));
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    {
        var path = context.Request.Path.Value?.ToLowerInvariant() ?? "";
        var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var user = context.User.FindFirst("sub")?.Value ?? ip;

        if (path.Contains("/auth/login", StringComparison.Ordinal) || path.Contains("/portal/login", StringComparison.Ordinal))
        {
            return RateLimitPartition.GetFixedWindowLimiter($"login_{ip}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 15,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        }

        if (path.Contains("/auth/mfa", StringComparison.Ordinal))
        {
            return RateLimitPartition.GetFixedWindowLimiter($"mfa_{ip}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        }

        if (path.Contains("/auth/refresh", StringComparison.Ordinal) || path.Contains("/portal/refresh", StringComparison.Ordinal))
        {
            return RateLimitPartition.GetFixedWindowLimiter($"refresh_{ip}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 40,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        }

        if (path.Contains("/invitations", StringComparison.Ordinal))
        {
            return RateLimitPartition.GetFixedWindowLimiter($"invite_{user}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        }

        if (path.Contains("/bootstrap", StringComparison.Ordinal))
        {
            return RateLimitPartition.GetFixedWindowLimiter($"bootstrap_{ip}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        }

        if (path.Contains("/public/", StringComparison.Ordinal))
        {
            return RateLimitPartition.GetFixedWindowLimiter($"public_{ip}", _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 60,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
        }

        return RateLimitPartition.GetFixedWindowLimiter(user, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 180,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["https://localhost:7080", "http://localhost:8080", "https://127.0.0.1:7080", "http://127.0.0.1:8080"];
builder.Services.AddCors(options => options.AddPolicy("web", policy => policy
    .WithOrigins(allowedOrigins)
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

app.UseForwardedHeaders(new Microsoft.AspNetCore.Builder.ForwardedHeadersOptions
{
    ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedFor | Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders.XForwardedProto
});


app.UseMiddleware<CorrelationIdMiddleware>();
app.UseSerilogRequestLogging();
app.UseMiddleware<ExceptionHandlingMiddleware>();
if (!app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("HttpsRedirection:Enabled"))
{
    app.UseHttpsRedirection();
}
app.UseCors("web");
app.UseRateLimiter();
app.UseAuthentication();
app.UseMiddleware<TenantContextMiddleware>();
app.UseAuthorization();


if (app.Environment.IsDevelopment() || builder.Configuration.GetValue<bool>("Swagger:Enabled"))
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.DocumentTitle = "MNSOFT Agro360 API";
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Agro360 API v1");
    });

    app.MapOpenApi();
}

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health", new() { Predicate = check => check.Tags.Contains("ready") });
app.MapControllers();
app.MapGet("/", () => Results.Ok(new
{
    product = "MNSOFT Agro 360",
    status = "operational",
    api = "v1"
})).AllowAnonymous();

await app.RunAsync();

public partial class Program
{
}
