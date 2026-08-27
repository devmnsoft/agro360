using System.ComponentModel.DataAnnotations;

namespace Agro360.Application.Contracts;

public sealed record IntelligencePageFilter([Range(1, 500)] int Page = 1, [Range(1, 100)] int PageSize = 20, string? Search = null, string? Status = null, string? Severity = null, string? Module = null);
public sealed record IntelligencePage<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
public sealed record RecommendationSummary(Guid Id, string Module, string Type, string Title, string Reason, string SuggestedAction, string Severity, string Status, string EntityType, DateTimeOffset CreatedAt, DateTimeOffset? DueAt);
public sealed record RecommendationSource(string SourceModule, string SourceEntityType, string DisplayValue, string? ReferenceValue, DateTimeOffset ObservedAt);
public sealed record RecommendationDetail(RecommendationSummary Recommendation, IReadOnlyList<RecommendationSource> Sources);
public sealed record RecommendationDecision([Required, RegularExpression("^(ACCEPTED|REJECTED|IGNORED|ARCHIVED)$")] string Status, [StringLength(1000)] string? Reason);
public sealed record RiskScoreSummary(Guid Id, string EntityType, Guid EntityId, string Module, decimal OverallScore, string RiskBand, string FormulaVersion, DateTimeOffset CalculatedAt);
public sealed record RiskFactor(string Code, string Description, decimal ObservedValue, decimal? ReferenceValue, decimal Weight, decimal Contribution, string SourceModule);
public sealed record RiskScoreDetail(RiskScoreSummary Score, IReadOnlyList<RiskFactor> Factors);
public sealed record AnomalySummary(Guid Id, string Module, string Type, string Title, string Criterion, decimal ObservedValue, decimal ReferenceValue, string Severity, string Status, string EntityType, DateTimeOffset DetectedAt);
public sealed record PrioritySummary(Guid Id, string SourceType, string Module, string Title, string Severity, decimal PriorityScore, decimal? ImpactAmount, DateTimeOffset? DueAt, string ActionUrl);
public sealed record IntelligenceDashboardSummary(int OpenRecommendations, int CriticalRecommendations, int AcceptedRecommendations, int RejectedRecommendations, int OpenAnomalies, int CriticalAnomalies, int CriticalScores, int OverdueItems, int UnassignedItems, decimal EstimatedImpact, IReadOnlyList<RiskScoreSummary> TopRisks, IReadOnlyList<PrioritySummary> Priorities);
public sealed record IntelligenceRuleCommand([Required, MinLength(3), MaxLength(160)] string Name, [Required, MaxLength(1000)] string Description, [Required, MaxLength(60)] string Module, [Required, MaxLength(40)] string Type, [Required] string ConditionDefinition, [Required, RegularExpression("^(LOW|ATTENTION|HIGH|CRITICAL)$")] string Severity, [Range(typeof(decimal), "0.01", "999999")] decimal Weight, [Required, MaxLength(1000)] string SuggestedAction, bool Active, [Range(1, 525600)] int PeriodicityMinutes, [MaxLength(120)] string? RequiredPermission);
public sealed record IntelligenceRuleSummary(Guid Id, string Name, string Description, string Module, string Type, string Severity, decimal Weight, bool Active, int PeriodicityMinutes, DateTimeOffset? LastExecutedAt);

public interface IOperationalIntelligenceService
{
    Task<IntelligenceDashboardSummary> DashboardAsync(CancellationToken ct);
    Task<IntelligencePage<RecommendationSummary>> RecommendationsAsync(IntelligencePageFilter filter, CancellationToken ct);
    Task<RecommendationDetail> RecommendationAsync(Guid id, CancellationToken ct);
    Task DecideRecommendationAsync(Guid id, RecommendationDecision decision, Guid userId, CancellationToken ct);
    Task<IntelligencePage<RiskScoreSummary>> ScoresAsync(IntelligencePageFilter filter, CancellationToken ct);
    Task<RiskScoreDetail> ScoreAsync(Guid id, CancellationToken ct);
    Task<IntelligencePage<AnomalySummary>> AnomaliesAsync(IntelligencePageFilter filter, CancellationToken ct);
    Task ArchiveAnomalyAsync(Guid id, string? reason, Guid userId, CancellationToken ct);
    Task<IReadOnlyList<PrioritySummary>> PrioritiesAsync(CancellationToken ct);
    Task<IReadOnlyList<IntelligenceRuleSummary>> RulesAsync(CancellationToken ct);
    Task<Guid> SaveRuleAsync(Guid? id, IntelligenceRuleCommand command, Guid userId, CancellationToken ct);
}
