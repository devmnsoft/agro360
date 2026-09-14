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
}
