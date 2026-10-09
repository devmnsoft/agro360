using Agro360.Application.Abstractions;
using Agro360.Infrastructure.Services.Ai;
using Dapper;
using Npgsql;

namespace Agro360.Worker;

/// <summary>
/// Libera reservas de cota de IA abandonadas (executions presas em RESERVED/IN_PROGRESS quando o
/// request morreu entre a reserva e a reconciliação). Sem esta varredura o saldo reservado consome
/// silenciosamente a cota contratada para sempre. Segue a convenção dos workers do host: varredura
/// por tenant ativo com tenant_id explícito (ITenantContext de requisição não existe aqui), e cada
/// inquilino é isolado no seu próprio escopo RLS. A IA nunca decide nada: isto apenas devolve ao
/// saldo autorizado aquilo que nenhuma execução chegou a consumir.
/// </summary>
public sealed partial class AiQuotaCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IDbConnectionFactory connectionFactory,
    ILogger<AiQuotaCleanupWorker> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    /// <summary>Reservas mais jovens que isto são consideradas execuções em voo, não abandono.</summary>
    private static readonly TimeSpan AbandonAfter = TimeSpan.FromMinutes(15);

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

        foreach (var tenantId in tenants)
        {
            // Um tenant com falha transitória não pode travar a liberação dos demais; a próxima
            // varredura reaproveita a idempotência da baixa (status já liberado não é reprocessado).
            try
            {
                using var scope = scopeFactory.CreateScope();
                var aiQuota = scope.ServiceProvider.GetRequiredService<IAiQuotaService>();
                var released = await aiQuota
                    .CleanupAbandonedReservationsAsync(tenantId, AbandonAfter, cancellationToken)
                    .ConfigureAwait(false);
                if (released > 0)
                    LogReleased(logger, tenantId, released);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogTenantFailure(logger, tenantId, exception);
            }
        }
    }

    [LoggerMessage(EventId = 2101, Level = LogLevel.Warning, Message = "IA: {Released} reserva(s) abandonada(s) liberada(s) no tenant {TenantId}.")]
    private static partial void LogReleased(ILogger logger, Guid tenantId, int released);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Error, Message = "IA: limpeza de reservas falhou no tenant {TenantId}.")]
    private static partial void LogTenantFailure(ILogger logger, Guid tenantId, Exception exception);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Error, Message = "IA: ciclo de limpeza de reservas falhou.")]
    private static partial void LogCycleFailure(ILogger logger, Exception exception);
}
