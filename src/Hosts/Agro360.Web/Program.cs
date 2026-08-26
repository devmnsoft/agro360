using System.Globalization;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Agro360.Web")
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture));

builder.Services.AddRazorPages();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; style-src 'self' 'unsafe-inline'; script-src 'self'; img-src 'self' data:; connect-src 'self' http://localhost:8081 https://localhost:7081; font-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    await next().ConfigureAwait(false);
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapHealthChecks("/health");
app.MapRazorPages();
foreach (var route in new[] { "/Tasks", "/Tasks/Dashboard", "/Tasks/New", "/Tasks/Details", "/Alerts", "/Alerts/Details", "/Rules", "/Rules/New", "/Rules/Edit", "/Workflows", "/Workflows/Details", "/Workflows/Decision", "/Notifications", "/Calendar/Operational", "/Outbox" })
{
    app.MapGet(route, (string? id) => Results.Redirect($"/Work?view={Uri.EscapeDataString(route)}{(string.IsNullOrWhiteSpace(id) ? "" : $"&id={Uri.EscapeDataString(id)}")}"));
}

await app.RunAsync();
