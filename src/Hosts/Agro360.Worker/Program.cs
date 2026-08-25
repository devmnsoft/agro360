using System.Globalization;
using Agro360.Infrastructure;
using Agro360.Worker;
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

builder.Services.AddAgro360Infrastructure(builder.Configuration);
builder.Services.AddSingleton<IOutboxPublisher, LoggingOutboxPublisher>();
builder.Services.AddHostedService<OutboxWorker>();

var host = builder.Build();
await host.RunAsync();
