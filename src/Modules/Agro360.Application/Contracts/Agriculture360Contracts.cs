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
    IReadOnlyCollection<Guid>? FieldIds,
    string? Priority = null,
    string? Origin = null,
    string? Unit = null,
    string? PauseReason = null,
    string? ReopenReason = null,
    long? Version = null,
    Guid? WarehouseId = null,
    string? InventoryLot = null);

// Cada indicador distingue planejado x realizado x não calculável: valor nulo significa "sem dados compatíveis" (nunca zero).
public sealed record AgricultureDashboard(
    long ActiveSeasons, decimal PlannedAreaHa, long PlannedActivities, long OverdueActivities,
    long CompletedThisMonth, decimal? PlannedCost, decimal? RecognizedCost, decimal? CostPerHectare,
    long OpenOccurrences, long CriticalOccurrences, IReadOnlyCollection<AgricultureInputUnit> InputsByUnit,
    long Applications, long Irrigations, decimal? ExpectedProduction, decimal? ActualProduction,
    IReadOnlyCollection<string> Alerts);

// Total físico por unidade base do produto: unidades diferentes nunca são somadas entre si.
public sealed class AgricultureInputUnit
{
    public string Unit { get; init; } = default!;
    public decimal Planned { get; init; }
    public decimal Consumed { get; init; }
}

public interface ILookupService
{
    Task<PagedResult<LookupItem>> SearchAsync(string resource, string? search, bool includeInactive, int page, int pageSize, CancellationToken cancellationToken);
}

public interface IAgriculture360Service
{
    Task<PagedResult<AgricultureRecord>> ListAsync(string moduleCode, int page, int pageSize, CancellationToken cancellationToken);
    Task<AgricultureRecord> CreateAsync(string moduleCode, AgricultureCommand command, CancellationToken cancellationToken);
    Task<AgricultureRecord> UpdateAsync(string moduleCode, Guid id, AgricultureCommand command, CancellationToken cancellationToken);
    Task<AgricultureRecord> TransitionAsync(string moduleCode, Guid id, string action, AgricultureCommand? command, CancellationToken cancellationToken);
    Task<AgricultureDashboard> DashboardAsync(CancellationToken cancellationToken);
}
