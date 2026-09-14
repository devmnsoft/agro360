namespace Agro360.Application.Contracts;

public sealed record SeasonCostQuery(Guid? SeasonId = null, Guid? FarmId = null, Guid? FieldId = null,
    string? Category = null, string? Status = null, string? Search = null, DateOnly? From = null,
    DateOnly? To = null, int Page = 1, int PageSize = 25);
public sealed record ManualSeasonCostCommand(Guid FarmId, string Category, DateOnly CompetenceDate,
    decimal RecognizedAmount, string Currency, string Description, string Justification, string IdempotencyKey,
    decimal PlannedAmount = 0, decimal CommittedAmount = 0, decimal PaidAmount = 0,
    string? SourceDocument = null);
public sealed record CostDestinationCommand(Guid SeasonId, Guid FarmId, Guid? FieldId, Guid? CostCenterId,
    decimal BaseValue, string? Unit = null, decimal? Percentage = null);
public sealed record CostAllocationPreviewCommand(Guid EntryId, decimal Amount, string Method,
    long EntryVersion, IReadOnlyList<CostDestinationCommand> Destinations);
public sealed record CostAllocationLineDto(Guid SeasonId, Guid FarmId, Guid? FieldId, Guid? CostCenterId,
    decimal BaseValue, string? Unit, decimal Percentage, decimal Amount, decimal RoundingAdjustment);
public sealed record CostAllocationPreviewDto(Guid EntryId, decimal OriginalAmount, decimal AlreadyAllocated,
    decimal AvailableAmount, decimal SelectedAmount, string Currency, string Method, long EntryVersion,
    string RoundingRule, IReadOnlyList<CostAllocationLineDto> Lines);
public sealed record ConfirmCostAllocationCommand(CostAllocationPreviewCommand Preview, string IdempotencyKey,
    string? Justification);
public sealed record ReverseCostAllocationCommand(string Reason, string IdempotencyKey);

public interface ISeasonCostService
{
    Task<dynamic> OverviewAsync(SeasonCostQuery query, CancellationToken ct);
    Task<dynamic> ListAsync(SeasonCostQuery query, CancellationToken ct);
    Task<dynamic?> GetAsync(Guid id, CancellationToken ct);
    Task<Guid> AddManualAsync(ManualSeasonCostCommand command, CancellationToken ct);
    Task<CostAllocationPreviewDto> PreviewAsync(CostAllocationPreviewCommand command, CancellationToken ct);
    Task<Guid> ConfirmAsync(ConfirmCostAllocationCommand command, CancellationToken ct);
    Task ReverseAsync(Guid batchId, ReverseCostAllocationCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> HistoryAsync(Guid? seasonId, CancellationToken ct);
    Task<dynamic> DestinationsAsync(Guid? farmId, CancellationToken ct);
    Task<dynamic> ClosingReviewAsync(Guid seasonId, DateOnly cutoffDate, CancellationToken ct);
    Task<string> ExportCsvAsync(SeasonCostQuery query, CancellationToken ct);
}
