using Agro360.SharedKernel;

namespace Agro360.Application.Contracts;

public sealed record HerdCommand(Guid FarmId, string Name, string Species, string Category, int HeadCount);
public sealed record PastureCommand(Guid FarmId, string Name, decimal AreaHectares, string ForageType, string Status);
public sealed record PaddockCommand(Guid PastureId, string Name, decimal AreaHectares, decimal CapacityAu, string Status, int RestDays, int OccupationDays, decimal? EntryHeightCm, decimal? ExitHeightCm, decimal? ForageMassKgHa);
public sealed record PaddockMovementCommand(Guid? AnimalId, Guid? HerdId, Guid? OriginPaddockId, DateTimeOffset OccurredAt, string? Notes);
public sealed record AnimalStatusCommand(DateOnly OccurredOn, string? Reason);
public sealed record AnimalTransferCommand(Guid FarmId, Guid? PaddockId, DateOnly OccurredOn, string? Notes);
public sealed record HandlingEventCommand(Guid? AnimalId, Guid? HerdId, string Type, DateOnly OccurredOn, string Responsible, string? Notes, decimal? WeightKg, decimal? BodyScore, Guid? PaddockId, decimal EstimatedCost, Guid? WarehouseId, Guid? ProductId, decimal? ProductQuantity, string? Unit);
public sealed record HealthEventCommand(Guid? AnimalId, Guid? HerdId, string Type, DateOnly AppliedOn, decimal Dose, string Unit, Guid? WarehouseId, Guid? ProductId, int WithdrawalDays, DateOnly? NextApplication, string? Technician, string? Diagnosis, string? Notes);
public sealed record ReproductionEventCommand(Guid FemaleId, string Type, DateOnly OccurredOn, Guid? SireId, string? GeneticLot, bool? Positive, bool CreateCalf, string? CalfTag, string? Notes);
public sealed record NutritionItemCommand(Guid ProductId, decimal QuantityPerDay, string Unit, decimal UnitCost);
public sealed record NutritionPlanCommand(Guid FarmId, Guid? HerdId, string Name, DateOnly StartsOn, DateOnly? EndsOn, IReadOnlyList<NutritionItemCommand> Items);
public sealed record FeedingCommand(Guid PlanId, Guid WarehouseId, DateOnly SuppliedOn, int HeadCount, IReadOnlyList<NutritionItemCommand> Items, string? Notes);
public sealed record MilkProductionCommand(Guid FarmId, Guid? AnimalId, Guid? HerdId, DateOnly ProducedOn, decimal QuantityLiters, decimal DiscardedLiters, string? Quality, string? Destination, string? Notes);
public sealed record LivestockDashboardDto(
    long ActiveAnimals,
    IReadOnlyList<MetricDto> BySpecies,
    IReadOnlyList<MetricDto> ByCategory,
    long ActiveHerds,
    long UnderObservation,
    long Deaths,
    decimal AverageDailyGainKg,
    decimal MilkThisMonthLiters,
    long PregnantFemales,
    long ExpectedBirths,
    long VaccinesDue,
    long InWithdrawal,
    long PasturesInUse,
    long PasturesResting,
    long OvercapacityAlerts,
    decimal NutritionCostMonth,
    decimal HealthCostMonth,
    long PendingHandlings,
    long HealthAlerts,
    long RecentWeighings,
    string Status,
    DateOnly? ReferenceDate = null,
    long RestrictedAnimals = 0,
    long CommercialReservations = 0,
    long MovementsIn = 0,
    long MovementsOut = 0)
{
    public static LivestockDashboardDto Empty { get; } = new(
        0, [], [], 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "Sem dados", DateOnly.FromDateTime(DateTime.UtcNow));
}
public sealed record MetricDto(string Name, decimal Value);

public interface ILivestock360Service
{
    Task<AnimalDto?> GetAnimalAsync(Guid id, CancellationToken ct); Task<AnimalDto> UpdateAnimalAsync(Guid id, RegisterAnimalCommand command, CancellationToken ct); Task ChangeAnimalStatusAsync(Guid id, string status, AnimalStatusCommand command, CancellationToken ct); Task TransferAnimalAsync(Guid id, AnimalTransferCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListHerdsAsync(CancellationToken ct); Task<Guid> SaveHerdAsync(Guid? id, HerdCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListPasturesAsync(CancellationToken ct); Task<Guid> SavePastureAsync(Guid? id, PastureCommand command, CancellationToken ct); Task<IReadOnlyList<dynamic>> ListPaddocksAsync(CancellationToken ct); Task<Guid> SavePaddockAsync(Guid? id, PaddockCommand command, CancellationToken ct); Task MovePaddockAsync(Guid id, bool occupy, PaddockMovementCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListEventsAsync(string kind, CancellationToken ct); Task<Guid> AddHandlingAsync(HandlingEventCommand command, CancellationToken ct); Task<Guid> AddHealthAsync(HealthEventCommand command, CancellationToken ct); Task<Guid> AddReproductionAsync(ReproductionEventCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListNutritionPlansAsync(CancellationToken ct); Task<Guid> AddNutritionPlanAsync(NutritionPlanCommand command, CancellationToken ct); Task<IReadOnlyList<dynamic>> ListFeedingsAsync(CancellationToken ct); Task<Guid> AddFeedingAsync(FeedingCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListMilkAsync(CancellationToken ct); Task<Guid> AddMilkAsync(MilkProductionCommand command, CancellationToken ct); Task<IReadOnlyList<dynamic>> WeightGainAsync(CancellationToken ct); Task<LivestockDashboardDto> DashboardAsync(CancellationToken ct);
}

public sealed record LivestockCatalogCommand(string Code, string Name, string? SpeciesCode, string? Sex, bool Active = true);
public sealed record FacilityCommand(Guid FarmId, string Name, string Kind, Guid? PaddockId, int? CapacityHead, string Status, string? Notes);
public sealed record HandlingLotCommand(Guid FarmId, string Name, string Purpose, string? Notes);
public sealed record ReconcileHerdCommand(Guid HerdId, IReadOnlyList<Guid> AnimalIds, DateOnly OccurredOn, string? Notes);
public sealed record ChangeTagCommand(string NewTag, DateOnly ChangedOn, string? Reason);
public sealed record InactivateAnimalCommand(DateOnly OccurredOn, string Reason);
public sealed record HerdMovementCommand(
    Guid? AnimalId,
    Guid? HerdId,
    string Kind,
    string ReasonCode,
    DateOnly OccurredOn,
    Guid ResponsibleId,
    Guid? ToFarmId,
    Guid? ToHerdId,
    Guid? ToFacilityId,
    Guid? ToPaddockId,
    int? Quantity,
    string? OriginNotes,
    string? Notes,
    string? IdempotencyKey);
public sealed record HandlingOrderCommand(
    Guid FarmId,
    string HandlingType,
    DateOnly PlannedOn,
    Guid ResponsibleId,
    string Priority,
    Guid? FacilityId,
    string? Instructions,
    bool MassAtomic,
    IReadOnlyList<Guid> AnimalIds,
    Guid? HerdId,
    IReadOnlyList<HandlingMaterialCommand>? Materials);
public sealed record HandlingMaterialCommand(Guid ProductId, Guid? WarehouseId, decimal PlannedQuantity, string Unit);
public sealed record HandlingOrderTransitionCommand(string Status, string? Reason);
public sealed record HandlingExecutionItem(Guid AnimalId, string Outcome, string? Reason);
public sealed record HandlingExecutionCommand(IReadOnlyList<HandlingExecutionItem> Items, bool CompleteOrder, string? Notes);
public sealed record WeighingCommand(
    string Scope,
    Guid FarmId,
    Guid? AnimalId,
    Guid? HerdId,
    Guid? HandlingLotId,
    DateTimeOffset WeighedAt,
    decimal Weight,
    string Unit,
    decimal? ConversionFactor,
    int? HeadCount,
    string? Method,
    string? Equipment,
    string Source,
    Guid? ResponsibleId,
    string? Notes,
    string? ReviewJustification,
    string? IdempotencyKey);
public sealed record WeighingCorrectionCommand(decimal WeightKg, string Unit, decimal? ConversionFactor, string Justification, Guid? ResponsibleId);
public sealed record RestrictionReleaseCommand(DateOnly ReleasedOn, string Authority, string Criteria);
public sealed record FeedingReturnCommand(decimal ReturnedQuantity, decimal LostQuantity, bool Reusable, string? Notes);
public sealed record SaleReservationCommand(
    Guid FarmId,
    Guid? AnimalId,
    Guid? HerdId,
    int Quantity,
    DateOnly ReservedOn,
    string? BuyerName,
    string? Notes,
    string? IdempotencyKey);
public sealed record PhysicalExitCommand(DateOnly OccurredOn, Guid ResponsibleId, string? Notes);
public sealed record FinancialConfirmCommand(string BuyerName, string? BuyerDocument, decimal UnitPrice, DateOnly DueDate, string? Notes);
public sealed record LivestockExportFilter(string Kind, DateOnly? From, DateOnly? To, Guid? FarmId, string? Status);

public interface ILivestockHerdService
{
    Task<PagedResult<LookupItem>> LookupsAsync(string resource, string? search, int page, int pageSize, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListCatalogAsync(string kind, CancellationToken ct);
    Task<Guid> SaveCatalogAsync(string kind, Guid? id, LivestockCatalogCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListFacilitiesAsync(Guid? farmId, CancellationToken ct);
    Task<Guid> SaveFacilityAsync(Guid? id, FacilityCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListHandlingLotsAsync(Guid? farmId, CancellationToken ct);
    Task<Guid> SaveHandlingLotAsync(Guid? id, HandlingLotCommand command, CancellationToken ct);
    Task ReconcileHerdAsync(ReconcileHerdCommand command, CancellationToken ct);
    Task<dynamic?> AnimalDetailAsync(Guid id, CancellationToken ct);
    Task ChangeTagAsync(Guid id, ChangeTagCommand command, CancellationToken ct);
    Task InactivateAsync(Guid id, InactivateAnimalCommand command, CancellationToken ct);
    Task SoftDeleteAsync(Guid id, string reason, CancellationToken ct);
    Task RestoreAsync(Guid id, string reason, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListMovementsAsync(Guid? farmId, string? kind, DateOnly? from, DateOnly? until, CancellationToken ct);
    Task<Guid> RegisterMovementAsync(HerdMovementCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListHandlingOrdersAsync(string? status, Guid? farmId, CancellationToken ct);
    Task<dynamic?> HandlingOrderAsync(Guid id, CancellationToken ct);
    Task<Guid> CreateHandlingOrderAsync(HandlingOrderCommand command, CancellationToken ct);
    Task TransitionHandlingOrderAsync(Guid id, HandlingOrderTransitionCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ExecuteHandlingAsync(Guid id, HandlingExecutionCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> PreviewHandlingAsync(Guid id, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListWeighingsAsync(Guid? animalId, Guid? herdId, DateOnly? from, DateOnly? until, CancellationToken ct);
    Task<Guid> RecordWeighingAsync(WeighingCommand command, CancellationToken ct);
    Task CorrectWeighingAsync(Guid id, WeighingCorrectionCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> PerformanceAsync(Guid? animalId, Guid? herdId, DateOnly? from, DateOnly? until, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> ListRestrictionsAsync(Guid? animalId, string? purpose, bool onlyActive, CancellationToken ct);
    Task ReleaseRestrictionAsync(Guid id, RestrictionReleaseCommand command, CancellationToken ct);
    Task ReturnFeedingAsync(Guid id, FeedingReturnCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> EligibleForSaleAsync(Guid farmId, string? search, CancellationToken ct);
    Task<Guid> ReserveAsync(SaleReservationCommand command, CancellationToken ct);
    Task CancelReservationAsync(Guid id, string reason, CancellationToken ct);
    Task ConfirmPhysicalExitAsync(Guid id, PhysicalExitCommand command, CancellationToken ct);
    Task ConfirmFinancialAsync(Guid id, FinancialConfirmCommand command, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> CostSummaryAsync(Guid? farmId, Guid? herdId, Guid? animalId, DateOnly? from, DateOnly? until, CancellationToken ct);
    Task<IReadOnlyList<dynamic>> HerdReportAsync(DateOnly asOf, Guid? farmId, CancellationToken ct);
    Task<byte[]> ExportCsvAsync(LivestockExportFilter filter, CancellationToken ct);
}
