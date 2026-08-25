using Agro360.SharedKernel;

namespace Agro360.Application.Contracts;

public sealed record CreateFarmCommand(
    Guid OrganizationId,
    string Name,
    string State,
    decimal TotalAreaHa,
    string? RegistrationNumber,
    string? CarNumber);

public sealed record FarmDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string State,
    decimal TotalAreaHa,
    string? RegistrationNumber,
    string? CarNumber,
    long Version);

public sealed record CreateFieldCommand(Guid FarmId, string Name, decimal AreaHa, string? BoundaryGeoJson);

public sealed record FieldDto(Guid Id, Guid FarmId, string Name, decimal AreaHa, string? BoundaryGeoJson, long Version);

public interface IPropertyService
{
    Task<FarmDto> CreateFarmAsync(CreateFarmCommand command, CancellationToken cancellationToken);

    Task<FieldDto> CreateFieldAsync(CreateFieldCommand command, CancellationToken cancellationToken);

    Task<PagedResult<FarmDto>> ListFarmsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken);

    Task<PagedResult<FieldDto>> ListFieldsAsync(Guid farmId, int page, int pageSize, CancellationToken cancellationToken);
}
