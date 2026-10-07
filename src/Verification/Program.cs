using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Agro360.Infrastructure;
using Agro360.Infrastructure.Services;
using Agro360.Multitenancy;
using Microsoft.AspNetCore.DataProtection;

Console.WriteLine("=== Agro360 Cycle Verification Harness ===");

var builder = Host.CreateApplicationBuilder(args);

// Set environment to Development to bypass production security checks on connection strings
builder.Environment.EnvironmentName = "Development";

// Setup configuration
builder.Configuration.AddJsonFile("appsettings.json", optional: false);

builder.Services.AddAgro360Infrastructure(builder.Configuration);
builder.Services.AddDataProtection();

// Mock TenantContext for verification
builder.Services.AddScoped<ITenantContext>(_ => new MockTenantContext());

using var host = builder.Build();
var services = host.Services;

try
{
    using var scope = services.CreateScope();
    var verificationService = scope.ServiceProvider.GetRequiredService<ICycleVerificationService>();
    Console.WriteLine("Running Full Cycle Verification...");

    var result = await verificationService.RunFullCycleAsync();

    Console.WriteLine($"\nOverall Result: {(result.OverallSuccess ? "SUCCESS ✅" : "FAILED ❌")}");
    Console.WriteLine("\n--- Step Details ---");
    foreach (var step in result.Steps)
    {
        var icon = step.Success ? "✅" : "❌";
        Console.WriteLine($"{icon} {step.Name}: {step.Detail}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"\nCRITICAL ERROR: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}

Console.WriteLine("\nVerification Complete.");

public class MockTenantContext : ITenantContext
{
    public Guid TenantId { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000001");
    public Guid UserId { get; set; } = Guid.Parse("00000000-0000-0000-0000-000000000002");
    public Guid? OrganizationId { get; set; }
    public Guid? FarmId { get; set; }
    public string TimeZoneId { get; set; } = "America/Belem";
    public bool IsAvailable { get; set; } = true;
}
