using System.ComponentModel.DataAnnotations;

namespace Agro360.Application.Contracts;

// --- Catalog / models ---

public sealed record InspectionModelListItem(
    Guid Id,
    string Code,
    string Name,
    string ProcessCode,
    string Status,
    int PublishedVersionNumber,
    DateTimeOffset UpdatedAt);

public sealed record InspectionModelVersionSummary(
    Guid Id,
    int VersionNumber,
    string Status,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    string? ChangeReason,
    long RowVersion,
    DateTimeOffset UpdatedAt);

public sealed record InspectionVersionDetail(
    Guid Id,
    Guid ModelId,
    int VersionNumber,
    string Status,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    string? ChangeReason,
    string? Instructions,
    long RowVersion,
    IReadOnlyList<InspectionSectionRuntimeDto> Sections);

public sealed record InspectionModelDetail(
    Guid Id,
    string Code,
    string Name,
    string? Description,
    string ProcessCode,
    string Status,
    int Precedence,
    bool AllowManualSelection,
    Guid? UnitId,
    string? ProductCategory,
    Guid? ProductId,
    long RowVersion,
    IReadOnlyList<InspectionModelVersionSummary> Versions);

public sealed record InspectionModelCreateCommand(
    [Required, MaxLength(40)] string Code,
    [Required, MaxLength(180)] string Name,
    [MaxLength(2000)] string? Description,
    [Required, MaxLength(40)] string ProcessCode,
    int Precedence,
    bool AllowManualSelection,
    Guid? UnitId,
    [MaxLength(80)] string? ProductCategory,
    Guid? ProductId,
    [MaxLength(4000)] string? Instructions,
    Guid? ReviewResponsibleId);

public sealed record InspectionModelUpdateCommand(
    [Required, MaxLength(180)] string Name,
    [MaxLength(2000)] string? Description,
    int Precedence,
    bool AllowManualSelection,
    Guid? UnitId,
    [MaxLength(80)] string? ProductCategory,
    Guid? ProductId,
    [MaxLength(4000)] string? Instructions,
    Guid? ReviewResponsibleId,
    long ExpectedRowVersion);

public sealed record InspectionCriterionOptionCommand(
    [Required, MaxLength(120)] string Value,
    [Required, MaxLength(180)] string Label,
    int SortOrder);

public sealed record InspectionCriterionDraftCommand(
    Guid? Id,
    [Required, MaxLength(80)] string StableKey,
    [Required, MaxLength(200)] string Name,
    [MaxLength(2000)] string? Guidance,
    [Required, MaxLength(40)] string CriterionType,
    bool Required,
    bool Critical,
    bool AllowNotApplicable,
    bool RequireNaJustification,
    bool RequireReview,
    [MaxLength(30)] string? Unit,
    decimal? Weight,
    int SortOrder,
    /// <summary>Structured approval_condition JSON object (operators/expected values).</summary>
    [MaxLength(8000)] string? ApprovalConditionJson,
    IReadOnlyList<InspectionCriterionOptionCommand>? Options);

public sealed record InspectionSectionDraftCommand(
    Guid? Id,
    [Required, MaxLength(80)] string StableKey,
    [Required, MaxLength(200)] string Name,
    [MaxLength(2000)] string? Description,
    int SortOrder,
    [Required, MinLength(1)] IReadOnlyList<InspectionCriterionDraftCommand> Criteria);

public sealed record InspectionDraftVersionUpdateCommand(
    [MaxLength(2000)] string? ChangeReason,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    long ExpectedRowVersion,
    [Required, MinLength(1)] IReadOnlyList<InspectionSectionDraftCommand> Sections);

public sealed record InspectionVersionActionCommand(
    long ExpectedRowVersion,
    [MaxLength(500)] string? Reason);

public sealed record InspectionPublishVersionCommand(
    [Required] DateOnly ValidFrom,
    DateOnly? ValidUntil,
    long ExpectedRowVersion,
    [MaxLength(500)] string? ChangeReason);

public sealed record InspectionInactivateCommand(
    [Required, MaxLength(500)] string Reason);

// --- Selection ---

public sealed record InspectionSelectionContext(
    [Required, MaxLength(40)] string ProcessCode,
    Guid? UnitId,
    [MaxLength(80)] string? ProductCategory,
    Guid? ProductId,
    [Required] DateOnly ReferenceDate);

public sealed record InspectionModelCandidate(
    Guid ModelId,
    string Code,
    string Name,
    Guid VersionId,
    int VersionNumber,
    int Precedence,
    int SpecificityScore,
    string MatchExplanation);

public sealed record InspectionModelResolution(
    IReadOnlyList<InspectionModelCandidate> Candidates,
    Guid? RecommendedModelId,
    Guid? RecommendedVersionId,
    bool Ambiguous,
    string RuleExplanation);

// --- Runs ---

public sealed record InspectionRunFilter(
    string? ProcessCode = null,
    string? Status = null,
    string? OverallResult = null,
    Guid? ModelId = null,
    Guid? ProductId = null,
    Guid? LotId = null,
    Guid? UnitId = null,
    Guid? InspectorId = null,
    DateOnly? From = null,
    DateOnly? To = null,
    int Page = 1,
    int PageSize = 50);

public sealed record InspectionRunListItem(
    Guid Id,
    string Number,
    string ProcessCode,
    string Status,
    string? OverallResult,
    string ModelName,
    int ModelVersionNumber,
    string? ProductName,
    string? LotName,
    string InspectorName,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset LastSavedAt);

public sealed record InspectionAnswerDto(
    Guid CriterionId,
    string StableKey,
    bool IsNotApplicable,
    string? NotApplicableJustification,
    bool? PassFailValue,
    string? TextValue,
    decimal? NumberValue,
    string? NumberUnit,
    DateOnly? DateValue,
    IReadOnlyList<string>? ChoiceValues,
    Guid? DocumentEvidenceId,
    string? Outcome,
    string? Notes);

public sealed record InspectionCriterionRuntimeDto(
    Guid Id,
    string StableKey,
    string Name,
    string? Guidance,
    string CriterionType,
    bool Required,
    bool Critical,
    bool AllowNotApplicable,
    bool RequireNaJustification,
    bool RequireReview,
    string? Unit,
    decimal? Weight,
    int SortOrder,
    string? ApprovalConditionJson,
    IReadOnlyList<InspectionCriterionOptionCommand> Options,
    InspectionAnswerDto? Answer);

public sealed record InspectionSectionRuntimeDto(
    Guid Id,
    string StableKey,
    string Name,
    string? Description,
    int SortOrder,
    IReadOnlyList<InspectionCriterionRuntimeDto> Criteria);

public sealed record InspectionRunProgress(
    int TotalCriteria,
    int AnsweredCriteria,
    int RequiredPending,
    int CriticalNonConforming,
    decimal PercentComplete);

public sealed record InspectionRunDetail(
    Guid Id,
    string Number,
    string ProcessCode,
    string Status,
    string? OverallResult,
    IReadOnlyList<string> DeterminingStableKeys,
    decimal? WeightedScorePercent,
    Guid ModelId,
    Guid ModelVersionId,
    string ModelName,
    int ModelVersionNumber,
    string? OriginType,
    Guid? OriginId,
    Guid? ProductId,
    string? ProductName,
    Guid? LotId,
    string? LotName,
    Guid? UnitId,
    string? UnitName,
    Guid InspectorId,
    string InspectorName,
    string SelectionMode,
    Guid? ParentRunId,
    string? ReinspectionReason,
    long RowVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset LastSavedAt,
    DateTimeOffset? CompletedAt,
    InspectionRunProgress Progress,
    IReadOnlyList<InspectionSectionRuntimeDto> Sections);

public sealed record InspectionStartRunCommand(
    Guid? ModelVersionId,
    [Required, MaxLength(40)] string ProcessCode,
    [MaxLength(40)] string? OriginType,
    Guid? OriginId,
    Guid? ProductId,
    Guid? LotId,
    Guid? UnitId,
    [Required] Guid InspectorId,
    [Required, MaxLength(40)] string SelectionMode,
    [Required, MaxLength(120)] string IdempotencyKey);

public sealed record InspectionAnswerSaveItem(
    [Required] Guid CriterionId,
    bool IsNotApplicable,
    [MaxLength(1000)] string? NotApplicableJustification,
    bool? PassFailValue,
    [MaxLength(4000)] string? TextValue,
    decimal? NumberValue,
    [MaxLength(30)] string? NumberUnit,
    DateOnly? DateValue,
    IReadOnlyList<string>? ChoiceValues,
    Guid? DocumentEvidenceId,
    [MaxLength(1000)] string? Notes);

public sealed record InspectionSaveAnswersCommand(
    [Required, MinLength(1)] IReadOnlyList<InspectionAnswerSaveItem> Answers,
    long ExpectedRowVersion);

public sealed record InspectionSaveAnswersResult(
    long RowVersion,
    DateTimeOffset LastSavedAt);

public sealed record InspectionCompleteRunCommand(
    long ExpectedRowVersion,
    [MaxLength(120)] string? IdempotencyKey);

public sealed record InspectionEffectSummary(
    bool NonConformitySuggested,
    bool LotHoldSuggested,
    IReadOnlyList<string> Messages);

public sealed record InspectionCompleteRunResult(
    string OverallResult,
    IReadOnlyList<string> DeterminingStableKeys,
    decimal? WeightedScorePercent,
    long RowVersion,
    InspectionEffectSummary Effects);

public sealed record InspectionCancelRunCommand(
    [Required, MaxLength(500)] string Reason,
    long ExpectedRowVersion);

public sealed record InspectionReinspectionCommand(
    [Required, MaxLength(500)] string Reason,
    bool UseLatestPublishedVersion,
    [Required, MaxLength(120)] string IdempotencyKey);

public sealed record InspectionRunCompareItem(
    string StableKey,
    string CriterionType,
    string Name,
    string? LeftOutcome,
    string? RightOutcome,
    string? LeftValueSummary,
    string? RightValueSummary,
    bool MeaningMatches);

public sealed record InspectionRunCompareResult(
    Guid RunId,
    Guid OtherRunId,
    IReadOnlyList<InspectionRunCompareItem> MatchedCriteria);

// --- Schedules ---

public sealed record InspectionScheduleListItem(
    Guid Id,
    string Name,
    string ProcessCode,
    Guid? ModelId,
    string? ModelName,
    string Cadence,
    string Status,
    DateOnly? NextDueOn,
    DateTimeOffset UpdatedAt);

public sealed record InspectionScheduleCommand(
    [Required, MaxLength(180)] string Name,
    [Required, MaxLength(40)] string ProcessCode,
    [Required] Guid ModelId,
    Guid? UnitId,
    [MaxLength(80)] string? ProductCategory,
    Guid? ProductId,
    [Required, MaxLength(40)] string Cadence,
    int? IntervalDays,
    [MaxLength(60)] string? EventCode,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    Guid? ResponsibleId,
    [MaxLength(2000)] string? Notes,
    bool AllowCatchUp,
    long? ExpectedRowVersion);

public sealed record InspectionScheduleInactivateCommand(
    [Required, MaxLength(500)] string Reason);

public sealed record InspectionDueScheduleGenerationResult(
    int GeneratedRuns,
    int SkippedExisting,
    IReadOnlyList<Guid> RunIds);

public interface IInspectionService
{
    // Models
    Task<IReadOnlyList<InspectionModelListItem>> ListModelsAsync(string? process, string? status, CancellationToken ct);
    Task<InspectionModelDetail> GetModelAsync(Guid id, CancellationToken ct);
    Task<Guid> CreateModelAsync(InspectionModelCreateCommand command, CancellationToken ct);
    Task UpdateModelAsync(Guid id, InspectionModelUpdateCommand command, CancellationToken ct);
    Task<Guid> CreateDraftVersionAsync(Guid modelId, Guid? fromVersionId, string? changeReason, CancellationToken ct);
    Task<InspectionVersionDetail> GetVersionAsync(Guid versionId, CancellationToken ct);
    Task UpdateDraftVersionAsync(Guid versionId, InspectionDraftVersionUpdateCommand command, CancellationToken ct);
    Task SubmitForReviewAsync(Guid versionId, long expectedRowVersion, CancellationToken ct);
    Task PublishVersionAsync(Guid versionId, InspectionPublishVersionCommand command, CancellationToken ct);
    Task InactivateModelAsync(Guid modelId, InspectionInactivateCommand command, CancellationToken ct);
    Task InactivateVersionAsync(Guid versionId, InspectionInactivateCommand command, CancellationToken ct);

    // Selection
    Task<InspectionModelResolution> ResolveApplicableModelsAsync(InspectionSelectionContext context, CancellationToken ct);

    // Runs
    Task<PagedResult<InspectionRunListItem>> ListRunsAsync(InspectionRunFilter filters, CancellationToken ct);
    Task<InspectionRunDetail> GetRunAsync(Guid id, CancellationToken ct);
    Task<Guid> StartRunAsync(InspectionStartRunCommand command, CancellationToken ct);
    Task<InspectionSaveAnswersResult> SaveAnswersAsync(Guid runId, InspectionSaveAnswersCommand command, CancellationToken ct);
    Task<InspectionCompleteRunResult> CompleteRunAsync(Guid runId, InspectionCompleteRunCommand command, CancellationToken ct);
    Task CancelRunAsync(Guid runId, InspectionCancelRunCommand command, CancellationToken ct);
    Task<Guid> CreateReinspectionAsync(Guid parentRunId, InspectionReinspectionCommand command, CancellationToken ct);
    Task<InspectionRunCompareResult> CompareRunsAsync(Guid runId, Guid otherRunId, CancellationToken ct);

    // Schedules
    Task<IReadOnlyList<InspectionScheduleListItem>> ListSchedulesAsync(CancellationToken ct);
    Task<Guid> SaveScheduleAsync(Guid? id, InspectionScheduleCommand command, CancellationToken ct);
    Task InactivateScheduleAsync(Guid id, InspectionScheduleInactivateCommand command, CancellationToken ct);
    Task<InspectionDueScheduleGenerationResult> GenerateDueSchedulesAsync(DateOnly asOf, CancellationToken ct);
    /// <summary>Worker entry: runs generation with explicit tenant_id (no request ITenantContext).</summary>
    Task<InspectionDueScheduleGenerationResult> GenerateDueSchedulesForTenantAsync(Guid tenantId, DateOnly asOf, CancellationToken ct);
}

// --- Event Intents (AG-Q-EVT-001) ---

public sealed record OperationalInspectionEventRequest(
    [Required, MaxLength(40)] string ProcessCode,
    [Required, MaxLength(60)] string OriginType,
    Guid OriginId,
    Guid? ProductId = null,
    Guid? LotId = null,
    Guid? UnitId = null,
    [MaxLength(2000)] string? Notes = null,
    [MaxLength(120)] string? CustomIdempotencyKey = null,
    [MaxLength(128)] string? RequestHash = null);

public sealed record OperationalInspectionEventResult(
    Guid IntentId,
    string ProcessCode,
    string OriginType,
    Guid OriginId,
    string Status,
    Guid? RunId,
    string? Message);

public sealed record InspectionEventIntentListItem(
    Guid Id,
    string ProcessCode,
    string OriginType,
    Guid OriginId,
    string Status,
    Guid? RunId,
    string? RunNumber,
    string? Notes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public interface IOperationalInspectionTrigger
{
    Task<OperationalInspectionEventResult> TryStartFromOriginAsync(
        OperationalInspectionEventRequest request,
        CancellationToken ct);

    Task<IReadOnlyList<InspectionEventIntentListItem>> ListEventIntentsAsync(
        string? process,
        string? status,
        int? limit,
        CancellationToken ct);
}
