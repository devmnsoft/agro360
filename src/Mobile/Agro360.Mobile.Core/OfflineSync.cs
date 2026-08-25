using Agro360.SharedKernel;

namespace Agro360.Mobile.Core;

public enum SyncState
{
    Local = 1,
    Pending = 2,
    Synchronizing = 3,
    Synchronized = 4,
    Conflict = 5,
    Error = 6
}

public enum ConflictStrategy
{
    AutomaticMerge = 1,
    ServerWins = 2,
    ClientWins = 3,
    HumanReview = 4
}

public sealed record LocalOutboxItem(
    Guid Id,
    Guid TenantId,
    Guid DeviceId,
    string EntityType,
    Guid EntityId,
    string Operation,
    string Payload,
    long BaseVersion,
    DateTimeOffset OccurredAt,
    SyncState State,
    int Attempts,
    string? LastError);

public sealed record SyncConflict(
    Guid EntityId,
    string EntityType,
    long ClientVersion,
    long ServerVersion,
    string ClientPayload,
    string ServerPayload,
    ConflictStrategy Strategy,
    string Reason);

public interface ILocalOutbox
{
    Task EnqueueAsync(LocalOutboxItem item, CancellationToken cancellationToken);

    Task<IReadOnlyCollection<LocalOutboxItem>> GetPendingAsync(int limit, CancellationToken cancellationToken);

    Task MarkSynchronizedAsync(Guid id, long serverVersion, CancellationToken cancellationToken);

    Task MarkConflictAsync(Guid id, SyncConflict conflict, CancellationToken cancellationToken);
}

public interface IConflictPolicy
{
    ConflictStrategy Resolve(string entityType, string operation);
}

public sealed class AgroConflictPolicy : IConflictPolicy
{
    private static readonly HashSet<string> StrictEntities = new(StringComparer.OrdinalIgnoreCase)
    {
        "StockMovement",
        "StockBalance",
        "Payment",
        "Receivable",
        "AnimalTreatment",
        "Permission"
    };

    public ConflictStrategy Resolve(string entityType, string operation)
    {
        Guard.Required(entityType, nameof(entityType), 80);
        Guard.Required(operation, nameof(operation), 40);
        return StrictEntities.Contains(entityType)
            ? ConflictStrategy.HumanReview
            : operation.Equals("append", StringComparison.OrdinalIgnoreCase)
                ? ConflictStrategy.AutomaticMerge
                : ConflictStrategy.ServerWins;
    }
}
