using Agro360.SharedKernel;

namespace Agro360.Application.Contracts;

public sealed record RegisterAnimalCommand(
    Guid FarmId,
    string Tag,
    string? Rfid,
    string Species,
    string Breed,
    string Sex,
    DateOnly BirthDate,
    Guid? HerdId,
    Guid? MotherId,
    Guid? FatherId);

public sealed record AnimalDto(
    Guid Id,
    Guid FarmId,
    string Tag,
    string? Rfid,
    string Species,
    string Breed,
    string Sex,
    DateOnly BirthDate,
    string Status,
    decimal? CurrentWeightKg,
    DateOnly? LastWeightDate,
    DateOnly? WithdrawalUntil,
    long Version);

public sealed record WeighAnimalCommand(Guid AnimalId, decimal WeightKg, DateOnly MeasuredOn, string? Notes, string? IdempotencyKey);

public sealed record TreatAnimalCommand(
    Guid AnimalId,
    Guid WarehouseId,
    Guid ProductId,
    decimal Quantity,
    string Unit,
    DateOnly AppliedOn,
    int WithdrawalDays,
    string TreatmentType,
    string? LotNumber,
    string? Notes,
    string? IdempotencyKey);

public sealed record AnimalEventResult(Guid EventId, Guid AnimalId, string EventType, decimal? DailyGainKg, decimal CostAmount);

public interface ILivestockService
{
    Task<AnimalDto> RegisterAnimalAsync(RegisterAnimalCommand command, CancellationToken cancellationToken);

    Task<AnimalEventResult> WeighAsync(WeighAnimalCommand command, CancellationToken cancellationToken);

    Task<AnimalEventResult> TreatAsync(TreatAnimalCommand command, CancellationToken cancellationToken);

    Task<PagedResult<AnimalDto>> ListAnimalsAsync(Guid? farmId, int page, int pageSize, string? search, CancellationToken cancellationToken);
}
