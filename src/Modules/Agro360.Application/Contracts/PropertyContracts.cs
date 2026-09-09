using System.ComponentModel.DataAnnotations;
using Agro360.SharedKernel;

namespace Agro360.Application.Contracts;

public sealed record CreateFarmCommand(
    Guid OrganizationId,
    [Required, StringLength(160, MinimumLength = 2)]
    string Name,
    [Required, RegularExpression("^[A-Za-z]{2}$")]
    string State,
    [Range(typeof(decimal), "0.0001", "99999999999999")]
    decimal TotalAreaHa,
    [StringLength(80)]
    string? RegistrationNumber,
    [StringLength(100)]
    string? CarNumber);

public sealed record UpdateFarmCommand(
    [Required, StringLength(160, MinimumLength = 2)] string Name,
    [Required, RegularExpression("^[A-Za-z]{2}$")] string State,
    [Range(typeof(decimal), "0.0001", "99999999999999")] decimal TotalAreaHa,
    [StringLength(80)] string? RegistrationNumber,
    [StringLength(100)] string? CarNumber,
    [Range(1, long.MaxValue)] long Version);

public sealed record FarmDto(
    Guid Id,
    Guid OrganizationId,
    string Name,
    string State,
    decimal TotalAreaHa,
    string? RegistrationNumber,
    string? CarNumber,
    long Version);

public sealed record CreateFieldCommand(
    Guid FarmId,
    [Required, StringLength(120, MinimumLength = 2)] string Name,
    [Range(typeof(decimal), "0.0001", "99999999999999")] decimal AreaHa,
    string? BoundaryGeoJson);

public sealed record UpdateFieldCommand(
    [Required, StringLength(120, MinimumLength = 2)] string Name,
    [Range(typeof(decimal), "0.0001", "99999999999999")] decimal AreaHa,
    string? BoundaryGeoJson,
    [Range(1, long.MaxValue)] long Version);

public sealed record FieldDto(Guid Id, Guid FarmId, string Name, decimal AreaHa, string? BoundaryGeoJson, long Version);

public sealed record PropertyOrganizationDto(Guid Id, string Name, string Type);

public interface IPropertyService
{
    Task<FarmDto> CreateFarmAsync(CreateFarmCommand command, CancellationToken cancellationToken);

    Task<FieldDto> CreateFieldAsync(CreateFieldCommand command, CancellationToken cancellationToken);

    Task<FarmDto> UpdateFarmAsync(Guid id, UpdateFarmCommand command, CancellationToken cancellationToken);

    Task<FieldDto> UpdateFieldAsync(Guid id, UpdateFieldCommand command, CancellationToken cancellationToken);

    Task ArchiveFarmAsync(Guid id, long version, CancellationToken cancellationToken);

    Task ArchiveFieldAsync(Guid id, long version, CancellationToken cancellationToken);

    Task<IReadOnlyList<PropertyOrganizationDto>> ListOrganizationsAsync(CancellationToken cancellationToken);

    Task<PagedResult<FarmDto>> ListFarmsAsync(int page, int pageSize, string? search, CancellationToken cancellationToken);

    Task<PagedResult<FieldDto>> ListFieldsAsync(Guid farmId, int page, int pageSize, CancellationToken cancellationToken);
}
