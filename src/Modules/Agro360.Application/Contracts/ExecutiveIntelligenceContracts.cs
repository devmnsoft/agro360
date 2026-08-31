using System.ComponentModel.DataAnnotations;

namespace Agro360.Application.Contracts;

public sealed record ExecutiveKpi(Guid Id, string Code, string Name, string Category, string Unit, decimal? Value, decimal? Target, string Status, DateTimeOffset? CalculatedAt, string? CalculationError);
public sealed record ExecutivePanel(IReadOnlyList<ExecutiveKpi> Indicators, int OpenAlerts, int CriticalAlerts, int OpenRisks, int PendingRecommendations);
public sealed record KpiDefinitionCommand([Required, StringLength(160, MinimumLength = 3)] string Name, [Required, RegularExpression("^[A-Z][A-Z0-9_]{2,59}$")] string Code, [Required] string Category, [Required] string Formula, [Required] string DataSource, [Required] string Periodicity, [Required] string Unit, decimal? Target, decimal? AttentionLimit, decimal? CriticalLimit, bool Active = true, bool Strategic = false);
public sealed record AlertDecisionCommand([Required, RegularExpression("^(UNDER_REVIEW|ASSIGNED|RESOLVED|IGNORED|CANCELLED)$")] string Status, Guid? ResponsibleId, [StringLength(2000)] string? Comment);
public sealed record RecommendationStatusCommand([Required, RegularExpression("^(ANALYSED|ACCEPTED|REJECTED|COMPLETED)$")] string Status, [StringLength(2000)] string? Reason);

public interface IExecutiveIntelligenceService
{
    Task<ExecutivePanel> GetPanelAsync(CancellationToken cancellationToken);
    Task<Guid> CreateKpiAsync(KpiDefinitionCommand command, Guid userId, bool canManageStrategic, CancellationToken cancellationToken);
    Task RecalculateAsync(Guid id, Guid userId, CancellationToken cancellationToken);
    Task DecideAlertAsync(Guid id, AlertDecisionCommand command, Guid userId, CancellationToken cancellationToken);
    Task DecideRecommendationAsync(Guid id, RecommendationStatusCommand command, Guid userId, CancellationToken cancellationToken);
    Task<byte[]> ExportAsync(string report, IntelligencePageFilter filter, Guid userId, CancellationToken cancellationToken);
}

public interface IExecutiveAiRecommendationProvider
{
    bool IsConfigured { get; }
    Task<string?> RecommendAsync(string sanitizedContext, CancellationToken cancellationToken);
}
