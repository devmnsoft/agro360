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

    public Task<Guid> AddPersonAsync(PersonCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) => { if (await c.ExecuteScalarAsync<bool>("select exists(select 1 from agro360.rural_hr_people where tenant_id=@TenantId and document=@Document)", new { tenant.TenantId, command.Document }, t)) throw new ArgumentException("Documento já cadastrado neste tenant."); var id = Guid.NewGuid(); await c.ExecuteAsync("insert into agro360.rural_hr_people(id,tenant_id,name,document,role_id,property_id,email,phone,skills,status,created_by,updated_by) values(@Id,@TenantId,@Name,@Document,@RoleId,@PropertyId,@Email,@Phone,@Skills::jsonb,'ACTIVE',@UserId,@UserId)", new { Id = id, tenant.TenantId, command.Name, command.Document, command.RoleId, command.PropertyId, command.Email, command.Phone, Skills = JsonSerializer.Serialize((command.Skills ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries)), tenant.UserId }, t); return id; }, ct);
    public Task<Guid> RegisterTimeAsync(TimeEntryCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) => { if (command.EndedAt is not null) RuralHrRules.WorkedHours(command.StartedAt, command.EndedAt.Value, command.BreakMinutes); if (await c.ExecuteScalarAsync<bool>("select exists(select 1 from agro360.rural_hr_time_entries where tenant_id=@TenantId and person_id=@PersonId and ended_at is null)", new { tenant.TenantId, command.PersonId }, t)) throw new ArgumentException("A pessoa já possui jornada aberta."); var id = Guid.NewGuid(); await c.ExecuteAsync("insert into agro360.rural_hr_time_entries(id,tenant_id,person_id,team_id,property_id,resource_id,started_at,ended_at,break_minutes,activity_type,notes,offline_id,status,created_by) values(@Id,@TenantId,@PersonId,@TeamId,@PropertyId,@ResourceId,@StartedAt,@EndedAt,@BreakMinutes,@ActivityType,@Notes,@OfflineId,case when @EndedAt is null then 'OPEN' else 'CLOSED' end,@UserId)", new { Id = id, tenant.TenantId, command.PersonId, command.TeamId, command.PropertyId, command.ResourceId, command.StartedAt, command.EndedAt, command.BreakMinutes, command.ActivityType, command.Notes, command.OfflineId, tenant.UserId }, t); return id; }, ct);

    public Task EndTimeAsync(Guid id, DateTimeOffset endedAt, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var entry = await c.QuerySingleOrDefaultAsync<(DateTimeOffset StartedAt, int BreakMinutes, string Status)>(new CommandDefinition(
            "select started_at as StartedAt, break_minutes as BreakMinutes, status as Status from agro360.rural_hr_time_entries where tenant_id=@TenantId and id=@Id for update",
            new { tenant.TenantId, Id = id }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

        if (entry == default) throw new KeyNotFoundException("Jornada aberta não encontrada.");
        if (entry.Status == "CLOSED") throw new ConflictException("Esta jornada já foi encerrada.");

        RuralHrRules.WorkedHours(entry.StartedAt, endedAt, entry.BreakMinutes);

        var updated = await c.ExecuteAsync(new CommandDefinition(
            "update agro360.rural_hr_time_entries set ended_at=@EndedAt,status='CLOSED',updated_at=now() where tenant_id=@TenantId and id=@Id and status='OPEN'",
            new { tenant.TenantId, Id = id, EndedAt = endedAt }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

        if (updated == 0) throw new ConflictException("Concorrência ao encerrar jornada.");
    }, ct);

    public Task<Guid> AddTransportAsync(TransportCommand command, CancellationToken ct) { RuralHrRules.EnsureCapacity(command.Capacity, command.PassengerCount); return SaveAsync(null, new("TRANSPORT", command.Name, command.DriverId, command.TeamId, null, command.VehicleId, command.StartsAt, command.EndsAt, command.PassengerCount, command.Route), ct); }

    public Task ChangeStatusAsync(Guid id, string kind, string status, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var targetKind = (kind ?? "").Trim().ToUpperInvariant();
        var targetStatus = (status ?? "").Trim().ToUpperInvariant();

        if (targetKind == "PERSON")
        {
            var current = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                "select status from agro360.rural_hr_people where tenant_id=@TenantId and id=@Id for update",
                new { tenant.TenantId, Id = id }, transaction: t, cancellationToken: ct)).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Pessoa não encontrada.");

            RuralHrRules.ValidateStatusTransition("PERSON", current, targetStatus);

            var updated = await c.ExecuteAsync(new CommandDefinition(
                "update agro360.rural_hr_people set status=@Status,updated_by=@UserId,updated_at=now() where tenant_id=@TenantId and id=@Id and status=@Current",
                new { tenant.TenantId, Id = id, Status = targetStatus, Current = current, tenant.UserId }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            if (updated == 0) throw new ConflictException("O status da pessoa foi alterado concorrentemente.");
        }
        else if (targetKind == "TIME_ENTRY")
        {
            var current = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
                "select status from agro360.rural_hr_time_entries where tenant_id=@TenantId and id=@Id for update",
                new { tenant.TenantId, Id = id }, transaction: t, cancellationToken: ct)).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Jornada não encontrada.");

            RuralHrRules.ValidateStatusTransition("TIME_ENTRY", current, targetStatus);

            var updated = await c.ExecuteAsync(new CommandDefinition(
                "update agro360.rural_hr_time_entries set status=@Status,updated_at=now() where tenant_id=@TenantId and id=@Id and status=@Current",
                new { tenant.TenantId, Id = id, Status = targetStatus, Current = current }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            if (updated == 0) throw new ConflictException("O status da jornada foi alterado concorrentemente.");
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
            RuralHrRules.ValidateStatusTransition(record.Kind, record.Status, targetStatus);
            var updated = await c.ExecuteAsync(new CommandDefinition(
                "update agro360.rural_hr_records set status=@Status,updated_by=@UserId,updated_at=now() where tenant_id=@TenantId and id=@Id and status=@Current",
                new { tenant.TenantId, Id = id, Status = targetStatus, Current = record.Status, tenant.UserId }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            if (updated == 0) throw new ConflictException("O status do registro foi alterado concorrentemente.");
            return;
        }

        // 2. Check rural_hr_people
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

        // 3. Check rural_hr_time_entries
        var entryStatus = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "select status from agro360.rural_hr_time_entries where tenant_id=@TenantId and id=@Id for update",
            new { tenant.TenantId, Id = id }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

        if (entryStatus is not null)
        {
            RuralHrRules.ValidateStatusTransition("TIME_ENTRY", entryStatus, targetStatus);
            var updated = await c.ExecuteAsync(new CommandDefinition(
                "update agro360.rural_hr_time_entries set status=@Status,updated_at=now() where tenant_id=@TenantId and id=@Id and status=@Current",
                new { tenant.TenantId, Id = id, Status = targetStatus, Current = entryStatus }, transaction: t, cancellationToken: ct)).ConfigureAwait(false);

            if (updated == 0) throw new ConflictException("O status da jornada foi alterado concorrentemente.");
            return;
        }

        throw new KeyNotFoundException("Registro não encontrado.");
    }, ct);

    public Task<RuralHrDashboard> DashboardAsync(CancellationToken ct) => db.InTenantTransactionAsync((c, t) => c.QuerySingleAsync<RuralHrDashboard>("select (select count(*) from agro360.rural_hr_people where tenant_id=@TenantId and status='ACTIVE') activepeople,count(*) filter(where kind='TEAM' and status='ACTIVE') activeteams,coalesce((select sum(extract(epoch from(coalesce(ended_at,now())-started_at))/3600-break_minutes/60.0) from agro360.rural_hr_time_entries where tenant_id=@TenantId),0) workedhours,coalesce(sum(amount) filter(where kind='LABOR_COST'),0) laborcost,count(*) filter(where kind='TRAINING' and ends_at<now()) expiredtrainings,count(*) filter(where kind='PPE' and ends_at<now()) expiredppe,count(*) filter(where kind='INCIDENT' and status not in('CLOSED','COMPLETED')) openincidents,count(*) filter(where kind='CORRECTIVE_ACTION' and ends_at<now() and status!='COMPLETED') overdueactions,count(*) filter(where kind='TEAM' and status='IN_FIELD') teamsinfield,count(*) filter(where kind in('INCIDENT','RISK','PPE','TRAINING') and status='CRITICAL') criticalalerts from agro360.rural_hr_records where tenant_id=@TenantId", new { tenant.TenantId }, t), ct);
    public async Task<byte[]> ExportAsync(string kind, CancellationToken ct) { var rows = await ListAsync(kind, null, ct); var csv = new StringBuilder("Nome;Status;Inicio;Fim;Valor\n"); foreach (var x in rows) csv.AppendLine(CultureInfo.InvariantCulture, $"{CsvSanitizer.Sanitize(x.Name.Replace(';', ','))};{CsvSanitizer.Sanitize(x.Status)};{x.StartsAt:O};{x.EndsAt:O};{x.Amount.ToString(CultureInfo.InvariantCulture)}"); return Encoding.UTF8.GetBytes(csv.ToString()); }
}

