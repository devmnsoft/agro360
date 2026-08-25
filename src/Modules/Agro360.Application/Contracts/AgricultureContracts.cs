using Agro360.SharedKernel;

namespace Agro360.Application.Contracts;

public sealed record CreateSeasonCommand(
    Guid FarmId,
    string Name,
    string Crop,
    DateOnly StartDate,
    DateOnly EndDate,
    decimal PlannedAreaHa,
    decimal ExpectedYieldPerHa);

public sealed record SeasonDto(
    Guid Id,
    Guid FarmId,
    string Name,
    string Crop,
    DateOnly StartDate,
    DateOnly EndDate,
    string Status,
    decimal PlannedAreaHa,
    decimal ExpectedYieldPerHa,
    long Version);

public sealed record RegisterPlantingCommand(
    Guid SeasonId,
    Guid FieldId,
    Guid WarehouseId,
    Guid SeedProductId,
    decimal AreaHa,
    decimal SeedQuantity,
    string SeedUnit,
    DateTimeOffset ExecutedAt,
    string? Notes,
    string? IdempotencyKey);

public sealed record RegisterHarvestCommand(
    Guid SeasonId,
    Guid FieldId,
    Guid DestinationWarehouseId,
    Guid HarvestedProductId,
    decimal Quantity,
    string Unit,
    DateTimeOffset ExecutedAt,
    string? LotNumber,
    string? IdempotencyKey);

public sealed record FieldOperationResult(
    Guid OperationId,
    Guid CostEntryId,
    Guid StockMovementId,
    decimal CostAmount,
    string Status);

public interface IAgricultureService
{
    Task<SeasonDto> CreateSeasonAsync(CreateSeasonCommand command, CancellationToken cancellationToken);

    Task<FieldOperationResult> RegisterPlantingAsync(RegisterPlantingCommand command, CancellationToken cancellationToken);

    Task<FieldOperationResult> RegisterHarvestAsync(RegisterHarvestCommand command, CancellationToken cancellationToken);

    Task<PagedResult<SeasonDto>> ListSeasonsAsync(Guid? farmId, int page, int pageSize, CancellationToken cancellationToken);
}
