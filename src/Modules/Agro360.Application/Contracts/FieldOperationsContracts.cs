namespace Agro360.Application.Contracts;

public sealed record FieldResourceCommand(string ResourceType, Guid ResourceId, DateTimeOffset StartsAt, DateTimeOffset EndsAt);
public sealed record FieldWorkLogCommand(Guid OperatorId, Guid? EquipmentId, string Stage, DateTimeOffset StartsAt,
    DateTimeOffset EndsAt, decimal PerformedQuantity, string Unit, decimal? PhysicalAreaHa, decimal? InitialMeter,
    decimal? FinalMeter, int InterruptionMinutes, string? InterruptionReason, string? Notes,
    string? EvidenceReference, string IdempotencyKey);
public sealed record FieldMaterialCommand(Guid ProductId, Guid? WarehouseId, string Unit, decimal PlannedQuantity, decimal? UnitCost);
public sealed record FieldMaterialEventCommand(string EventType, decimal Quantity, string? Reason, Guid? SourceEventId, string IdempotencyKey);
public sealed record FieldReviewCommand(long Version, string? Notes);
public sealed record FieldIssue(string Severity, string Code, string Message, string Action);
public sealed record FieldCostSummary(decimal? Planned, decimal? ActualMaterials, decimal? Labor, decimal? Equipment,
    decimal? Services, decimal? Losses, decimal? Actual, decimal? Variation, IReadOnlyCollection<string> PendingValues);
public sealed record FieldOrderDetail(AgricultureRecord Order, long Version, IReadOnlyCollection<dynamic> Resources,
    IReadOnlyCollection<dynamic> WorkLogs, IReadOnlyCollection<dynamic> Materials, IReadOnlyCollection<dynamic> History,
    IReadOnlyCollection<FieldIssue> Issues, FieldCostSummary Costs);

public interface IFieldOperationsService
{
    Task<FieldOrderDetail> DetailAsync(Guid orderId, CancellationToken cancellationToken);
    Task AddResourceAsync(Guid orderId, FieldResourceCommand command, CancellationToken cancellationToken);
    Task AddWorkLogAsync(Guid orderId, FieldWorkLogCommand command, CancellationToken cancellationToken);
    Task AddMaterialAsync(Guid orderId, FieldMaterialCommand command, CancellationToken cancellationToken);
    Task ApplyMaterialEventAsync(Guid orderId, Guid materialId, FieldMaterialEventCommand command, CancellationToken cancellationToken);
    Task ReviewAsync(Guid orderId, FieldReviewCommand command, CancellationToken cancellationToken);
}
