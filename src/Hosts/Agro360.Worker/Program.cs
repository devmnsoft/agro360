using System.Globalization;
using Agro360.Infrastructure;
using Agro360.Infrastructure.Security;
using Agro360.Worker;
using Microsoft.AspNetCore.DataProtection;
using Serilog;

var builder = Host.CreateApplicationBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .Enrich.WithProperty("Application", "Agro360.Worker")
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateLogger();
builder.Logging.ClearProviders();
builder.Logging.AddSerilog(Log.Logger, dispose: true);

// DataProtection: IdentityService usa IDataProtectionProvider (protetor MFA) no construtor;
// em Development a validacao DI na Build() falha sem este registro no host worker.
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName(DataProtectionSettings.ApplicationName);
var dataProtectionKeysPath = builder.Configuration["DataProtection:KeysPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeysPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));
}

builder.Services.AddAgro360Infrastructure(builder.Configuration);
builder.Services.Configure<OutboxOptions>(builder.Configuration.GetSection(OutboxOptions.SectionName));
builder.Services.AddSingleton<IOutboxPublisher, LoggingOutboxPublisher>();
builder.Services.AddHostedService<OutboxWorker>();
builder.Services.AddHostedService<InspectionScheduleWorker>();
builder.Services.AddHostedService<AiQuotaCleanupWorker>();
builder.Services.AddHostedService<InspectionIntentRecoveryWorker>();

var host = builder.Build();
await host.RunAsync();
