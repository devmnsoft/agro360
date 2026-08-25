using Agro360.SharedKernel;

namespace Agro360.Application.Contracts;

public sealed record LookupItem(Guid Id, string Label, string Description, string Status, IReadOnlyDictionary<string, object?> Metadata);

public sealed record AgricultureRecord(
    Guid Id, string Module, string Status, DateTimeOffset CreatedAt, IReadOnlyDictionary<string, object?> Data);

public sealed record AgricultureCommand(
    Guid? PropertyId,
    Guid? FieldId,
    Guid? CropSeasonId,
    Guid? CropId,
    Guid? ResponsibleId,
    Guid? MachineId,
    Guid? InventoryItemId,
    Guid? CostCenterId,
    string? Name,
    string? Type,
    string? Severity,
    string? Status,
    DateTimeOffset? PlannedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? FinishedAt,
    decimal? Area,
    decimal? Quantity,
    decimal? Dose,
    decimal? EstimatedCost,
    decimal? Rainfall,
    decimal? Temperature,
    decimal? Humidity,
    decimal? Wind,
    bool ChecklistRequired,
    bool ChecklistCompleted,
    string? Notes,
    string? CancellationReason,
    IReadOnlyCollection<Guid>? FieldIds);

public sealed record AgricultureDashboard(
    long ActiveSeasons, decimal PlantedArea, long PlannedActivities, long OverdueActivities,
    long CompletedThisMonth, decimal PlannedCost, decimal ActualCost, decimal CostPerHectare,
    long OpenOccurrences, long CriticalOccurrences, decimal PlannedInputs, decimal ActualInputs,
    long Applications, long Irrigations, decimal ExpectedYield, decimal ActualYield,
    IReadOnlyCollection<string> Alerts);

public interface ILookupService
{
    Task<PagedResult<LookupItem>> SearchAsync(string resource, string? search, bool includeInactive, int page, int pageSize, CancellationToken cancellationToken);
}

public interface IAgriculture360Service
{
    Task<PagedResult<AgricultureRecord>> ListAsync(string module, int page, int pageSize, CancellationToken cancellationToken);
    Task<AgricultureRecord> CreateAsync(string module, AgricultureCommand command, CancellationToken cancellationToken);
    Task<AgricultureRecord> UpdateAsync(string module, Guid id, AgricultureCommand command, CancellationToken cancellationToken);
    Task<AgricultureRecord> TransitionAsync(string module, Guid id, string action, AgricultureCommand? command, CancellationToken cancellationToken);
    Task<AgricultureDashboard> DashboardAsync(CancellationToken cancellationToken);
}
