using System.Globalization;
using System.Text;
using System.Text.Json;
using Agro360.Application.Contracts;
using Agro360.Domain.People;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;

namespace Agro360.Infrastructure.Services;

public sealed class RuralHrService(DatabaseExecutor db, ITenantContext tenant) : IRuralHrService
{
    public Task<IReadOnlyList<RuralHrRecord>> ListAsync(string kind, string? status, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var targetKind = (kind ?? "").Trim().ToUpperInvariant();
        if (targetKind == "PERSON")
        {
            var people = await c.QueryAsync<RuralHrRecord>(new CommandDefinition(
                "select id, 'PERSON' as kind, name, status, null as personid, null as teamid, null as startsat, null as endsat, 0 as amount, updated_at as updatedat from agro360.rural_hr_people where tenant_id=@TenantId and (@Status is null or status=@Status) order by name asc",
                new { tenant.TenantId, Status = status }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);
            return (IReadOnlyList<RuralHrRecord>)people.ToArray();
        }
        if (targetKind == "TIME_ENTRY")
        {
            var entries = await c.QueryAsync<RuralHrRecord>(new CommandDefinition(
                "select t.id, 'TIME_ENTRY' as kind, coalesce(p.name, 'Jornada') as name, t.status, t.person_id as personid, t.team_id as teamid, t.started_at as startsat, t.ended_at as endsat, 0 as amount, t.updated_at as updatedat from agro360.rural_hr_time_entries t left join agro360.rural_hr_people p on p.id = t.person_id and p.tenant_id = t.tenant_id where t.tenant_id=@TenantId and (@Status is null or t.status=@Status) order by t.started_at desc",
                new { tenant.TenantId, Status = status }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);
            return (IReadOnlyList<RuralHrRecord>)entries.ToArray();
        }

        var records = await c.QueryAsync<RuralHrRecord>(new CommandDefinition(
            "select id,kind,name,status,person_id personid,team_id teamid,starts_at startsat,ends_at endsat,amount,updated_at updatedat from agro360.rural_hr_records where tenant_id=@TenantId and kind=@Kind and (@Status is null or status=@Status) order by updated_at desc",
            new { tenant.TenantId, Kind = targetKind, Status = status }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);
        return (IReadOnlyList<RuralHrRecord>)records.ToArray();
    }, ct);

    public Task<Guid> SaveAsync(Guid? id, RuralHrCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        if (command.EndsAt is not null && command.StartsAt is not null)
            RuralHrRules.WorkedHours(command.StartsAt.Value, command.EndsAt.Value, 0);

        var key = id ?? Guid.NewGuid();
        var initialStatus = RuralHrRules.InitialStatus(command.Kind);

        if (id is null)
        {
            await c.ExecuteAsync(new CommandDefinition(
                "insert into agro360.rural_hr_records(id,tenant_id,kind,name,person_id,team_id,property_id,resource_id,starts_at,ends_at,amount,notes,status,created_by,updated_by) values(@Id,@TenantId,@Kind,@Name,@PersonId,@TeamId,@PropertyId,@ResourceId,@StartsAt,@EndsAt,@Amount,@Notes,@Status,@UserId,@UserId)",
                new { Id = key, tenant.TenantId, command.Kind, command.Name, command.PersonId, command.TeamId, command.PropertyId, command.ResourceId, command.StartsAt, command.EndsAt, command.Amount, command.Notes, Status = initialStatus, tenant.UserId },
                transaction: t, cancellationToken: ct)).ConfigureAwait(false);
        }
        else
        {
            var current = await c.QuerySingleOrDefaultAsync<(string Kind, string Status)>(new CommandDefinition(
                "select kind, status from agro360.rural_hr_records where tenant_id=@TenantId and id=@Id for update",
                new { tenant.TenantId, Id = id.Value }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            if (current == default || string.IsNullOrWhiteSpace(current.Kind))
                throw new KeyNotFoundException("Registro de RH não encontrado.");

            if (!string.Equals(current.Kind, command.Kind, StringComparison.OrdinalIgnoreCase))
                throw new ConflictException($"O registro informado não é do tipo '{command.Kind}'.");

            var n = await c.ExecuteAsync(new CommandDefinition(
                "update agro360.rural_hr_records set name=@Name,person_id=@PersonId,team_id=@TeamId,property_id=@PropertyId,resource_id=@ResourceId,starts_at=@StartsAt,ends_at=@EndsAt,amount=@Amount,notes=@Notes,updated_by=@UserId,updated_at=now() where tenant_id=@TenantId and id=@Id and kind=@Kind",
                new { Id = key, tenant.TenantId, command.Kind, command.Name, command.PersonId, command.TeamId, command.PropertyId, command.ResourceId, command.StartsAt, command.EndsAt, command.Amount, command.Notes, tenant.UserId },
                transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            if (n == 0) throw new KeyNotFoundException("Registro não encontrado.");
        }

        return key;
    }, ct);

    public Task<Guid> SaveGenericAsync(Guid? id, RuralHrGenericRecordCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var targetKind = (command.Kind ?? "").Trim().ToUpperInvariant();
        var key = id ?? Guid.CreateVersion7();
        var startsAt = command.StartedAt ?? command.StartsAt;
        var endsAt = command.EndedAt ?? command.EndsAt;

        if (startsAt is not null && endsAt is not null)
            RuralHrRules.WorkedHours(startsAt.Value, endsAt.Value, command.BreakMinutes);

        var initialStatus = command.Status ?? RuralHrRules.InitialStatus(targetKind);

        if (targetKind == "TIME_ENTRY")
        {
            if (command.PersonId.HasValue && endsAt is null)
            {
                var hasOpen = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                    "select exists(select 1 from agro360.rural_hr_records where tenant_id=@TenantId and person_id=@PersonId and kind='TIME_ENTRY' and ended_at is null and status not in ('CLOSED','CANCELLED'))",
                    new { tenant.TenantId, PersonId = command.PersonId.Value }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);
                if (hasOpen) throw new ArgumentException("A pessoa já possui jornada aberta.");
            }

            var propId = command.PropertyId ?? await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "select id from agro360.geo_farms where tenant_id=@TenantId and deleted_at is null limit 1",
                new { tenant.TenantId }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.rural_hr_records(
                    id, tenant_id, kind, name, person_id, team_id, property_id, resource_id,
                    starts_at, started_at, ends_at, ended_at, amount, notes, status,
                    role, activity_type, break_minutes, order_id, season_id, plot_id,
                    created_by, updated_by
                ) values(
                    @Id, @TenantId, 'TIME_ENTRY', @Name, @PersonId, @TeamId, @PropertyId, @ResourceId,
                    @StartsAt, @StartsAt, @EndsAt, @EndsAt, @Amount, @Notes, @Status,
                    @Role, @ActivityType, @BreakMinutes, @OrderId, @SeasonId, @PlotId,
                    @UserId, @UserId
                )
                """,
                new
                {
                    Id = key, tenant.TenantId, command.Name, command.PersonId, command.TeamId,
                    PropertyId = propId, command.ResourceId, StartsAt = startsAt, EndsAt = endsAt,
                    command.Amount, command.Notes, Status = initialStatus,
                    command.Role, ActivityType = command.ActivityType ?? "OPERATIONAL",
                    command.BreakMinutes, command.OrderId, command.SeasonId, command.PlotId,
                    tenant.UserId
                }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            if (command.PersonId.HasValue && propId.HasValue && startsAt.HasValue)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    """
                    insert into agro360.rural_hr_time_entries(
                        id, tenant_id, person_id, team_id, property_id, resource_id,
                        started_at, ended_at, break_minutes, activity_type, notes, status, created_by
                    ) values(
                        @Id, @TenantId, @PersonId, @TeamId, @PropertyId, @ResourceId,
                        @StartsAt, @EndsAt, @BreakMinutes, @ActivityType, @Notes,
                        case when @EndsAt is null then 'OPEN' else 'CLOSED' end, @UserId
                    ) on conflict (id) do nothing
                    """,
                    new
                    {
                        Id = key, tenant.TenantId, PersonId = command.PersonId.Value, command.TeamId,
                        PropertyId = propId.Value, command.ResourceId, StartsAt = startsAt.Value,
                        EndsAt = endsAt, command.BreakMinutes, ActivityType = command.ActivityType ?? "OPERATIONAL",
                        command.Notes, tenant.UserId
                    }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);
            }

            return key;
        }

        if (targetKind == "PERSON")
        {
            var propId = command.PropertyId ?? await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                "select id from agro360.geo_farms where tenant_id=@TenantId and deleted_at is null limit 1",
                new { tenant.TenantId }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            Guid roleId = Guid.Empty;
            if (!string.IsNullOrWhiteSpace(command.Role))
            {
                var foundRoleId = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                    "select id from agro360.rural_hr_roles where tenant_id=@TenantId and lower(name)=lower(@RoleName) limit 1",
                    new { tenant.TenantId, RoleName = command.Role.Trim() }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);
                if (foundRoleId.HasValue)
                {
                    roleId = foundRoleId.Value;
                }
                else
                {
                    roleId = Guid.CreateVersion7();
                    await c.ExecuteAsync(new CommandDefinition(
                        "insert into agro360.rural_hr_roles(id, tenant_id, name, active) values(@Id, @TenantId, @Name, true) on conflict (tenant_id, name) do nothing",
                        new { Id = roleId, tenant.TenantId, Name = command.Role.Trim() }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);
                }
            }

            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.rural_hr_records(
                    id, tenant_id, kind, name, person_id, team_id, property_id, resource_id,
                    starts_at, started_at, ends_at, ended_at, amount, notes, status,
                    role, created_by, updated_by
                ) values(
                    @Id, @TenantId, 'PERSON', @Name, null, null, @PropertyId, null,
                    null, null, null, null, 0, @Notes, @Status,
                    @Role, @UserId, @UserId
                )
                """,
                new
                {
                    Id = key, tenant.TenantId, command.Name, PropertyId = propId,
                    command.Notes, Status = initialStatus, Role = command.Role, tenant.UserId
                }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            if (propId.HasValue && roleId != Guid.Empty)
            {
                var doc = "1" + Random.Shared.Next(10000000, 99999999).ToString(CultureInfo.InvariantCulture) + "00";
                await c.ExecuteAsync(new CommandDefinition(
                    """
                    insert into agro360.rural_hr_people(
                        id, tenant_id, name, document, role_id, property_id, status, created_by, updated_by
                    ) values(
                        @Id, @TenantId, @Name, @Doc, @RoleId, @PropertyId, @Status, @UserId, @UserId
                    ) on conflict (id) do nothing
                    """,
                    new
                    {
                        Id = key, tenant.TenantId, command.Name, Doc = doc, RoleId = roleId,
                        PropertyId = propId.Value, Status = initialStatus, tenant.UserId
                    }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);
            }

            return key;
        }

        // Generic record save
        await c.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.rural_hr_records(
                id, tenant_id, kind, name, person_id, team_id, property_id, resource_id,
                starts_at, started_at, ends_at, ended_at, amount, notes, status,
                role, activity_type, break_minutes, order_id, season_id, plot_id,
                created_by, updated_by
            ) values(
                @Id, @TenantId, @Kind, @Name, @PersonId, @TeamId, @PropertyId, @ResourceId,
                @StartsAt, @StartsAt, @EndsAt, @EndsAt, @Amount, @Notes, @Status,
                @Role, @ActivityType, @BreakMinutes, @OrderId, @SeasonId, @PlotId,
                @UserId, @UserId
            )
            """,
            new
            {
                Id = key, tenant.TenantId, Kind = targetKind, command.Name, command.PersonId, command.TeamId,
                command.PropertyId, command.ResourceId, StartsAt = startsAt, EndsAt = endsAt,
                command.Amount, command.Notes, Status = initialStatus,
                command.Role, command.ActivityType, command.BreakMinutes, command.OrderId,
                command.SeasonId, command.PlotId, tenant.UserId
            }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

        return key;
    }, ct);

    public Task<Guid> AddPersonAsync(PersonCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) => { if (await c.ExecuteScalarAsync<bool>("select exists(select 1 from agro360.rural_hr_people where tenant_id=@TenantId and document=@Document)", new { tenant.TenantId, command.Document }, t)) throw new ArgumentException("Documento já cadastrado neste tenant."); var id = Guid.NewGuid(); await c.ExecuteAsync("insert into agro360.rural_hr_people(id,tenant_id,name,document,role_id,property_id,email,phone,skills,status,created_by,updated_by) values(@Id,@TenantId,@Name,@Document,@RoleId,@PropertyId,@Email,@Phone,@Skills::jsonb,'ACTIVE',@UserId,@UserId)", new { Id = id, tenant.TenantId, command.Name, command.Document, command.RoleId, command.PropertyId, command.Email, command.Phone, Skills = JsonSerializer.Serialize((command.Skills ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)), tenant.UserId }, t); return id; }, ct);
    public Task<Guid> RegisterTimeAsync(TimeEntryCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) => { if (command.EndedAt is not null) RuralHrRules.WorkedHours(command.StartedAt, command.EndedAt.Value, command.BreakMinutes); if (await c.ExecuteScalarAsync<bool>("select exists(select 1 from agro360.rural_hr_time_entries where tenant_id=@TenantId and person_id=@PersonId and ended_at is null)", new { tenant.TenantId, command.PersonId }, t)) throw new ArgumentException("A pessoa já possui jornada aberta."); var id = Guid.NewGuid(); await c.ExecuteAsync("insert into agro360.rural_hr_time_entries(id,tenant_id,person_id,team_id,property_id,resource_id,started_at,ended_at,break_minutes,activity_type,notes,offline_id,status,created_by) values(@Id,@TenantId,@PersonId,@TeamId,@PropertyId,@ResourceId,@StartedAt,@EndedAt,@BreakMinutes,@ActivityType,@Notes,@OfflineId,case when @EndedAt is null then 'OPEN' else 'CLOSED' end,@UserId)", new { Id = id, tenant.TenantId, command.PersonId, command.TeamId, command.PropertyId, command.ResourceId, command.StartedAt, command.EndedAt, command.BreakMinutes, command.ActivityType, command.Notes, command.OfflineId, tenant.UserId }, t); return id; }, ct);

    public Task EndTimeAsync(Guid id, DateTimeOffset endedAt, CancellationToken ct) => db.InTenantTransactionAsync((c, t) => EndTimeInternalAsync(c, t, id, endedAt, null, ct), ct);
    public Task EndTimeAsync(Guid id, DateTimeOffset? endedAt, string? justification, CancellationToken ct) => db.InTenantTransactionAsync((c, t) => EndTimeInternalAsync(c, t, id, endedAt ?? DateTimeOffset.UtcNow, justification, ct), ct);

    private async Task EndTimeInternalAsync(System.Data.Common.DbConnection c, System.Data.Common.DbTransaction t, Guid id, DateTimeOffset endedAt, string? justification, CancellationToken ct)
    {
        var record = await c.QuerySingleOrDefaultAsync<(string Kind, string Status, DateTimeOffset? StartedAt, int BreakMinutes)>(new CommandDefinition(
            "select kind as Kind, status as Status, coalesce(started_at, starts_at) as StartedAt, break_minutes as BreakMinutes from agro360.rural_hr_records where tenant_id=@TenantId and id=@Id for update",
            new { tenant.TenantId, Id = id }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

        if (record != default && !string.IsNullOrWhiteSpace(record.Kind))
        {
            if (string.Equals(record.Status, "CLOSED", StringComparison.OrdinalIgnoreCase))
                throw new ConflictException("Esta jornada já foi encerrada.");

            var st = record.StartedAt ?? endedAt.AddHours(-1);
            RuralHrRules.WorkedHours(st, endedAt, record.BreakMinutes);

            var noteSuffix = string.IsNullOrWhiteSpace(justification) ? "" : $" [Encerramento: {justification.Trim()}]";
            var updated = await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.rural_hr_records
                   set status = 'CLOSED', ended_at = @EndedAt, ends_at = @EndedAt,
                       notes = coalesce(notes, '') || @NoteSuffix,
                       updated_by = @UserId, updated_at = now()
                 where tenant_id = @TenantId and id = @Id and status != 'CLOSED'
                """,
                new { tenant.TenantId, Id = id, EndedAt = endedAt, NoteSuffix = noteSuffix, tenant.UserId }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.rural_hr_time_entries
                   set status = 'CLOSED', ended_at = @EndedAt, updated_at = now()
                 where tenant_id = @TenantId and id = @Id and status != 'CLOSED'
                """,
                new { tenant.TenantId, Id = id, EndedAt = endedAt }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            if (updated == 0) throw new ConflictException("Concorrência ao encerrar jornada.");
            return;
        }

        var entry = await c.QuerySingleOrDefaultAsync<(DateTimeOffset StartedAt, int BreakMinutes, string Status)>(new CommandDefinition(
            "select started_at as StartedAt, break_minutes as BreakMinutes, status as Status from agro360.rural_hr_time_entries where tenant_id=@TenantId and id=@Id for update",
            new { tenant.TenantId, Id = id }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

        if (entry == default) throw new KeyNotFoundException("Jornada aberta não encontrada.");
        if (entry.Status == "CLOSED") throw new ConflictException("Esta jornada já foi encerrada.");

        RuralHrRules.WorkedHours(entry.StartedAt, endedAt, entry.BreakMinutes);

        var updatedEntry = await c.ExecuteAsync(new CommandDefinition(
            "update agro360.rural_hr_time_entries set ended_at=@EndedAt,status='CLOSED',updated_at=now() where tenant_id=@TenantId and id=@Id and status!='CLOSED'",
            new { tenant.TenantId, Id = id, EndedAt = endedAt }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

        if (updatedEntry == 0) throw new ConflictException("Concorrência ao encerrar jornada.");
    }

    public Task<Guid> AddTransportAsync(TransportCommand command, CancellationToken ct) { RuralHrRules.EnsureCapacity(command.Capacity, command.PassengerCount); return SaveAsync(null, new("TRANSPORT", command.Name, command.DriverId, command.TeamId, null, command.VehicleId, command.StartsAt, command.EndsAt, command.PassengerCount, command.Route), ct); }

    public Task ChangeStatusAsync(Guid id, string kind, string status, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var targetKind = (kind ?? "").Trim().ToUpperInvariant();
        var targetStatus = (status ?? "").Trim().ToUpperInvariant();

        if (targetKind == "TIME_ENTRY" && targetStatus == "CLOSED")
        {
            await EndTimeInternalAsync(c, t, id, DateTimeOffset.UtcNow, null, ct).ConfigureAwait(false);
            return;
        }

        if (targetKind == "PERSON")
        {
            var current = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                "select status from agro360.rural_hr_people where tenant_id=@TenantId and id=@Id for update",
                new { tenant.TenantId, Id = id }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            if (current is null)
            {
                current = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                    "select status from agro360.rural_hr_records where tenant_id=@TenantId and id=@Id and kind='PERSON' for update",
                    new { tenant.TenantId, Id = id }, transaction: t, cancellationToken: ct)).ConfigureAwait(false)
                    ?? throw new KeyNotFoundException("Pessoa não encontrada.");
            }

            RuralHrRules.ValidateStatusTransition("PERSON", current, targetStatus);

            await c.ExecuteAsync(new CommandDefinition(
                "update agro360.rural_hr_people set status=@Status,updated_by=@UserId,updated_at=now() where tenant_id=@TenantId and id=@Id and status=@Current",
                new { tenant.TenantId, Id = id, Status = targetStatus, Current = current, tenant.UserId }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            await c.ExecuteAsync(new CommandDefinition(
                "update agro360.rural_hr_records set status=@Status,updated_by=@UserId,updated_at=now() where tenant_id=@TenantId and id=@Id and kind='PERSON' and status=@Current",
                new { tenant.TenantId, Id = id, Status = targetStatus, Current = current, tenant.UserId }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            return;
        }
        else if (targetKind == "TIME_ENTRY")
        {
            var current = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                "select status from agro360.rural_hr_time_entries where tenant_id=@TenantId and id=@Id for update",
                new { tenant.TenantId, Id = id }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            if (current is null)
            {
                current = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                    "select status from agro360.rural_hr_records where tenant_id=@TenantId and id=@Id and kind='TIME_ENTRY' for update",
                    new { tenant.TenantId, Id = id }, transaction: t, cancellationToken: ct)).ConfigureAwait(false)
                    ?? throw new KeyNotFoundException("Jornada não encontrada.");
            }

            RuralHrRules.ValidateStatusTransition("TIME_ENTRY", current, targetStatus);

            await c.ExecuteAsync(new CommandDefinition(
                "update agro360.rural_hr_time_entries set status=@Status,updated_at=now() where tenant_id=@TenantId and id=@Id and status=@Current",
                new { tenant.TenantId, Id = id, Status = targetStatus, Current = current }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            await c.ExecuteAsync(new CommandDefinition(
                "update agro360.rural_hr_records set status=@Status,updated_at=now() where tenant_id=@TenantId and id=@Id and kind='TIME_ENTRY' and status=@Current",
                new { tenant.TenantId, Id = id, Status = targetStatus, Current = current }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);
            return;
        }
        else
        {
            var record = await c.QuerySingleOrDefaultAsync<(string Kind, string Status)>(new CommandDefinition(
                "select kind, status from agro360.rural_hr_records where tenant_id=@TenantId and id=@Id for update",
                new { tenant.TenantId, Id = id }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            if (record == default || string.IsNullOrWhiteSpace(record.Kind))
                throw new KeyNotFoundException("Registro de RH não encontrado.");

            if (!string.Equals(record.Kind, targetKind, StringComparison.OrdinalIgnoreCase))
                throw new ConflictException($"O registro informado não é do tipo '{targetKind}' (tipo cadastrado: '{record.Kind}').");

            RuralHrRules.ValidateStatusTransition(record.Kind, record.Status, targetStatus);

            var updated = await c.ExecuteAsync(new CommandDefinition(
                "update agro360.rural_hr_records set status=@Status,updated_by=@UserId,updated_at=now() where tenant_id=@TenantId and id=@Id and kind=@Kind and status=@Current",
                new { tenant.TenantId, Id = id, Kind = record.Kind, Status = targetStatus, Current = record.Status, tenant.UserId }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            if (updated == 0) throw new ConflictException("O status do registro foi alterado concorrentemente.");
        }
    }, ct);

    public Task ChangeStatusAsync(Guid id, string status, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var targetStatus = (status ?? "").Trim().ToUpperInvariant();

        // 1. Check rural_hr_records
        var record = await c.QuerySingleOrDefaultAsync<(string Kind, string Status)>(new CommandDefinition(
            "select kind, status from agro360.rural_hr_records where tenant_id=@TenantId and id=@Id for update",
            new { tenant.TenantId, Id = id }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

        if (record != default && !string.IsNullOrWhiteSpace(record.Kind))
        {
            if (string.Equals(record.Kind, "TIME_ENTRY", StringComparison.OrdinalIgnoreCase) && targetStatus == "CLOSED")
            {
                await EndTimeInternalAsync(c, t, id, DateTimeOffset.UtcNow, null, ct).ConfigureAwait(false);
                return;
            }

            RuralHrRules.ValidateStatusTransition(record.Kind, record.Status, targetStatus);
            var updated = await c.ExecuteAsync(new CommandDefinition(
                "update agro360.rural_hr_records set status=@Status,updated_by=@UserId,updated_at=now() where tenant_id=@TenantId and id=@Id and status=@Current",
                new { tenant.TenantId, Id = id, Status = targetStatus, Current = record.Status, tenant.UserId }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            if (updated == 0) throw new ConflictException("O status do registro foi alterado concorrentemente.");
            return;
        }

        // 2. Check rural_hr_time_entries
        var entryExists = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from agro360.rural_hr_time_entries where tenant_id=@TenantId and id=@Id)",
            new { tenant.TenantId, Id = id }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

        if (entryExists)
        {
            if (targetStatus == "CLOSED")
            {
                await EndTimeInternalAsync(c, t, id, DateTimeOffset.UtcNow, null, ct).ConfigureAwait(false);
                return;
            }

            var current = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                "select status from agro360.rural_hr_time_entries where tenant_id=@TenantId and id=@Id for update",
                new { tenant.TenantId, Id = id }, transaction: t, cancellationToken: ct)).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Jornada não encontrada.");

            RuralHrRules.ValidateStatusTransition("TIME_ENTRY", current, targetStatus);
            var updated = await c.ExecuteAsync(new CommandDefinition(
                "update agro360.rural_hr_time_entries set status=@Status,updated_at=now() where tenant_id=@TenantId and id=@Id and status=@Current",
                new { tenant.TenantId, Id = id, Status = targetStatus, Current = current }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            if (updated == 0) throw new ConflictException("O status da jornada foi alterado concorrentemente.");
            return;
        }

        // 3. Check rural_hr_people
        var personStatus = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "select status from agro360.rural_hr_people where tenant_id=@TenantId and id=@Id for update",
            new { tenant.TenantId, Id = id }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

        if (personStatus is not null)
        {
            RuralHrRules.ValidateStatusTransition("PERSON", personStatus, targetStatus);
            var updated = await c.ExecuteAsync(new CommandDefinition(
                "update agro360.rural_hr_people set status=@Status,updated_by=@UserId,updated_at=now() where tenant_id=@TenantId and id=@Id and status=@Current",
                new { tenant.TenantId, Id = id, Status = targetStatus, Current = personStatus, tenant.UserId }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            if (updated == 0) throw new ConflictException("O status da pessoa foi alterado concorrentemente.");
            return;
        }

        throw new KeyNotFoundException("Registro não encontrado.");
    }, ct);

    public Task<IReadOnlyList<LookupOption>> LookupAsync(string kind, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var k = (kind ?? "").Trim().ToLowerInvariant();
        var (sql, param) = k switch
        {
            "people" => (
                """
                select id, name as label from agro360.rural_hr_people where tenant_id=@TenantId and status='ACTIVE'
                union
                select id, name as label from agro360.rural_hr_records where tenant_id=@TenantId and kind='PERSON' and status='ACTIVE'
                order by label limit 100
                """, (object)new { tenant.TenantId }),
            "teams" => ("select id, name as label from agro360.rural_hr_records where tenant_id=@TenantId and kind='TEAM' and status in ('ACTIVE','IN_FIELD') order by name limit 100", (object)new { tenant.TenantId }),
            "properties" => ("select id, name as label from agro360.geo_farms where tenant_id=@TenantId and deleted_at is null order by name limit 100", (object)new { tenant.TenantId }),
            "roles" => (
                """
                select id, name as label from agro360.rural_hr_roles where tenant_id=@TenantId and active
                union
                select distinct gen_random_uuid() as id, role as label from agro360.rural_hr_records where tenant_id=@TenantId and role is not null and trim(role) != ''
                order by label limit 100
                """, (object)new { tenant.TenantId }),
            "seasons" => ("select id, name || ' (' || crop || ')' as label from agro360.agriculture_seasons where tenant_id=@TenantId and deleted_at is null order by name limit 100", (object)new { tenant.TenantId }),
            "plots" => ("select id, name as label from agro360.geo_plots where tenant_id=@TenantId and deleted_at is null order by name limit 100", (object)new { tenant.TenantId }),
            "work-orders" => ("select id, number || ' - ' || activity_type as label from agro360.agriculture_work_orders where tenant_id=@TenantId and deleted_at is null order by number desc limit 100", (object)new { tenant.TenantId }),
            "operational-resources" or "resources" => (
                """
                select id, name as label from agro360.geo_plots where tenant_id=@TenantId and deleted_at is null
                union all
                select id, name as label from agro360.regional_logistics_vehicles where tenant_id=@TenantId and active
                order by label limit 100
                """, (object)new { tenant.TenantId }),
            "vehicles" => ("select id, name || ' (' || coalesce(registration, type) || ')' as label from agro360.regional_logistics_vehicles where tenant_id=@TenantId and active order by name limit 100", (object)new { tenant.TenantId }),
            _ => throw new DomainException($"Tipo de consulta '{kind}' não suportado.", "rural_hr.invalid_lookup")
        };

        var items = await c.QueryAsync<LookupOption>(new CommandDefinition(sql, param, transaction: t, cancellationToken: ct)).ConfigureAwait(false);
        return (IReadOnlyList<LookupOption>)items.ToArray();
    }, ct);

    public Task<RuralHrDashboard> DashboardAsync(CancellationToken ct) => db.InTenantTransactionAsync((c, t) => c.QuerySingleAsync<RuralHrDashboard>("select (select count(*) from agro360.rural_hr_people where tenant_id=@TenantId and status='ACTIVE') activepeople,count(*) filter(where kind='TEAM' and status='ACTIVE') activeteams,coalesce((select sum(extract(epoch from(coalesce(ended_at,now())-started_at))/3600-break_minutes/60.0) from agro360.rural_hr_time_entries where tenant_id=@TenantId),0) workedhours,coalesce(sum(amount) filter(where kind='LABOR_COST'),0) laborcost,count(*) filter(where kind='TRAINING' and ends_at<now()) expiredtrainings,count(*) filter(where kind='PPE' and ends_at<now()) expiredppe,count(*) filter(where kind='INCIDENT' and status not in('CLOSED','COMPLETED')) openincidents,count(*) filter(where kind='CORRECTIVE_ACTION' and ends_at<now() and status!='COMPLETED') overdueactions,count(*) filter(where kind='TEAM' and status='IN_FIELD') teamsinfield,count(*) filter(where kind in('INCIDENT','RISK','PPE','TRAINING') and status='CRITICAL') criticalalerts from agro360.rural_hr_records where tenant_id=@TenantId", new { tenant.TenantId }, t), ct);
    public async Task<byte[]> ExportAsync(string kind, CancellationToken ct) { var rows = await ListAsync(kind, null, ct); var csv = new StringBuilder("Nome;Status;Inicio;Fim;Valor\n"); foreach (var x in rows) csv.AppendLine(CultureInfo.InvariantCulture, $"{CsvSanitizer.Sanitize(x.Name.Replace(';', ','))};{CsvSanitizer.Sanitize(x.Status)};{x.StartsAt:O};{x.EndsAt:O};{x.Amount.ToString(CultureInfo.InvariantCulture)}"); return Encoding.UTF8.GetBytes(csv.ToString()); }
}

