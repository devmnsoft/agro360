using Agro360.Application.Abstractions;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Agro360.Worker;

public sealed record OutboxEnvelope(
    Guid Id,
    Guid TenantId,
    string EventType,
    Guid AggregateId,
    string Payload,
    DateTimeOffset OccurredAt,
    int Attempts,
    string? CorrelationId);

public sealed class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int BatchSize { get; init; } = 50;

    public int MaximumAttempts { get; init; } = 10;

    public TimeSpan PollInterval { get; init; } = TimeSpan.FromSeconds(5);
}

public interface IOutboxPublisher
{
    Task PublishAsync(OutboxEnvelope message, CancellationToken cancellationToken);
}

public sealed partial class LoggingOutboxPublisher(ILogger<LoggingOutboxPublisher> logger) : IOutboxPublisher
{
    public Task PublishAsync(OutboxEnvelope message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        LogPublished(logger, message.Id, message.EventType, message.TenantId, message.AggregateId, message.CorrelationId);

        return Task.CompletedTask;
    }

    [LoggerMessage(
        EventId = 2001,
        Level = LogLevel.Information,
        Message = "Evento Outbox {OutboxId} ({EventType}) publicado. Tenant={TenantId} Aggregate={AggregateId} CorrelationId={CorrelationId}")]
    private static partial void LogPublished(
        ILogger logger,
        Guid outboxId,
        string eventType,
        Guid tenantId,
        Guid aggregateId,
        string? correlationId);
}

public sealed partial class OutboxWorker(
    IDbConnectionFactory connectionFactory,
    IOutboxPublisher publisher,
    IOptions<OutboxOptions> options,
    ILogger<OutboxWorker> logger) : BackgroundService
{
    private readonly OutboxOptions _options = Validate(options.Value);

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

            await Task.Delay(_options.PollInterval, stoppingToken).ConfigureAwait(false);
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
                       occurred_at as OccurredAt, attempts, correlation_id as CorrelationId
                from platform.outbox_messages
                where tenant_id = @TenantId
                  and processed_at is null
                  and dead_lettered_at is null
                  and next_attempt_at <= now()
                order by occurred_at
                limit @BatchSize
                for update skip locked;
                """,
                new { TenantId = tenantId, _options.BatchSize },
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
                        set processed_at = now(), attempts = attempts + 1,
                            last_attempt_at = now(), last_error = null
                        where id = @Id and tenant_id = @TenantId;
                        """,
                        new { message.Id, message.TenantId },
                        transaction,
                        cancellationToken: cancellationToken)).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogPublishFailure(logger, message.Id, message.TenantId, message.Attempts + 1, exception.GetType().Name);
                    await connection.ExecuteAsync(new CommandDefinition(
                        """
                        update platform.outbox_messages
                        set attempts = attempts + 1,
                            last_attempt_at = now(),
                            last_error = left(@Error, 2000),
                            dead_lettered_at = case
                                when attempts + 1 >= @MaximumAttempts then now()
                                else dead_lettered_at
                            end,
                            next_attempt_at = now() + make_interval(secs => least(3600, power(2, least(attempts, 10))::int))
                        where id = @Id and tenant_id = @TenantId;
                        """,
                        new
                        {
                            message.Id,
                            message.TenantId,
                            Error = exception.GetType().Name,
                            _options.MaximumAttempts
                        },
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

    private static OutboxOptions Validate(OutboxOptions options)
    {
        if (options.BatchSize is < 1 or > 500)
        {
            throw new InvalidOperationException("Outbox:BatchSize deve estar entre 1 e 500.");
        }

        if (options.MaximumAttempts is < 1 or > 100)
        {
            throw new InvalidOperationException("Outbox:MaximumAttempts deve estar entre 1 e 100.");
        }

        if (options.PollInterval < TimeSpan.FromMilliseconds(250))
        {
            throw new InvalidOperationException("Outbox:PollInterval deve ser de pelo menos 250 ms.");
        }

        return options;
    }

    [LoggerMessage(EventId = 2002, Level = LogLevel.Error, Message = "Falha no ciclo do Outbox Worker.")]
    private static partial void LogWorkerCycleFailure(ILogger logger, Exception exception);

    [LoggerMessage(
        EventId = 2003,
        Level = LogLevel.Warning,
        Message = "Falha ao publicar evento {OutboxId}. Tenant={TenantId} Attempt={Attempt} ErrorType={ErrorType}")]
    private static partial void LogPublishFailure(
        ILogger logger,
        Guid outboxId,
        Guid tenantId,
        int attempt,
        string errorType);
}
