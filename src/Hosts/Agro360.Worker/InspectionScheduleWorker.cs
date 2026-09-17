using Agro360.Application.Abstractions;
using Agro360.Application.Contracts;
using Dapper;
using Npgsql;

namespace Agro360.Worker;

/// <summary>
/// Polls due inspection schedules per active tenant.
/// Uses IInspectionService.GenerateDueSchedulesForTenantAsync (explicit tenant_id via DatabaseExecutor)
/// because request-scoped ITenantContext is not available in the worker host.
/// </summary>
public sealed partial class InspectionScheduleWorker(
    IServiceScopeFactory scopeFactory,
    IDbConnectionFactory connectionFactory,
    ILogger<InspectionScheduleWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessAllTenantsAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                LogCycleFailure(logger, exception);
            }

            await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessAllTenantsAsync(CancellationToken cancellationToken)
    {
        await using var connection = (NpgsqlConnection)await connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        var tenants = (await connection.QueryAsync<Guid>(new CommandDefinition(
            "select id from agro360.tenancy_tenants where status in (1, 2) order by created_at;",
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        var asOf = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var tenantId in tenants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var inspections = scope.ServiceProvider.GetRequiredService<IInspectionService>();
                var result = await inspections
                    .GenerateDueSchedulesForTenantAsync(tenantId, asOf, cancellationToken)
                    .ConfigureAwait(false);
                if (result.GeneratedRuns > 0 || result.SkippedExisting > 0)
                    LogGenerated(logger, tenantId, result.GeneratedRuns, result.SkippedExisting);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogTenantFailure(logger, tenantId, exception);
            }
        }
    }

    [LoggerMessage(EventId = 2101, Level = LogLevel.Error, Message = "Falha no ciclo do InspectionScheduleWorker.")]
    private static partial void LogCycleFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Warning, Message = "Falha ao gerar agendas de inspeção. Tenant={TenantId}")]
    private static partial void LogTenantFailure(ILogger logger, Guid tenantId, Exception exception);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Information,
        Message = "Agendas de inspeção processadas. Tenant={TenantId} Generated={Generated} Skipped={Skipped}")]
    private static partial void LogGenerated(ILogger logger, Guid tenantId, int generated, int skipped);
}
