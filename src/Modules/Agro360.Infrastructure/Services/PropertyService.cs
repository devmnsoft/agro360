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
        var registrationNumber = PropertyRules.NormalizeOptional(command.RegistrationNumber, "Matrícula", 80);
        var carNumber = PropertyRules.NormalizeOptional(command.CarNumber, "CAR", 100);

        return database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            var organizationExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                """
                select exists(
                    select 1 from agro360.organization_organizations
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

            var duplicated = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from agro360.geo_farms where tenant_id=@TenantId and organization_id=@OrganizationId and lower(name)=lower(@Name) and deleted_at is null);",
                new { tenantContext.TenantId, command.OrganizationId, farm.Name },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (duplicated)
            {
                throw new ConflictException("Já existe uma fazenda com este nome na organização.", "agro360.properties_farm_duplicate");
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.geo_farms
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
                    RegistrationNumber = registrationNumber,
                    CarNumber = carNumber,
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
                registrationNumber,
                carNumber,
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
        var boundary = PropertyRules.NormalizeBoundary(command.BoundaryGeoJson);
        return database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            var farmArea = await connection.QuerySingleOrDefaultAsync<decimal?>(new CommandDefinition(
                "select total_area_ha from agro360.geo_farms where id=@FarmId and tenant_id=@TenantId and deleted_at is null for update;",
                new { command.FarmId, tenantContext.TenantId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (farmArea is null)
            {
                throw new NotFoundException("Fazenda", command.FarmId);
            }

            var allocatedArea = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
                "select coalesce(sum(area_ha),0) from agro360.geo_fields where tenant_id=@TenantId and farm_id=@FarmId and deleted_at is null;",
                new { command.FarmId, tenantContext.TenantId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            PropertyRules.EnsureFieldsFit(farmArea.Value, allocatedArea, field.AreaHa);

            var duplicated = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from agro360.geo_fields where tenant_id=@TenantId and farm_id=@FarmId and lower(name)=lower(@Name) and deleted_at is null);",
                new { command.FarmId, tenantContext.TenantId, field.Name },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (duplicated)
            {
                throw new ConflictException("Já existe um talhão com este nome na fazenda.", "agro360.properties_field_duplicate");
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.geo_fields
                    (id, tenant_id, farm_id, name, area_ha, boundary, created_at, created_by, version)
                values
                    (@Id, @TenantId, @FarmId, @Name, @AreaHa,
                     case when @BoundaryGeoJson is null then null
                          else cast(@BoundaryGeoJson as jsonb) end,
                     now(), @CreatedBy, 1);
                """,
                new
                {
                    field.Id,
                    field.TenantId,
                    field.FarmId,
                    field.Name,
                    field.AreaHa,
                    BoundaryGeoJson = boundary,
                    CreatedBy = tenantContext.UserId
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var dto = new FieldDto(field.Id, field.FarmId, field.Name, field.AreaHa, boundary, 1);
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

    public Task<FarmDto> UpdateFarmAsync(Guid id, UpdateFarmCommand command, CancellationToken cancellationToken) =>
        database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            var current = await connection.QuerySingleOrDefaultAsync<FarmDto>(new CommandDefinition(
                """
                select id, organization_id as OrganizationId, name, state,
                       total_area_ha as TotalAreaHa, registration_number as RegistrationNumber,
                       car_number as CarNumber, version
                from agro360.geo_farms
                where id=@Id and tenant_id=@TenantId and deleted_at is null
                for update;
                """,
                new { Id = id, tenantContext.TenantId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (current is null) throw new NotFoundException("Fazenda", id);

            var validated = Farm.Create(tenantContext.TenantId, current.OrganizationId, command.Name, command.State, command.TotalAreaHa);
            var registrationNumber = PropertyRules.NormalizeOptional(command.RegistrationNumber, "Matrícula", 80);
            var carNumber = PropertyRules.NormalizeOptional(command.CarNumber, "CAR", 100);
            var allocatedArea = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
                "select coalesce(sum(area_ha),0) from agro360.geo_fields where tenant_id=@TenantId and farm_id=@Id and deleted_at is null;",
                new { Id = id, tenantContext.TenantId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (allocatedArea > validated.TotalAreaHa)
            {
                throw new DomainException("A área total não pode ser menor que a soma dos talhões ativos.", "agro360.properties_farm_area_below_fields");
            }

            var duplicated = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from agro360.geo_farms where tenant_id=@TenantId and organization_id=@OrganizationId and id<>@Id and lower(name)=lower(@Name) and deleted_at is null);",
                new { Id = id, tenantContext.TenantId, current.OrganizationId, validated.Name },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (duplicated) throw new ConflictException("Já existe uma fazenda com este nome na organização.", "agro360.properties_farm_duplicate");

            var changed = await connection.ExecuteAsync(new CommandDefinition(
                """
                update agro360.geo_farms
                set name=@Name,state=@State,total_area_ha=@TotalAreaHa,
                    registration_number=@RegistrationNumber,car_number=@CarNumber,
                    updated_at=now(),updated_by=@UserId,version=version+1
                where id=@Id and tenant_id=@TenantId and version=@Version and deleted_at is null;
                """,
                new
                {
                    Id = id,
                    tenantContext.TenantId,
                    validated.Name,
                    validated.State,
                    validated.TotalAreaHa,
                    RegistrationNumber = registrationNumber,
                    CarNumber = carNumber,
                    UserId = tenantContext.UserId,
                    command.Version
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (changed == 0) throw new ConflictException("A fazenda foi alterada por outra pessoa. Atualize a lista e tente novamente.");

            var updated = new FarmDto(id, current.OrganizationId, validated.Name, validated.State, validated.TotalAreaHa, registrationNumber, carNumber, command.Version + 1);
            await connection.WriteAuditAsync(transaction, tenantContext, "update", "Farm", id, current, updated, cancellationToken).ConfigureAwait(false);
            await connection.EnqueueAsync(transaction, tenantContext.TenantId, "FarmUpdated", id, updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }, cancellationToken);

    public Task<FieldDto> UpdateFieldAsync(Guid id, UpdateFieldCommand command, CancellationToken cancellationToken) =>
        database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            var current = await connection.QuerySingleOrDefaultAsync<FieldDto>(new CommandDefinition(
                """
                select id,farm_id as FarmId,name,area_ha as AreaHa,
                       case when boundary is null then null else boundary::text end as BoundaryGeoJson,version
                from agro360.geo_fields
                where id=@Id and tenant_id=@TenantId and deleted_at is null
                for update;
                """,
                new { Id = id, tenantContext.TenantId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (current is null) throw new NotFoundException("Talhão", id);

            var validated = Field.Create(tenantContext.TenantId, current.FarmId, command.Name, command.AreaHa);
            var boundary = PropertyRules.NormalizeBoundary(command.BoundaryGeoJson);
            var farmArea = await connection.QuerySingleAsync<decimal>(new CommandDefinition(
                "select total_area_ha from agro360.geo_farms where id=@FarmId and tenant_id=@TenantId and deleted_at is null for update;",
                new { current.FarmId, tenantContext.TenantId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            var allocatedArea = await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
                "select coalesce(sum(area_ha),0) from agro360.geo_fields where tenant_id=@TenantId and farm_id=@FarmId and id<>@Id and deleted_at is null;",
                new { current.FarmId, Id = id, tenantContext.TenantId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            PropertyRules.EnsureFieldsFit(farmArea, allocatedArea, validated.AreaHa);

            var duplicated = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from agro360.geo_fields where tenant_id=@TenantId and farm_id=@FarmId and id<>@Id and lower(name)=lower(@Name) and deleted_at is null);",
                new { current.FarmId, Id = id, tenantContext.TenantId, validated.Name }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (duplicated) throw new ConflictException("Já existe um talhão com este nome na fazenda.", "agro360.properties_field_duplicate");

            var changed = await connection.ExecuteAsync(new CommandDefinition(
                """
                update agro360.geo_fields
                set name=@Name,area_ha=@AreaHa,boundary=case when @BoundaryGeoJson is null then null else cast(@BoundaryGeoJson as jsonb) end,
                    updated_at=now(),updated_by=@UserId,version=version+1
                where id=@Id and tenant_id=@TenantId and version=@Version and deleted_at is null;
                """,
                new { Id = id, tenantContext.TenantId, validated.Name, validated.AreaHa, BoundaryGeoJson = boundary, UserId = tenantContext.UserId, command.Version },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (changed == 0) throw new ConflictException("O talhão foi alterado por outra pessoa. Atualize a lista e tente novamente.");

            var updated = new FieldDto(id, current.FarmId, validated.Name, validated.AreaHa, boundary, command.Version + 1);
            await connection.WriteAuditAsync(transaction, tenantContext, "update", "Field", id, current, updated, cancellationToken).ConfigureAwait(false);
            return updated;
        }, cancellationToken);

    public Task ArchiveFarmAsync(Guid id, long version, CancellationToken cancellationToken) =>
        database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            var current = await connection.QuerySingleOrDefaultAsync<FarmDto>(new CommandDefinition(
                "select id,organization_id OrganizationId,name,state,total_area_ha TotalAreaHa,registration_number RegistrationNumber,car_number CarNumber,version from agro360.geo_farms where id=@Id and tenant_id=@TenantId and deleted_at is null for update;",
                new { Id = id, tenantContext.TenantId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (current is null) throw new NotFoundException("Fazenda", id);
            var hasFields = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from agro360.geo_fields where tenant_id=@TenantId and farm_id=@Id and deleted_at is null);",
                new { Id = id, tenantContext.TenantId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (hasFields) throw new ConflictException("Arquive primeiro os talhões ativos desta fazenda.", "agro360.properties_farm_has_fields");
            var changed = await connection.ExecuteAsync(new CommandDefinition(
                "update agro360.geo_farms set deleted_at=now(),deleted_by=@UserId,updated_at=now(),updated_by=@UserId,version=version+1 where id=@Id and tenant_id=@TenantId and version=@Version and deleted_at is null;",
                new { Id = id, tenantContext.TenantId, UserId = tenantContext.UserId, Version = version }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (changed == 0) throw new ConflictException("A fazenda foi alterada por outra pessoa. Atualize a lista e tente novamente.");
            await connection.WriteAuditAsync(transaction, tenantContext, "archive", "Farm", id, current, null, cancellationToken).ConfigureAwait(false);
            await connection.EnqueueAsync(transaction, tenantContext.TenantId, "FarmArchived", id, new { id, version = version + 1 }, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public Task ArchiveFieldAsync(Guid id, long version, CancellationToken cancellationToken) =>
        database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            var current = await connection.QuerySingleOrDefaultAsync<FieldDto>(new CommandDefinition(
                "select id,farm_id FarmId,name,area_ha AreaHa,case when boundary is null then null else boundary::text end BoundaryGeoJson,version from agro360.geo_fields where id=@Id and tenant_id=@TenantId and deleted_at is null for update;",
                new { Id = id, tenantContext.TenantId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (current is null) throw new NotFoundException("Talhão", id);
            var changed = await connection.ExecuteAsync(new CommandDefinition(
                "update agro360.geo_fields set deleted_at=now(),deleted_by=@UserId,updated_at=now(),updated_by=@UserId,version=version+1 where id=@Id and tenant_id=@TenantId and version=@Version and deleted_at is null;",
                new { Id = id, tenantContext.TenantId, UserId = tenantContext.UserId, Version = version }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (changed == 0) throw new ConflictException("O talhão foi alterado por outra pessoa. Atualize a lista e tente novamente.");
            await connection.WriteAuditAsync(transaction, tenantContext, "archive", "Field", id, current, null, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    public Task<IReadOnlyList<PropertyOrganizationDto>> ListOrganizationsAsync(CancellationToken cancellationToken) =>
        database.InTenantTransactionAsync(async (connection, transaction) =>
            (IReadOnlyList<PropertyOrganizationDto>)(await connection.QueryAsync<PropertyOrganizationDto>(new CommandDefinition(
                "select id,name,type from agro360.organization_organizations where tenant_id=@TenantId and deleted_at is null order by name;",
                new { tenantContext.TenantId }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray(), cancellationToken);

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
                from agro360.geo_farms
                where tenant_id = @TenantId
                  and deleted_at is null
                  and (@Search is null or name ilike '%' || @Search || '%');

                select id, organization_id as OrganizationId, name, state,
                       total_area_ha as TotalAreaHa, registration_number as RegistrationNumber,
                       car_number as CarNumber, version
                from agro360.geo_farms
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
                select count(*) from agro360.geo_fields
                where tenant_id = @TenantId and farm_id = @FarmId and deleted_at is null;

                select id, farm_id as FarmId, name, area_ha as AreaHa,
                       case when boundary is null then null else boundary::text end as BoundaryGeoJson,
                       version
                from agro360.geo_fields
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
