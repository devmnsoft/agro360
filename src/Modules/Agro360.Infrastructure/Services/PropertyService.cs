using Agro360.Application.Contracts;
using Agro360.Domain.Properties;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;

namespace Agro360.Infrastructure.Services;

public sealed class PropertyService(DatabaseExecutor database, ITenantContext tenantContext) : IPropertyService
{
    public Task<FarmDto> CreateFarmAsync(CreateFarmCommand command, CancellationToken cancellationToken)
    {
        var farm = Farm.Create(
            tenantContext.TenantId,
            command.OrganizationId,
            command.Name,
            command.State,
            command.TotalAreaHa);

        return database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            var organizationExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                select exists(
                    select 1 from organization.organizations
                    where id = @OrganizationId and tenant_id = @TenantId and deleted_at is null
                );
                """,
                new { command.OrganizationId, tenantContext.TenantId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (!organizationExists)
            {
                throw new NotFoundException("Organização", command.OrganizationId);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into geo.farms
                    (id, tenant_id, organization_id, name, state, total_area_ha,
                     registration_number, car_number, created_at, created_by, version)
                values
                    (@Id, @TenantId, @OrganizationId, @Name, @State, @TotalAreaHa,
                     @RegistrationNumber, @CarNumber, now(), @CreatedBy, 1);
                """,
                new
                {
                    farm.Id,
                    farm.TenantId,
                    command.OrganizationId,
                    farm.Name,
                    farm.State,
                    farm.TotalAreaHa,
                    command.RegistrationNumber,
                    command.CarNumber,
                    CreatedBy = tenantContext.UserId
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var dto = new FarmDto(
                farm.Id,
                command.OrganizationId,
                farm.Name,
                farm.State,
                farm.TotalAreaHa,
                command.RegistrationNumber,
                command.CarNumber,
                1);
            await connection.WriteAuditAsync(
                transaction,
                tenantContext,
                "create",
                "Farm",
                farm.Id,
                null,
                dto,
                cancellationToken).ConfigureAwait(false);
            await connection.EnqueueAsync(
                transaction,
                tenantContext.TenantId,
                "FarmCreated",
                farm.Id,
                dto,
                cancellationToken).ConfigureAwait(false);
            return dto;
        }, cancellationToken);
    }

    public Task<FieldDto> CreateFieldAsync(CreateFieldCommand command, CancellationToken cancellationToken)
    {
        var field = Field.Create(tenantContext.TenantId, command.FarmId, command.Name, command.AreaHa);
        return database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            var farmExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from geo.farms where id = @FarmId and tenant_id = @TenantId and deleted_at is null);",
                new { command.FarmId, tenantContext.TenantId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (!farmExists)
            {
                throw new NotFoundException("Fazenda", command.FarmId);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into geo.fields
                    (id, tenant_id, farm_id, name, area_ha, boundary, created_at, created_by, version)
                values
                    (@Id, @TenantId, @FarmId, @Name, @AreaHa,
                     case when @BoundaryGeoJson is null then null
                          else ST_Multi(ST_SetSRID(ST_GeomFromGeoJSON(@BoundaryGeoJson), 4326)) end,
                     now(), @CreatedBy, 1);
                """,
                new
                {
                    field.Id,
                    field.TenantId,
                    field.FarmId,
                    field.Name,
                    field.AreaHa,
                    command.BoundaryGeoJson,
                    CreatedBy = tenantContext.UserId
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var dto = new FieldDto(field.Id, field.FarmId, field.Name, field.AreaHa, command.BoundaryGeoJson, 1);
            await connection.WriteAuditAsync(
                transaction,
                tenantContext,
                "create",
                "Field",
                field.Id,
                null,
                dto,
                cancellationToken).ConfigureAwait(false);
            return dto;
        }, cancellationToken);
    }

    public Task<PagedResult<FarmDto>> ListFarmsAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        return database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
                """
                select count(*)
                from geo.farms
                where tenant_id = @TenantId
                  and deleted_at is null
                  and (@Search is null or name ilike '%' || @Search || '%');

                select id, organization_id as OrganizationId, name, state,
                       total_area_ha as TotalAreaHa, registration_number as RegistrationNumber,
                       car_number as CarNumber, version
                from geo.farms
                where tenant_id = @TenantId
                  and deleted_at is null
                  and (@Search is null or name ilike '%' || @Search || '%')
                order by name
                limit @PageSize offset @Offset;
                """,
                new
                {
                    tenantContext.TenantId,
                    Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim(),
                    PageSize = pageSize,
                    Offset = (page - 1) * pageSize
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            var total = await grid.ReadSingleAsync<long>().ConfigureAwait(false);
            var items = (await grid.ReadAsync<FarmDto>().ConfigureAwait(false)).ToArray();
            return new PagedResult<FarmDto>(items, page, pageSize, total);
        }, cancellationToken);
    }

    public Task<PagedResult<FieldDto>> ListFieldsAsync(
        Guid farmId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        (page, pageSize) = NormalizePage(page, pageSize);
        return database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            using var grid = await connection.QueryMultipleAsync(new CommandDefinition(
                """
                select count(*) from geo.fields
                where tenant_id = @TenantId and farm_id = @FarmId and deleted_at is null;

                select id, farm_id as FarmId, name, area_ha as AreaHa,
                       case when boundary is null then null else ST_AsGeoJSON(boundary) end as BoundaryGeoJson,
                       version
                from geo.fields
                where tenant_id = @TenantId and farm_id = @FarmId and deleted_at is null
                order by name
                limit @PageSize offset @Offset;
                """,
                new
                {
                    tenantContext.TenantId,
                    FarmId = farmId,
                    PageSize = pageSize,
                    Offset = (page - 1) * pageSize
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            var total = await grid.ReadSingleAsync<long>().ConfigureAwait(false);
            var items = (await grid.ReadAsync<FieldDto>().ConfigureAwait(false)).ToArray();
            return new PagedResult<FieldDto>(items, page, pageSize, total);
        }, cancellationToken);
    }

    private static (int Page, int PageSize) NormalizePage(int page, int pageSize) =>
        (Math.Max(1, page), Math.Clamp(pageSize, 1, 100));
}
