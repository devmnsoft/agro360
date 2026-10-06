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

public sealed partial class RuralHrService(DatabaseExecutor db, ITenantContext tenant) : IRuralHrService
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
                "select t.id, 'TIME_ENTRY' as kind, coalesce(p.name, 'Jornada') as name, t.status, t.person_id as personid, t.team_id as teamid, t.started_at as startsat, t.ended_at as endsat, coalesce(t.cost_amount, 0) as amount, t.updated_at as updatedat, t.cost_status as coststate, t.allocation_id as allocationid, t.cost_block as blockreason, t.review_status as reviewstatus from agro360.rural_hr_time_entries t left join agro360.rural_hr_people p on p.id = t.person_id and p.tenant_id = t.tenant_id where t.tenant_id=@TenantId and (@Status is null or t.status=@Status) order by t.started_at desc",
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
        await EnsureSavedAllocationAsync(c, t, id, command, ct).ConfigureAwait(false);

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


    public Task EndTimeAsync(Guid id, DateTimeOffset endedAt, CancellationToken ct) => db.InTenantTransactionAsync((c, t) => EndTimeInternalAsync(c, t, id, endedAt, null, ct), ct);
    public Task EndTimeAsync(Guid id, DateTimeOffset? endedAt, string? justification, CancellationToken ct) => db.InTenantTransactionAsync((c, t) => EndTimeInternalAsync(c, t, id, endedAt ?? DateTimeOffset.UtcNow, justification, ct), ct);

    private async Task EndTimeInternalAsync(System.Data.Common.DbConnection c, System.Data.Common.DbTransaction t, Guid id, DateTimeOffset endedAt, string? justification, CancellationToken ct)
    {
        var entry = await LockEntryAsync(c, t, id, ct).ConfigureAwait(false);
        if (string.Equals(entry.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Jornada cancelada não é encerrada por este comando.");
        if (string.Equals(entry.Status, "CLOSED", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Esta jornada já foi encerrada.");
        decimal hours;
        try
        {
            hours = RuralHrRules.WorkedHours(entry.StartedAt, endedAt, entry.BreakMinutes);
        }
        catch (ArgumentException ex)
        {
            throw new DomainException(ex.Message, "rural_hr.journey_invalid");
        }
        await EnsureJourneyIntervalAsync(c, t, entry.PersonId, entry.StartedAt, endedAt, id, ct).ConfigureAwait(false);
        var overrun = await PlanOverrunForEntryAsync(c, t, id, entry.StartedAt, endedAt, ct).ConfigureAwait(false);
        var noteSuffix = string.IsNullOrWhiteSpace(justification) ? "" : $" [Encerramento: {justification.Trim()}]";
        var updated = await c.ExecuteAsync(new CommandDefinition(
            """
            update agro360.rural_hr_time_entries
               set status='CLOSED', ended_at=@EndedAt, hours_worked=@Hours, plan_overrun=@Overrun, updated_at=now()
             where tenant_id=@TenantId and id=@Id and status='OPEN'
            """,
            new { tenant.TenantId, Id = id, EndedAt = endedAt, Hours = hours, Overrun = overrun }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);
        if (updated == 0) throw new ConflictException("Concorrência ao encerrar jornada.");
        if (noteSuffix.Length > 0)
        {
            await c.ExecuteAsync(new CommandDefinition(
                "update agro360.rural_hr_records set notes=coalesce(notes, '') || @Note where tenant_id=@TenantId and id=@Id and kind='TIME_ENTRY'",
                new { tenant.TenantId, Id = id, Note = noteSuffix }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);
        }
        await UpsertJourneyProjectionAsync(c, t, id, ct).ConfigureAwait(false);
        await SnapshotLaborAsync(c, t, id, ct).ConfigureAwait(false);
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

    public Task<IReadOnlyList<LookupOption>> LookupAsync(string kind, CancellationToken ct) => LookupAsync(kind, null, ct);

    public Task<IReadOnlyList<LookupOption>> LookupAsync(string kind, Guid? scopeId, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var k = (kind ?? "").Trim().ToLowerInvariant();
        var scope = scopeId ?? (k is "properties" or "plots" or "seasons" or "work-orders" or "allocations" ? tenant.FarmId : null);
        var param = new { tenant.TenantId, Scope = scope };
        var sql = k switch
        {
            "people" => "select id, name as label from agro360.rural_hr_people where tenant_id=@TenantId and status='ACTIVE' order by name limit 100",
            "teams" => "select id, name as label from agro360.rural_hr_records where tenant_id=@TenantId and kind='TEAM' and status in ('ACTIVE','IN_FIELD') order by name limit 100",
            "properties" => "select id, name as label from agro360.geo_farms where tenant_id=@TenantId and deleted_at is null and (@Scope is null or id=@Scope) order by name limit 100",
            "roles" => "select id, name as label from agro360.rural_hr_roles where tenant_id=@TenantId and active order by name limit 100",
            "seasons" => "select id, name || ' (' || crop || ')' as label from agro360.agriculture_seasons where tenant_id=@TenantId and deleted_at is null and status not in (4, 5) and (@Scope is null or farm_id=@Scope) order by name limit 100",
            "plots" => "select id, name as label from agro360.geo_fields where tenant_id=@TenantId and deleted_at is null and (@Scope is null or farm_id=@Scope) order by name limit 100",
            "work-orders" => "select id, coalesce(data->>'number', data->>'name', 'Ordem de campo') as label from agro360.agriculture_records where tenant_id=@TenantId and module='work-orders' and deleted_at is null and status not in ('CANCELLED','CLOSED') and (@Scope is null or data->>'propertyId'=cast(@Scope as text)) order by created_at desc limit 100",
            "allocations" => "select id, name as label from agro360.rural_hr_records where tenant_id=@TenantId and kind='ALLOCATION' and status='ACTIVE' and (@Scope is null or property_id=@Scope) order by starts_at desc limit 100",
            "operational-resources" or "resources" => "select id, name as label from agro360.geo_fields where tenant_id=@TenantId and deleted_at is null and (@Scope is null or farm_id=@Scope) order by name limit 100",
            "vehicles" => "select id, name || ' (' || coalesce(registration, type) || ')' as label from agro360.regional_logistics_vehicles where tenant_id=@TenantId and active order by name limit 100",
            _ => throw new DomainException($"Tipo de consulta '{kind}' não suportado.", "rural_hr.invalid_lookup")
        };
        var items = await c.QueryAsync<LookupOption>(new CommandDefinition(sql, param, transaction: t, cancellationToken: ct)).ConfigureAwait(false);
        return (IReadOnlyList<LookupOption>)items.ToArray();
    }, ct);

    public Task<RuralHrDashboard> DashboardAsync(CancellationToken ct) => db.InTenantTransactionAsync((c, t) => c.QuerySingleAsync<RuralHrDashboard>("select (select count(*) from agro360.rural_hr_people where tenant_id=@TenantId and status='ACTIVE') activepeople,count(*) filter(where kind='TEAM' and status='ACTIVE') activeteams,coalesce((select sum(extract(epoch from(coalesce(ended_at,now())-started_at))/3600-break_minutes/60.0) from agro360.rural_hr_time_entries where tenant_id=@TenantId),0) workedhours,coalesce(sum(amount) filter(where kind='LABOR_COST'),0)+coalesce((select sum(cost_amount) from agro360.rural_hr_time_entries te where te.tenant_id=@TenantId and te.review_status='CONFIRMED' and te.cost_amount is not null),0) laborcost,count(*) filter(where kind='TRAINING' and ends_at<now()) expiredtrainings,count(*) filter(where kind='PPE' and ends_at<now()) expiredppe,count(*) filter(where kind='INCIDENT' and status not in('CLOSED','COMPLETED')) openincidents,count(*) filter(where kind='CORRECTIVE_ACTION' and ends_at<now() and status!='COMPLETED') overdueactions,count(*) filter(where kind='TEAM' and status='IN_FIELD') teamsinfield,count(*) filter(where kind in('INCIDENT','RISK','PPE','TRAINING') and status='CRITICAL') criticalalerts from agro360.rural_hr_records where tenant_id=@TenantId", new { tenant.TenantId }, t), ct);
    public async Task<byte[]> ExportAsync(string kind, CancellationToken ct)
    {
        var rows = await ListAsync(kind, null, ct);
        var csv = new StringBuilder("Nome;Status;Inicio;Fim;Valor;SituacaoCusto\n");
        foreach (var x in rows)
        {
            var unavailable = string.Equals(x.CostState, "UNAVAILABLE", StringComparison.OrdinalIgnoreCase);
            var amount = unavailable ? "" : x.Amount.ToString(CultureInfo.InvariantCulture);
            var state = x.CostState switch { "UNAVAILABLE" => "indisponivel", "CALCULATED" => "calculado", _ => "" };
            csv.AppendLine(CultureInfo.InvariantCulture, $"{CsvSanitizer.Sanitize(x.Name.Replace(';', ','))};{CsvSanitizer.Sanitize(x.Status)};{x.StartsAt:O};{x.EndsAt:O};{amount};{state}");
        }
        return Encoding.UTF8.GetBytes(csv.ToString());
    }
}

