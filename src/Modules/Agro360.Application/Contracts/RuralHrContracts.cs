using System.ComponentModel.DataAnnotations;

namespace Agro360.Application.Contracts;

public sealed record RuralHrRecord(Guid Id, string Kind, string Name, string Status, Guid? PersonId, Guid? TeamId, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, decimal Amount, DateTimeOffset UpdatedAt, string? CostState = null, Guid? AllocationId = null, string? BlockReason = null, string? ReviewStatus = null);
public sealed record RuralHrCommand([Required, MaxLength(40)] string Kind, [Required, MaxLength(180)] string Name, Guid? PersonId, Guid? TeamId, Guid? PropertyId, Guid? ResourceId, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, [Range(0, double.MaxValue)] decimal Amount = 0, [MaxLength(2000)] string? Notes = null);
public sealed record PersonCommand([Required, MaxLength(180)] string Name, [Required, RegularExpression("^[0-9]{11,14}$")] string Document, [Required] Guid RoleId, [Required] Guid PropertyId, [EmailAddress] string? Email, [Phone] string? Phone, [MaxLength(1000)] string? Skills);
public sealed record TimeEntryCommand([Required] Guid PersonId, Guid? TeamId, [Required] Guid PropertyId, Guid? ResourceId, [Required] DateTimeOffset StartedAt, DateTimeOffset? EndedAt, [Range(0, 1440)] int BreakMinutes, [Required, MaxLength(40)] string ActivityType, [MaxLength(1000)] string? Notes = null, string? OfflineId = null, Guid? AllocationId = null, [MaxLength(160)] string? IdempotencyKey = null, decimal? PieceQuantity = null);
public sealed record TransportCommand([Required, MaxLength(180)] string Name, [Required] Guid TeamId, [Required] Guid VehicleId, [Required] Guid DriverId, [Range(1, 500)] int Capacity, [Range(1, 500)] int PassengerCount, DateTimeOffset StartsAt, DateTimeOffset EndsAt, [MaxLength(500)] string? Route);
public sealed record RuralHrDashboard(int ActivePeople, int ActiveTeams, decimal WorkedHours, decimal LaborCost, int ExpiredTrainings, int ExpiredPpe, int OpenIncidents, int OverdueActions, int TeamsInField, int CriticalAlerts);

public sealed record RuralHrStatusChangeCommand([Required] string Status, [MaxLength(1000)] string? Reason = null, string? Justification = null);

public sealed record EndTimeRequest(DateTimeOffset? EndedAt = null, string? Justification = null);

public sealed record RuralHrGenericRecordCommand(
    [Required, MaxLength(40)] string Kind,
    [Required, MaxLength(180)] string Name,
    Guid? PersonId = null,
    Guid? TeamId = null,
    Guid? PropertyId = null,
    Guid? ResourceId = null,
    DateTimeOffset? StartsAt = null,
    DateTimeOffset? StartedAt = null,
    DateTimeOffset? EndsAt = null,
    DateTimeOffset? EndedAt = null,
    [Range(0, double.MaxValue)] decimal Amount = 0,
    [MaxLength(2000)] string? Notes = null,
    string? Status = null,
    string? Role = null,
    string? ActivityType = null,
    [Range(0, 1440)] int BreakMinutes = 0,
    Guid? OrderId = null,
    Guid? SeasonId = null,
    Guid? PlotId = null,
    string? RateType = null,
    decimal? RateValue = null,
    [MaxLength(14)] string? Document = null,
    [MaxLength(160)] string? IdempotencyKey = null,
    decimal? PieceQuantity = null
);

public sealed record RuralHrAllocationCommand(
    [Required, MaxLength(180)] string Name,
    Guid? PersonId,
    Guid? TeamId,
    [Required] Guid PropertyId,
    Guid? PlotId,
    Guid? SeasonId,
    Guid? OrderId,
    [Required, MaxLength(60)] string ActivityType,
    [Required] DateTimeOffset StartsAt,
    [Required] DateTimeOffset EndsAt,
    [MaxLength(2000)] string? Notes,
    [Required, MaxLength(160)] string IdempotencyKey,
    [MaxLength(1000)] string? Reason = null);

public sealed record RuralHrTariffCommand(
    Guid? RoleId,
    [MaxLength(60)] string? ActivityType,
    [Required, MaxLength(30)] string RateType,
    [Range(typeof(decimal), "0", "999999.9999")] decimal RateValue,
    [Required] DateOnly ValidFrom,
    DateOnly? ValidTo,
    [Required, MaxLength(160)] string IdempotencyKey);

public sealed record RuralHrTariff(
    Guid Id, Guid? RoleId, string? RoleName, string? ActivityType, string RateType, decimal RateValue,
    string UnitCode, DateOnly ValidFrom, DateOnly? ValidTo, bool Active);

public sealed record RuralHrTimeCorrectionCommand(
    DateTimeOffset? EndedAt,
    [Range(0, 1440)] int BreakMinutes,
    decimal? PieceQuantity,
    [Required, MinLength(5), MaxLength(1000)] string Justification);

public sealed record RuralHrAppropriationResult(Guid? EntryId, Guid? BatchId, string State, string Message);
public sealed record RuralHrIdempotencyCommand([Required, MaxLength(160)] string IdempotencyKey);

public sealed record RuralHrBoardQuery(DateOnly? From = null, DateOnly? To = null, Guid? PropertyId = null, Guid? SeasonId = null, Guid? TeamId = null, string? Situation = null, int Page = 1, int PageSize = 25);

public sealed record RuralHrBoardMetric(string Code, string Label, int? Count, decimal? Amount, string AmountState);

public sealed record RuralHrOperationsBoard(IReadOnlyList<RuralHrBoardMetric> Metrics, IReadOnlyList<RuralHrRecord> Items, int Page, int PageSize, int Total);

public sealed record RuralHrDataReview(Guid Id, string EntityTable, Guid EntityId, string ReasonCode, string Detail, string Resolution, DateTimeOffset DetectedAt);

public sealed record RuralHrProjectionIssue(string Kind, Guid RecordId, string Issue);

public interface IRuralHrService
{
    Task<IReadOnlyList<RuralHrRecord>> ListAsync(string kind, string? status, CancellationToken ct);
    Task<Guid> SaveAsync(Guid? id, RuralHrCommand command, CancellationToken ct);
    Task<Guid> SaveGenericAsync(Guid? id, RuralHrGenericRecordCommand command, CancellationToken ct);
    Task<Guid> AddPersonAsync(PersonCommand command, CancellationToken ct);
    Task<Guid> RegisterTimeAsync(TimeEntryCommand command, CancellationToken ct);
    Task EndTimeAsync(Guid id, DateTimeOffset endedAt, CancellationToken ct);
    Task EndTimeAsync(Guid id, DateTimeOffset? endedAt, string? justification, CancellationToken ct);
    Task<Guid> AddTransportAsync(TransportCommand command, CancellationToken ct);
    Task ChangeStatusAsync(Guid id, string kind, string status, CancellationToken ct);
    Task ChangeStatusAsync(Guid id, string status, CancellationToken ct);
    Task<IReadOnlyList<LookupOption>> LookupAsync(string kind, CancellationToken ct);
    Task<IReadOnlyList<LookupOption>> LookupAsync(string kind, Guid? scopeId, CancellationToken ct);
    Task<RuralHrDashboard> DashboardAsync(CancellationToken ct);
    Task<byte[]> ExportAsync(string kind, CancellationToken ct);
    Task<Guid> SaveAllocationAsync(Guid? id, RuralHrAllocationCommand command, CancellationToken ct);
    Task CancelAllocationAsync(Guid id, string reason, CancellationToken ct);
    Task ConfirmTimeAsync(Guid id, CancellationToken ct);
    Task CorrectTimeAsync(Guid id, RuralHrTimeCorrectionCommand command, CancellationToken ct);
    Task<Guid> SaveTariffAsync(RuralHrTariffCommand command, CancellationToken ct);
    Task<IReadOnlyList<RuralHrTariff>> ListTariffsAsync(CancellationToken ct);
    Task<RuralHrAppropriationResult> AppropriateLaborAsync(Guid timeEntryId, string idempotencyKey, CancellationToken ct);
    Task<RuralHrOperationsBoard> OperationsBoardAsync(RuralHrBoardQuery query, CancellationToken ct);
    Task<IReadOnlyList<RuralHrDataReview>> ListDataReviewsAsync(CancellationToken ct);
    Task<IReadOnlyList<RuralHrProjectionIssue>> ProjectionIssuesAsync(CancellationToken ct);
}

