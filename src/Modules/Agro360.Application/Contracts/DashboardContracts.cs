namespace Agro360.Application.Contracts;

public sealed record DashboardKpis(
    int Farms,
    decimal TotalAreaHa,
    int ActiveSeasons,
    int ActiveAnimals,
    decimal InventoryValue,
    decimal Receivables,
    decimal OperationalCosts,
    decimal EstimatedMargin,
    int CriticalAlerts,
    DateTimeOffset GeneratedAt);

public sealed record RecentOperation(
    Guid Id,
    string Module,
    string Type,
    string Description,
    decimal? Amount,
    DateTimeOffset OccurredAt,
    string Status);

public sealed record CommandCenterResult(DashboardKpis Kpis, IReadOnlyCollection<RecentOperation> RecentOperations);

public interface IDashboardService
{
    Task<CommandCenterResult> GetCommandCenterAsync(Guid? farmId, CancellationToken cancellationToken);
}
