using System.Text.Json;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Npgsql;

namespace Agro360.Infrastructure.Persistence;

internal static class TransactionWriters
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Task WriteAuditAsync(
        this NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        ITenantContext context,
        string action,
        string entityType,
        Guid entityId,
        object? before,
        object? after,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            insert into audit.logs
                (id, tenant_id, user_id, action, entity_type, entity_id, before_data, after_data, occurred_at)
            values
                (@Id, @TenantId, @UserId, @Action, @EntityType, @EntityId, cast(@BeforeData as jsonb), cast(@AfterData as jsonb), now());
            """,
            new
            {
                Id = Guid.CreateVersion7(),
                context.TenantId,
                context.UserId,
                Action = action,
                EntityType = entityType,
                EntityId = entityId,
                BeforeData = before is null ? null : JsonSerializer.Serialize(before, JsonOptions),
                AfterData = after is null ? null : JsonSerializer.Serialize(after, JsonOptions)
            },
            transaction,
            cancellationToken: cancellationToken));

    public static Task EnqueueAsync(
        this NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid tenantId,
        string eventType,
        Guid aggregateId,
        object payload,
        CancellationToken cancellationToken) =>
        connection.ExecuteAsync(new CommandDefinition(
            """
            insert into platform.outbox_messages
                (id, tenant_id, event_type, aggregate_id, payload, occurred_at, attempts)
            values
                (@Id, @TenantId, @EventType, @AggregateId, cast(@Payload as jsonb), now(), 0);
            """,
            new
            {
                Id = Guid.CreateVersion7(),
                TenantId = tenantId,
                EventType = eventType,
                AggregateId = aggregateId,
                Payload = JsonSerializer.Serialize(payload, JsonOptions)
            },
            transaction,
            cancellationToken: cancellationToken));
}
