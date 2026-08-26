using System.ComponentModel.DataAnnotations;

namespace Agro360.Application.Contracts;

public sealed record IntelligenceFilter(DateOnly? From, DateOnly? To, Guid? FarmId, Guid? SeasonId, string? Status);
public sealed record IndicatorResult(string Code, string Name, string Category, decimal Value, string Unit, string Explanation);
public sealed record ReportDefinition(string Id, string Name, string Category, bool SupportsSeason, bool SupportsStatus);
public sealed record ReportResult(string ReportId, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyDictionary<string, object?>> Rows, int Total);
public sealed record AlertResult(Guid Id, string Type, string Severity, string Title, string Status, DateTimeOffset DetectedAt, DateTimeOffset? SnoozedUntil);
public sealed record AlertAction([Required] Guid UserId, DateTimeOffset? Until, [StringLength(500)] string? Reason);
public sealed record ForecastResult(string Type, string Status, decimal? Value, string Unit, string Explanation, IReadOnlyDictionary<string, object?> Evidence);
public sealed record AssistantQuery([Required, MinLength(3), MaxLength(300)] string Question);
public sealed record AssistantAnswer(string Intent, string Answer, IReadOnlyList<string> RecommendedActions, IReadOnlyList<IReadOnlyDictionary<string, object?>> Data);
public sealed record ExecutiveDashboard(IReadOnlyList<IndicatorResult> Indicators, IReadOnlyList<AlertResult> TopRisks, IReadOnlyList<ForecastResult> Forecasts, DateTimeOffset GeneratedAt);
public sealed record DashboardCommand([Required, MinLength(2), MaxLength(120)] string Name, [MaxLength(500)] string? Description, IReadOnlyList<string>? SharedRoles);
public sealed record WidgetCommand([Required] string IndicatorCode, Guid? FarmId, Guid? SeasonId, [Range(0, 1000)] int Order, [RegularExpression("^(SMALL|MEDIUM|LARGE)$")] string Size);
public sealed record CustomDashboard(Guid Id, string Name, string? Description, IReadOnlyList<string> SharedRoles, IReadOnlyList<DashboardWidget> Widgets);
public sealed record DashboardWidget(Guid Id, string IndicatorCode, Guid? FarmId, Guid? SeasonId, int Order, string Size);

public interface IIntelligenceService
{
    Task<IReadOnlyList<IndicatorResult>> GetIndicatorsAsync(IntelligenceFilter filter, CancellationToken ct);
    Task<IReadOnlyList<ReportDefinition>> GetReportsAsync(CancellationToken ct);
    Task<ReportResult> RunReportAsync(string id, IntelligenceFilter filter, CancellationToken ct);
    Task<byte[]> ExportCsvAsync(string id, IntelligenceFilter filter, CancellationToken ct);
    Task<IReadOnlyList<AlertResult>> GetAlertsAsync(string? status, CancellationToken ct);
    Task ActOnAlertAsync(Guid id, string action, AlertAction command, CancellationToken ct);
    Task<ExecutiveDashboard> GetExecutiveDashboardAsync(IntelligenceFilter filter, CancellationToken ct);
    Task<IReadOnlyList<ForecastResult>> GetForecastsAsync(IntelligenceFilter filter, CancellationToken ct);
    Task<AssistantAnswer> AskAsync(AssistantQuery query, CancellationToken ct);
    Task<IReadOnlyList<CustomDashboard>> GetDashboardsAsync(CancellationToken ct);
    Task<Guid> SaveDashboardAsync(Guid? id, DashboardCommand command, Guid userId, CancellationToken ct);
    Task<Guid> AddWidgetAsync(Guid dashboardId, WidgetCommand command, CancellationToken ct);
    Task DeleteWidgetAsync(Guid dashboardId, Guid widgetId, CancellationToken ct);
}
