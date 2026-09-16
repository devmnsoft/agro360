namespace Agro360.Application.Contracts;

public sealed record PlanOperationCommand(Guid SeasonId, Guid FarmId, Guid FieldId, string Name, string OperationType,
    DateTimeOffset PlannedStart, DateTimeOffset PlannedEnd, decimal PlannedAreaHa, decimal? PlannedHours,
    Guid? ResponsibleId, string? OutsideSeasonReason);
public sealed record OperationDependencyCommand(Guid PredecessorId, string BlockingType, string ReleaseCondition);
public sealed record GenerateOperationOrderCommand(decimal CoveredAreaHa, string IdempotencyKey);
public sealed record RescheduleOperationCommand(DateTimeOffset PlannedStart, DateTimeOffset PlannedEnd,
    decimal PlannedAreaHa, Guid? ResponsibleId, string Reason, long Version);
public sealed record PlanOperationDto(Guid Id, Guid PlanRecordId, Guid SeasonId, Guid FarmId, Guid FieldId,
    string Name, string OperationType, DateTimeOffset PlannedStart, DateTimeOffset PlannedEnd,
    decimal PlannedAreaHa, decimal? PlannedHours, Guid? ResponsibleId, string Status, long Version,
    decimal ExecutedAreaHa, decimal ScheduledAreaHa, decimal UnscheduledAreaHa, string? BlockReason, string? BlockUrl);
public sealed record SeasonMetric(string Code, string Label, decimal? Value, string? Unit, string Availability,
    string Definition, string SourceUrl, string? Explanation);
public sealed record SeasonPending(string Origin, string Reason, DateTimeOffset? ReferenceDate, string? Responsible,
    string Impact, string NextAction, string ContextUrl, string Severity);
public sealed record SeasonTrackingOverview(Guid SeasonId, Guid FarmId, string Farm, string Season, string Crop,
    DateOnly StartsOn, DateOnly EndsOn, string Status, decimal PlannedPhysicalAreaHa,
    IReadOnlyCollection<dynamic> Fields, IReadOnlyCollection<PlanOperationDto> Operations,
    IReadOnlyCollection<SeasonMetric> Metrics, IReadOnlyCollection<SeasonPending> Pending,
    IReadOnlyCollection<dynamic> Materials, IReadOnlyCollection<dynamic> Costs,
    IReadOnlyCollection<dynamic> Production, IReadOnlyCollection<dynamic> History);

public interface ISeasonTrackingService
{
    Task<SeasonTrackingOverview> OverviewAsync(Guid seasonId, DateOnly? referenceDate, CancellationToken ct);
    Task<PlanOperationDto> AddOperationAsync(Guid planId, PlanOperationCommand command, CancellationToken ct);
    Task AddDependencyAsync(Guid operationId, OperationDependencyCommand command, CancellationToken ct);
    Task<Guid> GenerateOrderAsync(Guid operationId, GenerateOperationOrderCommand command, CancellationToken ct);
    Task<PlanOperationDto> RescheduleAsync(Guid operationId, RescheduleOperationCommand command, CancellationToken ct);
}
