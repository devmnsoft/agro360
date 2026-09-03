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
    string Status)
{
    public static LivestockDashboardDto Empty { get; } = new(
        0, [], [], 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, "Sem dados");
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
