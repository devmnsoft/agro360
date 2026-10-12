using Agro360.Application.Abstractions;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Services;
using Agro360.Multitenancy;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Agro360.Worker;

/// <summary>
/// Recuperação automática de inspeções por evento operacional. Por tenant ativo:
/// (1) adota origens confirmadas sem intent (crash entre commit da origem e criação do intent);
/// (2) claima intents PENDING/PENDING_MODEL vencidos (lease + SKIP LOCKED) e processa cada um com
///     o contexto do tenant/ator original — sem reenviar o comando original.
/// Crash durante o processamento: o lease expira e outro ciclo/instância retoma (idempotência pela
/// chave do run garante ausência de duplicidade). Reinício não perde trabalho: tudo é durável.
/// </summary>
public sealed partial class InspectionIntentRecoveryWorker(
    IServiceScopeFactory scopeFactory,
    IDbConnectionFactory connectionFactory,
    IOptions<InspectionIntentsOptions> options,
    ILogger<InspectionIntentRecoveryWorker> logger) : BackgroundService
{
    private readonly InspectionIntentsOptions _options = Validate(options.Value);

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

            await Task.Delay(TimeSpan.FromSeconds(_options.PollSeconds), stoppingToken).ConfigureAwait(false);
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

        foreach (var tenantId in tenants)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await ProcessTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogTenantFailure(logger, tenantId, exception);
            }
        }
    }

    private async Task ProcessTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var recovery = scope.ServiceProvider.GetRequiredService<IInspectionIntentRecoveryService>();

        var adopted = await recovery.AdoptMissingOriginIntentsForTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        var claimed = await recovery.ClaimDueIntentsForTenantAsync(tenantId, _options.BatchSize, cancellationToken).ConfigureAwait(false);

        var started = 0;
        var settled = 0;
        var failed = 0;

        foreach (var item in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (item.ActorId is null)
                {
                    // Sem operador vinculado ao evento: estado terminal honesto, sem loop.
                    await recovery.MarkSkippedNoActorAsync(tenantId, item.IntentId, cancellationToken).ConfigureAwait(false);
                    settled++;
                    continue;
                }

                // Contexto de tenant + ator original para o processamento (start de run, auditoria).
                await using var itemScope = scopeFactory.CreateAsyncScope();
                itemScope.ServiceProvider.GetRequiredService<IMutableTenantContext>()
                    .SetScope(new TenantScope(tenantId, item.ActorId.Value, null, null));

                var processor = itemScope.ServiceProvider.GetRequiredService<IInspectionIntentProcessor>();
                var result = await processor.ProcessIntentAsync(item.IntentId, cancellationToken).ConfigureAwait(false);

                if (result.Status == "STARTED") started++;
                else settled++;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Exceção que escapou do processador: registra tentativas/backoff/dead-letter
                // (mesmas semânticas do outbox) para o próximo ciclo ou para re-disparo manual.
                LogIntentFailure(logger, tenantId, item.IntentId, exception.GetType().Name);
                try
                {
                    await recovery.MarkProcessingFailureAsync(tenantId, item.IntentId, exception, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception markerFailure)
                {
                    LogFailureMarkerLost(logger, tenantId, item.IntentId, markerFailure);
                }
                failed++;
            }
        }

        if (adopted > 0 || claimed.Count > 0)
            LogCycleResult(logger, tenantId, adopted, claimed.Count, started, settled, failed);
    }

    private static InspectionIntentsOptions Validate(InspectionIntentsOptions options)
    {
        if (options.PollSeconds < 1) throw new InvalidOperationException("InspectionIntents:PollSeconds deve ser >= 1.");
        if (options.BatchSize is < 1 or > 200) throw new InvalidOperationException("InspectionIntents:BatchSize deve estar entre 1 e 200.");
        if (options.LeaseSeconds < 30) throw new InvalidOperationException("InspectionIntents:LeaseSeconds deve ser >= 30.");
        if (options.MaximumAttempts is < 1 or > 100) throw new InvalidOperationException("InspectionIntents:MaximumAttempts deve estar entre 1 e 100.");
        return options;
    }

    [LoggerMessage(EventId = 3201, Level = LogLevel.Error, Message = "Falha no ciclo do InspectionIntentRecoveryWorker.")]
    private static partial void LogCycleFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 3202, Level = LogLevel.Warning, Message = "Falha ao recuperar intents de inspeção. Tenant={TenantId}")]
    private static partial void LogTenantFailure(ILogger logger, Guid tenantId, Exception exception);

    [LoggerMessage(EventId = 3203, Level = LogLevel.Warning, Message = "Falha ao processar intent de inspeção. Tenant={TenantId} Intent={IntentId} ErrorType={ErrorType}")]
    private static partial void LogIntentFailure(ILogger logger, Guid tenantId, Guid intentId, string errorType);

    [LoggerMessage(EventId = 3204, Level = LogLevel.Error, Message = "Falha ao registrar contagem de falha do intent (lease expirará e o ciclo retoma). Tenant={TenantId} Intent={IntentId}")]
    private static partial void LogFailureMarkerLost(ILogger logger, Guid tenantId, Guid intentId, Exception exception);

    [LoggerMessage(EventId = 3205, Level = LogLevel.Information,
        Message = "Ciclo de recuperação de inspeções. Tenant={TenantId} Adopted={Adopted} Claimed={Claimed} Started={Started} Settled={Settled} Failed={Failed}")]
    private static partial void LogCycleResult(ILogger logger, Guid tenantId, int adopted, int claimed, int started, int settled, int failed);
}
