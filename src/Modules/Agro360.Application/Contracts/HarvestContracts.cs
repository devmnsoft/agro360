using Agro360.SharedKernel;

namespace Agro360.Application.Contracts;

public sealed record CreateHarvestPlanCommand(Guid FarmId, Guid SeasonId, Guid FieldId, Guid ProductId,
    Guid DestinationWarehouseId, Guid? CostCenterId, Guid? ResponsibleId, DateOnly PlannedStart,
    DateOnly PlannedEnd, decimal PlannedAreaHa, decimal EstimatedQuantity, string Unit, string? Notes,
    string IdempotencyKey);

public sealed record RegisterHarvestRecordCommand(Guid PlanId, DateTimeOffset OperationalAt,
    decimal HarvestedQuantity, string Unit, decimal? HarvestedAreaHa, string CommercialReference,
    string? Notes, string IdempotencyKey);

public sealed record ReceiveHarvestCommand(Guid HarvestRecordId, Guid WarehouseId, DateTimeOffset ReceivedAt,
    decimal ReceivedQuantity, string Unit, decimal? GrossWeight, decimal? TareWeight, string LotNumber,
    string EntryMode, string? DivergenceReason, string? Notes, string IdempotencyKey);

public sealed record CompleteHarvestInspectionCommand(Guid ReceiptId, Guid SpecificationId,
    IReadOnlyCollection<HarvestInspectionResultCommand> Results, string Decision, string? Reason,
    string IdempotencyKey);

public sealed record HarvestInspectionResultCommand(Guid ParameterId, decimal? NumericValue,
    string? TextValue, Guid? EvidenceDocumentId);

public sealed record AllocateHarvestMaterialCommand(Guid ReceiptId, decimal Quantity, string Destination,
    string Reason, string IdempotencyKey);

public sealed record HarvestOperationDto(Guid Id, string Kind, string Status, decimal Quantity,
    string Unit, decimal RemainingQuantity, DateTimeOffset OccurredAt, string Reference, long Version);

public sealed record HarvestDashboardDto(decimal PlannedQuantity, decimal HarvestedQuantity,
    decimal ReceivedQuantity, decimal AwaitingReceipt, decimal AwaitingQuality, decimal ApprovedQuantity,
    decimal PhysicalLoss, decimal AppropriatedCosts, decimal? CostPerHectare, decimal? CostPerAcceptedUnit,
    bool Provisional, string Unit, IReadOnlyCollection<HarvestOperationDto> Recent);

public sealed record HarvestTraceDto(Guid ReceiptId, string LotNumber, string QualityStatus,
    Guid FarmId, Guid SeasonId, Guid FieldId, Guid HarvestRecordId, decimal ReceivedQuantity,
    decimal AllocatedQuantity, string Unit, IReadOnlyCollection<HarvestOperationDto> Timeline);

public sealed record SeasonClosingScopeDto(Guid SeasonId, Guid FarmId, string Season, string Farm,
    string Crop, DateOnly StartsOn, DateOnly EndsOn, DateOnly CutoffDate, string Unit);
public sealed record SeasonClosingIndicatorDto(string Code, string Label, decimal? Value, string Unit,
    string Availability, string Definition, string SourceUrl, string? Explanation);
public sealed record SeasonClosingIssueDto(Guid Id, string Code, string Category, string Severity,
    string Title, decimal? Expected, decimal? Found, string Unit, string Rule, string Impact,
    string? Responsible, string ActionLabel, string SourceUrl, string Status);
public sealed record SeasonClosingRunDto(Guid Id, DateTimeOffset GeneratedAt, string CriteriaVersion,
    IReadOnlyCollection<SeasonClosingIssueDto> Issues);
public sealed record SeasonClosingVersionDto(Guid Id, int Version, string State, DateOnly CutoffDate,
    DateTimeOffset GeneratedAt, Guid ResponsibleId, Guid? SupersedesId, string? Reason, string? Notes,
    IReadOnlyCollection<SeasonClosingIndicatorDto> Indicators, IReadOnlyCollection<SeasonClosingIssueDto> Issues);
public sealed record SeasonClosingDto(SeasonClosingScopeDto Scope, string State,
    IReadOnlyCollection<SeasonClosingIndicatorDto> Indicators, SeasonClosingRunDto? LastRun,
    IReadOnlyCollection<SeasonClosingVersionDto> Versions, bool HasRetroactiveMovement);
public sealed record RunSeasonClosingCommand(Guid SeasonId, DateOnly CutoffDate, string IdempotencyKey);
public sealed record CreateSeasonClosingCommand(Guid SeasonId, DateOnly CutoffDate, string? Notes,
    string? RevisionReason, string IdempotencyKey);
public sealed record ChangeSeasonClosingStateCommand(long Version, string? Notes);
public sealed record ReopenSeasonClosingCommand(long Version, string Reason, string IdempotencyKey);

public sealed record GenealogyNodeDto(string Stage, string OriginLabel, string DestinationLabel,
    string Status, decimal? Quantity, string? Unit, string? LotNumber, bool HasGap, string? GapReason,
    DateTimeOffset? OccurredAt, string? Details);

public sealed record SeasonGenealogyDto(Guid SeasonId, string SeasonName, string FarmName, string Crop,
    IReadOnlyCollection<GenealogyNodeDto> Nodes, bool HasMissingFieldOrigin, bool HasUnlinkedShipments,
    decimal TotalReceivedQuantity, decimal TotalAllocatedQuantity, decimal? CurrentStockQuantity,
    string? CurrentStockUnit, string CurrentStockStatus, string? CurrentStockExplanation,
    decimal? RecognizedRevenue, string RevenueCurrency, string RevenueStatus, string? RevenueExplanation);

public sealed record LotGenealogyDto(string LotNumber, string ProductName, Guid? SeasonId,
    string? SeasonName, string? FarmName, string? FieldName, string Status,
    IReadOnlyCollection<GenealogyNodeDto> Nodes, bool HasGap, string? GapReason);

public sealed record RecordGenealogyLinkCommand(string Kind, string OriginType, Guid OriginId,
    string DestinationType, Guid DestinationId, Guid? SeasonId, Guid? FieldId, string? LotNumber,
    decimal? Quantity, string? Unit, string? Metadata, string IdempotencyKey);

public sealed record OperationalPendingDto(string Code, string Title, string Impact, string Origin,
    string RecommendedAction, string SourceUrl, string RequiredPermission, string Status,
    DateTimeOffset? OccurredAt, string? Responsible);

public sealed record PublishPublicTraceCommand(string LotNumber, bool PublishFarm, string IdempotencyKey);
public sealed record RevokePublicTraceCommand(string Reason);
public sealed record PublicTraceEventDto(string Stage, string Status, DateTimeOffset? OccurredAt);
public sealed record PublicTraceDto(string PublicCode, string Product, string Lot, string GeneralOrigin,
    string? Farm, string? Season, string? Crop, IReadOnlyCollection<string> PublicCertifications,
    IReadOnlyCollection<PublicTraceEventDto> Events, string FinalStatus, DateTimeOffset UpdatedAt);
public sealed record PublicTracePublicationDto(string PublicCode, string Status, DateTimeOffset PublishedAt,
    DateTimeOffset? RevokedAt);

public interface IPublicTraceabilityService
{
    Task<PublicTracePublicationDto> PublishAsync(PublishPublicTraceCommand command, CancellationToken cancellationToken);
    Task RevokeAsync(string publicCode, RevokePublicTraceCommand command, CancellationToken cancellationToken);
    Task<PublicTraceDto?> GetAsync(string publicCode, CancellationToken cancellationToken);
}

public interface IHarvestService
{
    Task<HarvestOperationDto> CreatePlanAsync(CreateHarvestPlanCommand command, CancellationToken cancellationToken);
    Task<HarvestOperationDto> RegisterAsync(RegisterHarvestRecordCommand command, CancellationToken cancellationToken);
    Task<HarvestOperationDto> ReceiveAsync(ReceiveHarvestCommand command, CancellationToken cancellationToken);
    Task<HarvestOperationDto> InspectAsync(CompleteHarvestInspectionCommand command, CancellationToken cancellationToken);
    Task<HarvestOperationDto> AllocateAsync(AllocateHarvestMaterialCommand command, CancellationToken cancellationToken);
    Task<HarvestDashboardDto> DashboardAsync(Guid? seasonId, Guid? fieldId, CancellationToken cancellationToken);
    Task<HarvestTraceDto> TraceAsync(Guid receiptId, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<HarvestOperationDto>> ListAsync(string? kind, Guid? seasonId, CancellationToken cancellationToken);
    Task<SeasonClosingDto> GetClosingAsync(Guid seasonId, DateOnly cutoffDate, CancellationToken cancellationToken);
    Task<SeasonClosingRunDto> RunClosingChecksAsync(RunSeasonClosingCommand command, CancellationToken cancellationToken);
    Task<SeasonClosingVersionDto> CreateClosingAsync(CreateSeasonClosingCommand command, CancellationToken cancellationToken);
    Task<SeasonClosingVersionDto> CloseAsync(Guid closingId, ChangeSeasonClosingStateCommand command, CancellationToken cancellationToken);
    Task<SeasonClosingVersionDto> ReopenAsync(Guid closingId, ReopenSeasonClosingCommand command, CancellationToken cancellationToken);
    Task<SeasonGenealogyDto> GetSeasonGenealogyAsync(Guid seasonId, CancellationToken cancellationToken);
    Task<LotGenealogyDto> GetLotGenealogyAsync(string lotNumber, CancellationToken cancellationToken);
    Task<Guid> RecordGenealogyLinkAsync(RecordGenealogyLinkCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<OperationalPendingDto>> GetOperationalPendingsAsync(CancellationToken cancellationToken);
}
