using System.Text.Json;
using Agro360.Application.Abstractions;
using Dapper;
using Npgsql;

namespace Agro360.Worker;

public sealed record OutboxEnvelope(
    Guid Id,
    Guid TenantId,
    string EventType,
    Guid AggregateId,
    string Payload,
    DateTimeOffset OccurredAt,
    int Attempts);

public interface IOutboxPublisher
{
    Task PublishAsync(OutboxEnvelope message, CancellationToken cancellationToken);
}

public sealed partial class LoggingOutboxPublisher(ILogger<LoggingOutboxPublisher> logger) : IOutboxPublisher
{
    public Task PublishAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        using var payload = JsonDocument.Parse(message.Payload);
        if (logger.IsEnabled(LogLevel.Information))
        {
            var payloadText = payload.RootElement.GetRawText();
            LogPublished(
                logger,
                message.EventType,
                message.TenantId,
                message.AggregateId,
                payloadText);
        }

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Evento {EventType} publicado para o barramento local. Tenant={TenantId} Aggregate={AggregateId} Payload={Payload}")]
    private static partial void LogPublished(
        ILogger logger,
        string eventType,
        Guid tenantId,
        Guid aggregateId,
        string payload);
}

public sealed partial class OutboxWorker(
    IDbConnectionFactory connectionFactory,
    IOutboxPublisher publisher,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

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
                LogWorkerCycleFailure(logger, exception);
            }

            await Task.Delay(Interval, stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessAllTenantsAsync(CancellationToken cancellationToken)
    {
        await using var connection = (NpgsqlConnection)await connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        var tenants = (await connection.QueryAsync<Guid>(new CommandDefinition(
            "select id from tenancy.tenants where status in (1, 2) order by created_at;",
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        foreach (var tenantId in tenants)
        {
            await ProcessTenantAsync(tenantId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task ProcessTenantAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        await using var connection = (NpgsqlConnection)await connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "select set_config('app.tenant_id', @TenantId, true);",
                new { TenantId = tenantId.ToString() },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            var messages = (await connection.QueryAsync<OutboxEnvelope>(new CommandDefinition(
                """
                select id, tenant_id as TenantId, event_type as EventType,
                       aggregate_id as AggregateId, payload::text as Payload,
                       occurred_at as OccurredAt, attempts
                from platform.outbox_messages
                where tenant_id = @TenantId and processed_at is null and next_attempt_at <= now()
                order by occurred_at
                limit 50
                for update skip locked;
                """,
                new { TenantId = tenantId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

            foreach (var message in messages)
            {
                try
                {
                    await publisher.PublishAsync(message, cancellationToken).ConfigureAwait(false);
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        update platform.outbox_messages
                        set processed_at = now(), attempts = attempts + 1, last_error = null
                        where id = @Id and tenant_id = @TenantId;
                        """,
                        new { message.Id, message.TenantId },
                        transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogPublishFailure(logger, message.Id, exception);
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        update platform.outbox_messages
                        set attempts = attempts + 1,
                            last_error = left(@Error, 2000),
                            next_attempt_at = now() + make_interval(secs => least(3600, power(2, least(attempts, 10))::int))
                        where id = @Id and tenant_id = @TenantId;
                        """,
                        new { message.Id, message.TenantId, Error = exception.Message },
                        transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
                }
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error, Message = "Falha no ciclo do Outbox Worker.")]
    private static partial void LogWorkerCycleFailure(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Warning, Message = "Falha ao publicar evento {OutboxId}.")]
    private static partial void LogPublishFailure(ILogger logger, Guid outboxId, Exception exception);
}
