using System.Data.Common;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Agro360.Application.Contracts;
using Agro360.Domain.People;
using Agro360.SharedKernel;
using Dapper;
using Npgsql;

namespace Agro360.Infrastructure.Services;

public sealed partial class RuralHrService
{
    private const string RoundingRule = "4 casas; AwayFromZero";

    public Task<Guid> SaveGenericAsync(Guid? id, RuralHrGenericRecordCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var targetKind = (command.Kind ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(command.Name)) throw new DomainException("Informe o nome.", "rural_hr.name_required");
        var startsAt = command.StartedAt ?? command.StartsAt;
        var endsAt = command.EndedAt ?? command.EndsAt;
        if (startsAt is not null && endsAt is not null)
            RuralHrRules.WorkedHours(startsAt.Value, endsAt.Value, command.BreakMinutes);

        if (targetKind == "PERSON")
        {
            if (string.IsNullOrWhiteSpace(command.Document))
                throw new DomainException("Informe o documento real da pessoa. O cadastro não gera documento.", "rural_hr.document_required");
            if (string.IsNullOrWhiteSpace(command.Role))
                throw new DomainException("Informe o cargo pelo nome do cadastro canônico.", "rural_hr.role_required");
            var propertyId = await ResolvePropertyAsync(c, t, command.PropertyId, ct).ConfigureAwait(false);
            var roleId = await ResolveRoleByNameAsync(c, t, command.Role, ct).ConfigureAwait(false);
            return await SavePersonCoreAsync(c, t, command.Name.Trim(), command.Document.Trim(), roleId, propertyId, null, null, null, command.IdempotencyKey, ct).ConfigureAwait(false);
        }

        if (targetKind == "TIME_ENTRY")
        {
            if (command.PersonId is null || startsAt is null || string.IsNullOrWhiteSpace(command.ActivityType))
                throw new DomainException("A jornada exige pessoa, atividade e início. O vínculo não é inferido.", "rural_hr.journey_incomplete");
            var propertyId = await ResolvePropertyAsync(c, t, command.PropertyId, ct).ConfigureAwait(false);
            return await SaveJourneyCoreAsync(c, t, new TimeEntryCommand(command.PersonId.Value, command.TeamId, propertyId, command.ResourceId, startsAt.Value, endsAt, command.BreakMinutes, command.ActivityType.Trim(), command.Notes, null, null, command.IdempotencyKey, command.PieceQuantity), ct).ConfigureAwait(false);
        }

        if (targetKind == "ALLOCATION")
            throw new DomainException("Use o comando de alocação agrícola. O cadastro genérico não define propriedade, período e vínculo.", "rural_hr.allocation_command_required");

        var key = id ?? Guid.CreateVersion7();
        var status = RuralHrRules.InitialStatus(targetKind);
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var hash = Hash($"generic|{targetKind}|{command.Name}|{command.PersonId}|{command.TeamId}|{command.PropertyId}|{startsAt:O}|{endsAt:O}|{command.Amount}");
            var replay = await ReplayAsync(c, t, "generic", command.IdempotencyKey, hash, ct).ConfigureAwait(false);
            if (replay.HasValue) return replay.Value;
            await RememberAsync(c, t, "generic", command.IdempotencyKey, key, hash, ct).ConfigureAwait(false);
        }

        await c.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.rural_hr_records(
                id, tenant_id, kind, name, person_id, team_id, property_id, resource_id,
                starts_at, started_at, ends_at, ended_at, amount, notes, status,
                role, activity_type, break_minutes, order_id, season_id, plot_id, created_by, updated_by)
            values(
                @Id, @TenantId, @Kind, @Name, @PersonId, @TeamId, @PropertyId, @ResourceId,
                @StartsAt, @StartsAt, @EndsAt, @EndsAt, @Amount, @Notes, @Status,
                @Role, @ActivityType, @BreakMinutes, @OrderId, @SeasonId, @PlotId, @UserId, @UserId)
            """,
            new
            {
                Id = key, tenant.TenantId, Kind = targetKind, command.Name, command.PersonId, command.TeamId,
                command.PropertyId, command.ResourceId, StartsAt = startsAt, EndsAt = endsAt, command.Amount,
                command.Notes, Status = status, command.Role, command.ActivityType, command.BreakMinutes,
                command.OrderId, command.SeasonId, command.PlotId, tenant.UserId
            }, t, cancellationToken: ct)).ConfigureAwait(false);
        return key;
    }, ct);

    public Task<Guid> AddPersonAsync(PersonCommand command, CancellationToken ct) => db.InTenantTransactionAsync(
        (c, t) => SavePersonCoreAsync(c, t, command.Name.Trim(), command.Document.Trim(), command.RoleId, command.PropertyId, command.Email, command.Phone, command.Skills, null, ct), ct);

    public Task<Guid> RegisterTimeAsync(TimeEntryCommand command, CancellationToken ct) => db.InTenantTransactionAsync(
        (c, t) => SaveJourneyCoreAsync(c, t, command, ct), ct);

    public Task<Guid> SaveAllocationAsync(Guid? id, RuralHrAllocationCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        if (command.PersonId is null && command.TeamId is null)
            throw new DomainException("Informe a pessoa ou a equipe.", "rural_hr.allocation_subject_required");
        if (command.EndsAt <= command.StartsAt)
            throw new DomainException("O período da alocação é inválido.", "rural_hr.period_invalid");
        if (string.IsNullOrWhiteSpace(command.Reason) && id is not null)
            throw new DomainException("Informe o motivo da alteração.", "rural_hr.reason_required");
        var hash = Hash($"allocation|{id}|{command.PersonId}|{command.TeamId}|{command.PropertyId}|{command.PlotId}|{command.SeasonId}|{command.OrderId}|{command.ActivityType}|{command.StartsAt:O}|{command.EndsAt:O}|{command.Name}");
        var replay = await ReplayAsync(c, t, "allocation", command.IdempotencyKey, hash, ct).ConfigureAwait(false);
        if (replay.HasValue) return replay.Value;

        var graph = await LoadAllocationGraphAsync(c, t, command.PropertyId, command.PlotId, command.SeasonId, command.OrderId, command.PersonId, command.TeamId, ct).ConfigureAwait(false);
        await EnsureNoOverlapAsync(c, t, id, command.PersonId, command.TeamId, command.StartsAt, command.EndsAt, ct).ConfigureAwait(false);
        var key = id ?? Guid.CreateVersion7();
        if (id is null)
        {
            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.rural_hr_records(
                    id, tenant_id, kind, name, person_id, team_id, property_id, starts_at, ends_at, started_at, ended_at,
                    activity_type, order_id, season_id, plot_id, notes, status, created_by, updated_by)
                values(
                    @Id, @TenantId, 'ALLOCATION', @Name, @PersonId, @TeamId, @PropertyId, @StartsAt, @EndsAt, @StartsAt, @EndsAt,
                    @Activity, @OrderId, @SeasonId, @PlotId, @Notes, 'ACTIVE', @UserId, @UserId)
                """,
                new
                {
                    Id = key, tenant.TenantId, command.Name, command.PersonId, command.TeamId, command.PropertyId,
                    command.StartsAt, command.EndsAt, Activity = command.ActivityType.Trim(), OrderId = graph.OrderId,
                    SeasonId = graph.SeasonId, PlotId = graph.PlotId, command.Notes, tenant.UserId
                }, t, cancellationToken: ct)).ConfigureAwait(false);
        }
        else
        {
            var current = await c.QuerySingleOrDefaultAsync<AllocationRow>(new CommandDefinition(
                "select id, person_id PersonId, team_id TeamId, status Status from agro360.rural_hr_records where tenant_id=@TenantId and id=@Id and kind='ALLOCATION' for update",
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct)).ConfigureAwait(false)
                ?? throw new KeyNotFoundException("Alocação não encontrada.");
            if (!string.Equals(current.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                throw new ConflictException("Alocação encerrada não pode ser reescrita.");
            await EnsureExecutionPreservedAsync(c, t, id.Value, command.PersonId, command.StartsAt, command.EndsAt, ct).ConfigureAwait(false);
            var updated = await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.rural_hr_records
                   set name=@Name, person_id=@PersonId, team_id=@TeamId, property_id=@PropertyId,
                       starts_at=@StartsAt, ends_at=@EndsAt, started_at=@StartsAt, ended_at=@EndsAt,
                       activity_type=@Activity, order_id=@OrderId, season_id=@SeasonId, plot_id=@PlotId,
                       notes=@Notes, updated_by=@UserId, updated_at=now()
                 where tenant_id=@TenantId and id=@Id and kind='ALLOCATION' and status='ACTIVE'
                """,
                new
                {
                    tenant.TenantId, Id = id, command.Name, command.PersonId, command.TeamId, command.PropertyId,
                    command.StartsAt, command.EndsAt, Activity = command.ActivityType.Trim(), OrderId = graph.OrderId,
                    SeasonId = graph.SeasonId, PlotId = graph.PlotId, command.Notes, tenant.UserId
                }, t, cancellationToken: ct)).ConfigureAwait(false);
            if (updated == 0) throw new ConflictException("A alocação mudou durante a edição.");
        }

        await WriteEventAsync(c, t, key, id is null ? "CREATED" : "UPDATED", command.Reason, command, ct).ConfigureAwait(false);
        await SyncFieldResourceAsync(c, t, key, graph.OrderId, command.PersonId, command.StartsAt, command.EndsAt, ct).ConfigureAwait(false);
        await RememberAsync(c, t, "allocation", command.IdempotencyKey, key, hash, ct).ConfigureAwait(false);
        return key;
    }, ct);

    public Task CancelAllocationAsync(Guid id, string reason, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < 5)
            throw new DomainException("Informe o motivo do cancelamento.", "rural_hr.reason_required");
        var current = await c.QuerySingleOrDefaultAsync<string>(new CommandDefinition(
            "select status from agro360.rural_hr_records where tenant_id=@TenantId and id=@Id and kind='ALLOCATION' for update",
            new { tenant.TenantId, Id = id }, t, cancellationToken: ct)).ConfigureAwait(false)
            ?? throw new KeyNotFoundException("Alocação não encontrada.");
        if (string.Equals(current, "CANCELLED", StringComparison.OrdinalIgnoreCase)) return;
        RuralHrRules.ValidateStatusTransition("ALLOCATION", current, "CANCELLED");
        var updated = await c.ExecuteAsync(new CommandDefinition(
            "update agro360.rural_hr_records set status='CANCELLED', updated_by=@UserId, updated_at=now() where tenant_id=@TenantId and id=@Id and kind='ALLOCATION' and status=@Current",
            new { tenant.TenantId, Id = id, tenant.UserId, Current = current }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (updated == 0) throw new ConflictException("A alocação mudou durante o cancelamento.");
        await c.ExecuteAsync(new CommandDefinition(
            """
            update agro360.field_work_order_resources
               set status='CANCELLED', updated_by=@UserId, updated_at=now()
             where tenant_id=@TenantId and hr_allocation_id=@Id and status='RESERVED' and deleted_at is null
            """,
            new { tenant.TenantId, Id = id, tenant.UserId }, t, cancellationToken: ct)).ConfigureAwait(false);
        await WriteEventAsync(c, t, id, "CANCELLED", reason.Trim(), new { id }, ct).ConfigureAwait(false);
    }, ct);

    public Task ConfirmTimeAsync(Guid id, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var entry = await LockEntryAsync(c, t, id, ct).ConfigureAwait(false);
        if (string.Equals(entry.ReviewStatus, "CONFIRMED", StringComparison.OrdinalIgnoreCase)) return;
        if (!string.Equals(entry.Status, "CLOSED", StringComparison.OrdinalIgnoreCase) || entry.EndedAt is null)
            throw new DomainException("A conferência exige jornada encerrada com saída válida.", "rural_hr.review_requires_close");
        await SnapshotLaborAsync(c, t, id, ct).ConfigureAwait(false);
        var updated = await c.ExecuteAsync(new CommandDefinition(
            """
            update agro360.rural_hr_time_entries
               set review_status='CONFIRMED', reviewed_by=@UserId, reviewed_at=now(), version=version+1, updated_at=now()
             where tenant_id=@TenantId and id=@Id and status='CLOSED' and review_status='PENDING' and ended_at is not null
            """,
            new { tenant.TenantId, Id = id, tenant.UserId }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (updated == 0) throw new ConflictException("A jornada mudou antes da conferência.");
    }, ct);

    public Task CorrectTimeAsync(Guid id, RuralHrTimeCorrectionCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var entry = await LockEntryAsync(c, t, id, ct).ConfigureAwait(false);
        if (string.Equals(entry.Status, "CANCELLED", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Jornada cancelada não é corrigida por este comando.");
        if (entry.AllocationBatchId is not null && await BatchIsConfirmedAsync(c, t, entry.AllocationBatchId.Value, ct).ConfigureAwait(false))
            throw new ConflictException("Estorne a apropriação antes de corrigir a jornada conferida.");
        if (entry.SeasonId is not null)
            await EnsureSeasonOpenAsync(c, t, entry.SeasonId.Value, DateOnly.FromDateTime(entry.StartedAt.UtcDateTime), ct).ConfigureAwait(false);

        var ended = command.EndedAt ?? entry.EndedAt;
        if (string.Equals(entry.Status, "CLOSED", StringComparison.OrdinalIgnoreCase) && ended is null)
            throw new DomainException("A jornada encerrada precisa continuar com saída válida.", "rural_hr.exit_required");
        decimal? hours = null;
        if (ended is not null)
            hours = RuralHrRules.WorkedHours(entry.StartedAt, ended.Value, command.BreakMinutes);

        await c.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.rural_hr_time_corrections(
                id, tenant_id, time_entry_id, previous_started_at, previous_ended_at, previous_break_minutes,
                previous_hours, previous_cost_amount, new_ended_at, new_break_minutes, new_hours, justification, created_by)
            values(
                @Id, @TenantId, @EntryId, @Started, @PreviousEnded, @PreviousBreak, @PreviousHours, @PreviousCost,
                @NewEnded, @NewBreak, @NewHours, @Justification, @UserId)
            """,
            new
            {
                Id = Guid.CreateVersion7(), tenant.TenantId, EntryId = id, Started = entry.StartedAt,
                PreviousEnded = entry.EndedAt, PreviousBreak = entry.BreakMinutes, PreviousHours = entry.HoursWorked,
                PreviousCost = entry.CostAmount, NewEnded = ended, NewBreak = command.BreakMinutes, NewHours = hours,
                Justification = command.Justification.Trim(), tenant.UserId
            }, t, cancellationToken: ct)).ConfigureAwait(false);

        var status = ended is null ? "OPEN" : "CLOSED";
        var updated = await c.ExecuteAsync(new CommandDefinition(
            """
            update agro360.rural_hr_time_entries
               set ended_at=@Ended, break_minutes=@Break, piece_quantity=@Piece, status=@Status,
                   hours_worked=@Hours, review_status='PENDING', reviewed_by=null, reviewed_at=null,
                   version=version+1, updated_at=now()
             where tenant_id=@TenantId and id=@Id and version=@Version
            """,
            new { tenant.TenantId, Id = id, Ended = ended, Break = command.BreakMinutes, Piece = command.PieceQuantity, Status = status, Hours = hours, Version = entry.Version }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (updated == 0) throw new ConflictException("A jornada foi alterada por outro operador.");
        await SyncProjectionAsync(c, t, id, "TIME_ENTRY", status, null, ct).ConfigureAwait(false);
        await SnapshotLaborAsync(c, t, id, ct).ConfigureAwait(false);
        if (entry.CostEntryId is not null)
            await AlignRecognizedCostAsync(c, t, id, entry.CostEntryId.Value, ct).ConfigureAwait(false);
    }, ct);

    public Task<Guid> SaveTariffAsync(RuralHrTariffCommand command, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var type = command.RateType.Trim().ToUpperInvariant();
        var unit = RuralHrRules.TariffUnit(type);
        if (command.ValidTo is not null && command.ValidTo < command.ValidFrom)
            throw new DomainException("A vigência final é anterior ao início.", "rural_hr.tariff_period_invalid");
        var activity = string.IsNullOrWhiteSpace(command.ActivityType) ? null : command.ActivityType.Trim();
        var hash = Hash($"tariff|{command.RoleId}|{activity}|{type}|{command.RateValue}|{command.ValidFrom:O}|{command.ValidTo:O}");
        var replay = await ReplayAsync(c, t, "tariff", command.IdempotencyKey, hash, ct).ConfigureAwait(false);
        if (replay.HasValue) return replay.Value;
        if (command.RoleId is not null && !await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from agro360.rural_hr_roles where tenant_id=@TenantId and id=@Id and active)",
            new { tenant.TenantId, Id = command.RoleId }, t, cancellationToken: ct)).ConfigureAwait(false))
            throw new DomainException("O cargo não pertence a este tenant.", "rural_hr.role_invalid");
        var overlap = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists(
                select 1 from agro360.rural_hr_tariffs t
                where t.tenant_id=@TenantId and t.active
                  and t.role_id is not distinct from @RoleId
                  and coalesce(lower(t.activity_type), '') = coalesce(lower(@Activity), '')
                  and t.valid_from <= coalesce(@ValidTo, date '9999-12-31')
                  and coalesce(t.valid_to, date '9999-12-31') >= @ValidFrom)
            """,
            new { tenant.TenantId, command.RoleId, Activity = activity, command.ValidFrom, command.ValidTo }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (overlap)
            throw new DomainException("Já existe tarifa vigente com a mesma especificidade. Encerre a vigência anterior ou diferencie papel e atividade.", "rural_hr.tariff_overlap");
        var id = Guid.CreateVersion7();
        await c.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.rural_hr_tariffs(
                id, tenant_id, role_id, activity_type, rate_type, rate_value, valid_from, valid_to, active, created_by, currency, unit_code, rounding_scale)
            values(@Id, @TenantId, @RoleId, @Activity, @Type, @Value, @ValidFrom, @ValidTo, true, @UserId, 'BRL', @Unit, 4)
            """,
            new { Id = id, tenant.TenantId, command.RoleId, Activity = activity, Type = type, Value = command.RateValue, command.ValidFrom, command.ValidTo, tenant.UserId, Unit = unit }, t, cancellationToken: ct)).ConfigureAwait(false);
        await RememberAsync(c, t, "tariff", command.IdempotencyKey, id, hash, ct).ConfigureAwait(false);
        return id;
    }, ct);

    public Task<IReadOnlyList<RuralHrTariff>> ListTariffsAsync(CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var rows = await c.QueryAsync<RuralHrTariff>(new CommandDefinition(
            """
            select t.id, t.role_id RoleId, r.name RoleName, t.activity_type ActivityType, t.rate_type RateType,
                   t.rate_value RateValue, t.unit_code UnitCode, t.valid_from ValidFrom, t.valid_to ValidTo, t.active
            from agro360.rural_hr_tariffs t
            left join agro360.rural_hr_roles r on r.tenant_id=t.tenant_id and r.id=t.role_id
            where t.tenant_id=@TenantId
            order by t.valid_from desc, t.created_at desc
            """,
            new { tenant.TenantId }, t, cancellationToken: ct)).ConfigureAwait(false);
        return (IReadOnlyList<RuralHrTariff>)rows.ToArray();
    }, ct);

    public Task<RuralHrAppropriationResult> AppropriateLaborAsync(Guid timeEntryId, string idempotencyKey, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new DomainException("Informe a chave de idempotência.", "rural_hr.idempotency_required");
        var entry = await LockEntryAsync(c, t, timeEntryId, ct).ConfigureAwait(false);
        if (!string.Equals(entry.ReviewStatus, "CONFIRMED", StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Apropriação exige jornada conferida.", "rural_hr.appropriation_requires_review");
        if (entry.CostStatus == "UNAVAILABLE" || entry.CostAmount is null)
            return new RuralHrAppropriationResult(null, null, "UNAVAILABLE", "Custo indisponível. Não há tarifa aplicável ou falta a quantidade de produção.");
        if (entry.SeasonId is not null)
            await EnsureSeasonOpenAsync(c, t, entry.SeasonId.Value, DateOnly.FromDateTime(entry.StartedAt.UtcDateTime), ct).ConfigureAwait(false);

        var sourceKey = timeEntryId.ToString("D");
        var costId = entry.CostEntryId ?? await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "select id from agro360.cost_management_entries where tenant_id=@TenantId and source_type='RURAL_HR_TIME' and source_key=@Key and deleted_at is null",
            new { tenant.TenantId, Key = sourceKey }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (costId is null)
        {
            costId = Guid.CreateVersion7();
            var personName = await c.ExecuteScalarAsync<string>(new CommandDefinition(
                "select name from agro360.rural_hr_people where tenant_id=@TenantId and id=@Id",
                new { tenant.TenantId, Id = entry.PersonId }, t, cancellationToken: ct)).ConfigureAwait(false) ?? "Pessoa";
            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.cost_management_entries(
                    id, tenant_id, farm_id, category, competence_date, recognized_amount, allocated_amount, currency,
                    source_type, source_key, source_document, description, status, created_by)
                values(
                    @Id, @TenantId, @FarmId, 'LABOR', @Competence, @Amount, 0, 'BRL',
                    'RURAL_HR_TIME', @Key, @Key, @Description, 'OPEN', @UserId)
                """,
                new
                {
                    Id = costId, tenant.TenantId, FarmId = entry.PropertyId, Competence = DateOnly.FromDateTime(entry.StartedAt.UtcDateTime),
                    Amount = entry.CostAmount.Value, Key = sourceKey, tenant.UserId,
                    Description = $"Mão de obra {personName} jornada {sourceKey}"[..Math.Min(240, $"Mão de obra {personName} jornada {sourceKey}".Length)]
                }, t, cancellationToken: ct)).ConfigureAwait(false);
            await c.ExecuteAsync(new CommandDefinition(
                "update agro360.rural_hr_time_entries set cost_entry_id=@CostId, updated_at=now() where tenant_id=@TenantId and id=@Id",
                new { tenant.TenantId, Id = timeEntryId, CostId = costId }, t, cancellationToken: ct)).ConfigureAwait(false);
        }

        if (entry.CostAmount.Value == 0)
            return new RuralHrAppropriationResult(costId, null, "ZERO_COST", "Custo conferido igual a zero. Não há saldo a apropriar.");
        if (entry.SeasonId is null)
            return new RuralHrAppropriationResult(costId, null, "RECOGNIZED_UNALLOCATED", "Custo reconhecido na propriedade. A alocação não tem safra de destino.");

        var existingBatch = await c.QuerySingleOrDefaultAsync<BatchLink>(new CommandDefinition(
            "select id Id, status Status from agro360.cost_allocation_batches where tenant_id=@TenantId and idempotency_key=@Key",
            new { tenant.TenantId, Key = idempotencyKey }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (existingBatch is not null && existingBatch.Status == "CONFIRMED")
            return new RuralHrAppropriationResult(costId, existingBatch.Id, "ALREADY_APPROPRIATED", "Reenvio devolveu a apropriação já confirmada.");

        var available = await c.ExecuteScalarAsync<decimal>(new CommandDefinition(
            "select recognized_amount-allocated_amount from agro360.cost_management_entries where tenant_id=@TenantId and id=@Id and deleted_at is null for update",
            new { tenant.TenantId, Id = costId }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (available <= 0)
            return new RuralHrAppropriationResult(costId, entry.AllocationBatchId, "ALREADY_APPROPRIATED", "O saldo apropriável desta jornada já foi utilizado.");
        var amount = Math.Min(available, entry.CostAmount.Value);
        var batch = Guid.CreateVersion7();
        await c.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.cost_allocation_batches(
                id, tenant_id, entry_id, method, amount, currency, base_snapshot, idempotency_key, status, justification, confirmed_at, confirmed_by, created_by)
            values(@Id, @TenantId, @EntryId, 'DIRECT', @Amount, 'BRL', cast(@Snapshot as jsonb), @Key, 'CONFIRMED', @Justification, now(), @UserId, @UserId)
            """,
            new
            {
                Id = batch, tenant.TenantId, EntryId = costId, Amount = amount, tenant.UserId, Key = idempotencyKey,
                Snapshot = JsonSerializer.Serialize(new { timeEntryId, tariffId = entry.TariffId, rateType = entry.RateType, rateValue = entry.RateValue, hours = entry.HoursWorked, quantity = entry.PieceQuantity, rule = RoundingRule }),
                Justification = "Apropriação direta da jornada conferida para a safra da alocação."
            }, t, cancellationToken: ct)).ConfigureAwait(false);
        await c.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.cost_allocations(
                id, tenant_id, batch_id, entry_id, season_id, farm_id, field_id, base_value, base_unit, percentage, amount,
                rounding_adjustment, status, confirmed_at, confirmed_by, created_by)
            values(@Id, @TenantId, @Batch, @EntryId, @SeasonId, @FarmId, @FieldId, 1, 'JOURNEY', 100, @Amount, 0, 'CONFIRMED', now(), @UserId, @UserId)
            """,
            new { Id = Guid.CreateVersion7(), tenant.TenantId, Batch = batch, EntryId = costId, entry.SeasonId, FarmId = entry.PropertyId, FieldId = entry.PlotId, Amount = amount, tenant.UserId }, t, cancellationToken: ct)).ConfigureAwait(false);
        var moved = await c.ExecuteAsync(new CommandDefinition(
            """
            update agro360.cost_management_entries
               set allocated_amount=allocated_amount+@Amount, row_version=row_version+1, updated_at=now(), updated_by=@UserId
             where tenant_id=@TenantId and id=@Id and status='OPEN' and allocated_amount+@Amount<=recognized_amount
            """,
            new { tenant.TenantId, Id = costId, Amount = amount, tenant.UserId }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (moved == 0) throw new ConflictException("O saldo apropriável mudou durante a confirmação.");
        await c.ExecuteAsync(new CommandDefinition(
            "update agro360.rural_hr_time_entries set allocation_batch_id=@Batch, updated_at=now() where tenant_id=@TenantId and id=@Id",
            new { tenant.TenantId, Id = timeEntryId, Batch = batch }, t, cancellationToken: ct)).ConfigureAwait(false);
        return new RuralHrAppropriationResult(costId, batch, "APPROPRIATED", "Custo apropriado à safra da alocação.");
    }, ct);

    public Task<RuralHrOperationsBoard> OperationsBoardAsync(RuralHrBoardQuery query, CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var page = Math.Clamp(query.Page, 1, 10_000);
        var size = Math.Clamp(query.PageSize, 1, 100);
        var filter = new { tenant.TenantId, query.From, query.To, query.PropertyId, query.SeasonId, query.TeamId };
        var allocated = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            select count(distinct person_id) from agro360.rural_hr_records a
            where a.tenant_id=@TenantId and a.kind='ALLOCATION' and a.status='ACTIVE' and a.person_id is not null
              and (@PropertyId is null or a.property_id=@PropertyId) and (@SeasonId is null or a.season_id=@SeasonId) and (@TeamId is null or a.team_id=@TeamId)
              and (@From is null or a.ends_at::date >= @From) and (@To is null or a.starts_at::date <= @To)
            """, filter, t, cancellationToken: ct)).ConfigureAwait(false);
        var open = await c.ExecuteScalarAsync<int>(new CommandDefinition(CountJourneys("t.status='OPEN'"), filter, t, cancellationToken: ct)).ConfigureAwait(false);
        var waiting = await c.ExecuteScalarAsync<int>(new CommandDefinition(CountJourneys("t.status='CLOSED' and t.review_status='PENDING'"), filter, t, cancellationToken: ct)).ConfigureAwait(false);
        var conflicts = await c.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            select count(*) from agro360.rural_hr_records a
            where a.tenant_id=@TenantId and a.kind='ALLOCATION' and a.status='ACTIVE'
              and (@PropertyId is null or a.property_id=@PropertyId) and (@SeasonId is null or a.season_id=@SeasonId) and (@TeamId is null or a.team_id=@TeamId)
              and exists(
                select 1 from agro360.rural_hr_records b
                where b.tenant_id=a.tenant_id and b.kind='ALLOCATION' and b.status='ACTIVE' and b.id<>a.id
                  and b.starts_at < a.ends_at and b.ends_at > a.starts_at
                  and ((a.person_id is not null and b.person_id=a.person_id) or (a.team_id is not null and b.team_id=a.team_id)))
            """, filter, t, cancellationToken: ct)).ConfigureAwait(false);
        var realized = await c.ExecuteScalarAsync<decimal?>(new CommandDefinition(SumJourneys("t.review_status='CONFIRMED' and t.cost_status='CALCULATED'"), filter, t, cancellationToken: ct)).ConfigureAwait(false);
        var pendingTariff = await c.ExecuteScalarAsync<int>(new CommandDefinition(CountJourneys("t.status='CLOSED' and t.cost_status='UNAVAILABLE'"), filter, t, cancellationToken: ct)).ConfigureAwait(false);
        var appropriated = await c.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            """
            select sum(ca.amount) from agro360.cost_allocations ca
            join agro360.cost_management_entries e on e.tenant_id=ca.tenant_id and e.id=ca.entry_id
            where ca.tenant_id=@TenantId and ca.status='CONFIRMED' and e.source_type='RURAL_HR_TIME' and e.deleted_at is null
              and (@PropertyId is null or e.farm_id=@PropertyId) and (@SeasonId is null or ca.season_id=@SeasonId)
              and (@From is null or e.competence_date >= @From) and (@To is null or e.competence_date <= @To)
            """, filter, t, cancellationToken: ct)).ConfigureAwait(false);
        var available = await c.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            """
            select sum(e.recognized_amount-e.allocated_amount) from agro360.cost_management_entries e
            where e.tenant_id=@TenantId and e.source_type='RURAL_HR_TIME' and e.status='OPEN' and e.deleted_at is null
              and (@PropertyId is null or e.farm_id=@PropertyId)
              and (@From is null or e.competence_date >= @From) and (@To is null or e.competence_date <= @To)
            """, filter, t, cancellationToken: ct)).ConfigureAwait(false);
        var situation = (query.Situation ?? "OPEN_JOURNEY").Trim().ToUpperInvariant();
        var (sql, args) = BoardItems(situation, filter, page, size);
        var rows = (await c.QueryAsync<BoardItem>(new CommandDefinition(sql, args, t, cancellationToken: ct)).ConfigureAwait(false)).ToArray();
        var total = await c.ExecuteScalarAsync<int>(new CommandDefinition(BoardCount(situation), filter, t, cancellationToken: ct)).ConfigureAwait(false);
        var metrics = new RuralHrBoardMetric[]
        {
            new("ALLOCATED", "Pessoas alocadas", allocated, null, "NONE"),
            new("OPEN_JOURNEY", "Jornadas abertas", open, null, "NONE"),
            new("AWAITING_REVIEW", "Aguardando conferência", waiting, null, "NONE"),
            new("CONFLICT", "Conflitos de alocação", conflicts, null, "NONE"),
            new("COST_REALIZED", "Custo conferido", null, realized, realized is null ? "NONE" : "VALUE"),
            new("COST_PENDING_TARIFF", "Custo sem tarifa", pendingTariff, null, pendingTariff > 0 ? "UNAVAILABLE" : "NONE"),
            new("APPROPRIATED", "Apropriado na safra", null, appropriated, appropriated is null ? "NONE" : "VALUE"),
            new("AVAILABLE_BALANCE", "Saldo a apropriar", null, available, available is null ? "NONE" : "VALUE")
        };
        return new RuralHrOperationsBoard(metrics, rows.Select(x => x.ToRecord()).ToArray(), page, size, total);
    }, ct);

    public Task<IReadOnlyList<RuralHrDataReview>> ListDataReviewsAsync(CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var rows = await c.QueryAsync<RuralHrDataReview>(new CommandDefinition(
            """
            select id, entity_table EntityTable, entity_id EntityId, reason_code ReasonCode, detail, resolution, detected_at DetectedAt
            from agro360.rural_hr_data_reviews
            where tenant_id=@TenantId and resolution='OPEN'
            order by detected_at desc
            limit 200
            """,
            new { tenant.TenantId }, t, cancellationToken: ct)).ConfigureAwait(false);
        return (IReadOnlyList<RuralHrDataReview>)rows.ToArray();
    }, ct);

    public Task<IReadOnlyList<RuralHrProjectionIssue>> ProjectionIssuesAsync(CancellationToken ct) => db.InTenantTransactionAsync(async (c, t) =>
    {
        var rows = await c.QueryAsync<RuralHrProjectionIssue>(new CommandDefinition(
            "select kind Kind, record_id RecordId, issue Issue from agro360.rural_hr_projection_issues() where tenant_id=@TenantId",
            new { tenant.TenantId }, t, cancellationToken: ct)).ConfigureAwait(false);
        return (IReadOnlyList<RuralHrProjectionIssue>)rows.ToArray();
    }, ct);

    private async Task EnsureSavedAllocationAsync(DbConnection c, DbTransaction t, Guid? id, RuralHrCommand command, CancellationToken ct)
    {
        if (!string.Equals(command.Kind, "ALLOCATION", StringComparison.OrdinalIgnoreCase)) return;
        if (command.PropertyId is null || command.StartsAt is null || command.EndsAt is null || (command.PersonId is null && command.TeamId is null))
            throw new DomainException("A alocação exige propriedade, pessoa ou equipe e período.", "rural_hr.allocation_incomplete");
        await LoadAllocationGraphAsync(c, t, command.PropertyId.Value, null, null, null, command.PersonId, command.TeamId, ct).ConfigureAwait(false);
        await EnsureNoOverlapAsync(c, t, id, command.PersonId, command.TeamId, command.StartsAt.Value, command.EndsAt.Value, ct).ConfigureAwait(false);
        if (id is not null)
            await EnsureExecutionPreservedAsync(c, t, id.Value, command.PersonId, command.StartsAt.Value, command.EndsAt.Value, ct).ConfigureAwait(false);
    }

    private async Task<Guid> SavePersonCoreAsync(DbConnection c, DbTransaction t, string name, string document, Guid roleId, Guid propertyId, string? email, string? phone, string? skills, string? idempotencyKey, CancellationToken ct)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(document, "^[0-9]{11,14}$"))
            throw new DomainException("Informe o documento real com 11 a 14 dígitos.", "rural_hr.document_invalid");
        await ResolvePropertyAsync(c, t, propertyId, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            var replay = await ReplayAsync(c, t, "person", idempotencyKey, Hash($"person|{document}|{name}|{roleId}|{propertyId}"), ct).ConfigureAwait(false);
            if (replay.HasValue) return replay.Value;
        }
        var existing = await c.QuerySingleOrDefaultAsync<PersonMatch>(new CommandDefinition(
            "select id Id, name Name, role_id RoleId, property_id PropertyId from agro360.rural_hr_people where tenant_id=@TenantId and document=@Document",
            new { tenant.TenantId, Document = document }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (existing is not null)
        {
            if (string.Equals(existing.Name, name, StringComparison.Ordinal) && existing.RoleId == roleId && existing.PropertyId == propertyId)
                return existing.Id;
            throw new DomainException("Documento já cadastrado neste tenant com dados diferentes.", "rural_hr.document_duplicate");
        }
        if (!await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from agro360.rural_hr_roles where tenant_id=@TenantId and id=@Id)",
            new { tenant.TenantId, Id = roleId }, t, cancellationToken: ct)).ConfigureAwait(false))
            throw new DomainException("O cargo não pertence a este tenant.", "rural_hr.role_invalid");
        var id = Guid.CreateVersion7();
        await c.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.rural_hr_people(id, tenant_id, name, document, role_id, property_id, email, phone, skills, status, created_by, updated_by)
            values(@Id, @TenantId, @Name, @Document, @RoleId, @PropertyId, @Email, @Phone, @Skills::jsonb, 'ACTIVE', @UserId, @UserId)
            """,
            new { Id = id, tenant.TenantId, Name = name, Document = document, RoleId = roleId, PropertyId = propertyId, Email = email, Phone = phone, Skills = JsonSerializer.Serialize((skills ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)), tenant.UserId }, t, cancellationToken: ct)).ConfigureAwait(false);
        await UpsertPersonProjectionAsync(c, t, id, name, propertyId, "ACTIVE", ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
            await RememberAsync(c, t, "person", idempotencyKey, id, Hash($"person|{document}|{name}|{roleId}|{propertyId}"), ct).ConfigureAwait(false);
        return id;
    }

    private async Task<Guid> SaveJourneyCoreAsync(DbConnection c, DbTransaction t, TimeEntryCommand command, CancellationToken ct)
    {
        if (command.EndedAt is not null)
            RuralHrRules.WorkedHours(command.StartedAt, command.EndedAt.Value, command.BreakMinutes);
        if (command.PieceQuantity is < 0)
            throw new DomainException("A quantidade de produção não pode ser negativa.", "rural_hr.quantity_invalid");
        await ResolvePropertyAsync(c, t, command.PropertyId, ct).ConfigureAwait(false);
        var personOk = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from agro360.rural_hr_people where tenant_id=@TenantId and id=@Id and status='ACTIVE')",
            new { tenant.TenantId, Id = command.PersonId }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (!personOk) throw new DomainException("A pessoa não está ativa neste tenant.", "rural_hr.person_unavailable");
        Guid? orderId = null, seasonId = null, plotId = null, teamId = command.TeamId;
        if (command.AllocationId is not null)
        {
            var allocation = await c.QuerySingleOrDefaultAsync<AllocationLink>(new CommandDefinition(
                """
                select id Id, person_id PersonId, team_id TeamId, property_id PropertyId, plot_id PlotId, season_id SeasonId, order_id OrderId, starts_at StartsAt, ends_at EndsAt, status Status
                from agro360.rural_hr_records
                where tenant_id=@TenantId and id=@Id and kind='ALLOCATION'
                """,
                new { tenant.TenantId, Id = command.AllocationId }, t, cancellationToken: ct)).ConfigureAwait(false)
                ?? throw new DomainException("A alocação informada não existe.", "rural_hr.allocation_invalid");
            if (!string.Equals(allocation.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
                throw new DomainException("A alocação não está ativa.", "rural_hr.allocation_inactive");
            if (allocation.PropertyId != command.PropertyId || (allocation.PersonId is not null && allocation.PersonId != command.PersonId) || (allocation.TeamId is not null && command.TeamId is not null && allocation.TeamId != command.TeamId))
                throw new DomainException("A jornada não corresponde à pessoa, equipe ou propriedade da alocação.", "rural_hr.allocation_mismatch");
            if (allocation.EndsAt is null || command.StartedAt < allocation.StartsAt || command.StartedAt >= allocation.EndsAt)
                throw new DomainException("O início da jornada está fora do período da alocação.", "rural_hr.allocation_period");
            orderId = allocation.OrderId; seasonId = allocation.SeasonId; plotId = allocation.PlotId; teamId ??= allocation.TeamId;
        }
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
        {
            var replay = await ReplayAsync(c, t, "time", command.IdempotencyKey, Hash(JourneyHash(command)), ct).ConfigureAwait(false);
            if (replay.HasValue) return replay.Value;
        }
        if (!string.IsNullOrWhiteSpace(command.OfflineId))
        {
            var offline = await c.QuerySingleOrDefaultAsync<(Guid Id, Guid PersonId, DateTimeOffset StartedAt)?>(new CommandDefinition(
                "select id Id, person_id PersonId, started_at StartedAt from agro360.rural_hr_time_entries where tenant_id=@TenantId and offline_id=@OfflineId",
                new { tenant.TenantId, command.OfflineId }, t, cancellationToken: ct)).ConfigureAwait(false);
            if (offline is not null)
            {
                if (offline.Value.PersonId == command.PersonId && offline.Value.StartedAt == command.StartedAt) return offline.Value.Id;
                throw new ConflictException("A identificação offline já foi usada em outra jornada.");
            }
        }
        var open = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from agro360.rural_hr_time_entries where tenant_id=@TenantId and person_id=@PersonId and ended_at is null and status='OPEN')",
            new { tenant.TenantId, command.PersonId }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (open && command.EndedAt is null) throw new DomainException("A pessoa já possui jornada aberta.", "rural_hr.journey_open");
        var id = Guid.CreateVersion7();
        var status = command.EndedAt is null ? "OPEN" : "CLOSED";
        decimal? hours = command.EndedAt is null ? null : RuralHrRules.WorkedHours(command.StartedAt, command.EndedAt.Value, command.BreakMinutes);
        await c.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.rural_hr_time_entries(
                id, tenant_id, person_id, team_id, property_id, resource_id, started_at, ended_at, break_minutes,
                activity_type, notes, offline_id, status, created_by, allocation_id, order_id, season_id, plot_id,
                hours_worked, piece_quantity, idempotency_key)
            values(
                @Id, @TenantId, @PersonId, @TeamId, @PropertyId, @ResourceId, @StartedAt, @EndedAt, @BreakMinutes,
                @ActivityType, @Notes, @OfflineId, @Status, @UserId, @AllocationId, @OrderId, @SeasonId, @PlotId,
                @Hours, @Piece, @IdempotencyKey)
            """,
            new
            {
                Id = id, tenant.TenantId, command.PersonId, TeamId = teamId, command.PropertyId, command.ResourceId,
                command.StartedAt, command.EndedAt, command.BreakMinutes, command.ActivityType, command.Notes, command.OfflineId,
                Status = status, tenant.UserId, command.AllocationId, OrderId = orderId, SeasonId = seasonId, PlotId = plotId,
                Hours = hours, Piece = command.PieceQuantity, command.IdempotencyKey
            }, t, cancellationToken: ct)).ConfigureAwait(false);
        await UpsertJourneyProjectionAsync(c, t, id, ct).ConfigureAwait(false);
        if (status == "CLOSED") await SnapshotLaborAsync(c, t, id, ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            await RememberAsync(c, t, "time", command.IdempotencyKey, id, Hash(JourneyHash(command)), ct).ConfigureAwait(false);
        return id;
    }

    private async Task SnapshotLaborAsync(DbConnection c, DbTransaction t, Guid id, CancellationToken ct)
    {
        var entry = await LockEntryAsync(c, t, id, ct).ConfigureAwait(false);
        if (!string.Equals(entry.Status, "CLOSED", StringComparison.OrdinalIgnoreCase) || entry.EndedAt is null || entry.HoursWorked is null)
        {
            if (entry.EndedAt is not null && entry.HoursWorked is null && string.Equals(entry.Status, "CLOSED", StringComparison.OrdinalIgnoreCase))
            {
                var calculated = RuralHrRules.WorkedHours(entry.StartedAt, entry.EndedAt.Value, entry.BreakMinutes);
                await c.ExecuteAsync(new CommandDefinition(
                    "update agro360.rural_hr_time_entries set hours_worked=@Hours, updated_at=now() where tenant_id=@TenantId and id=@Id",
                    new { tenant.TenantId, Id = id, Hours = calculated }, t, cancellationToken: ct)).ConfigureAwait(false);
                entry = entry with { HoursWorked = calculated };
            }
            else if (!string.Equals(entry.Status, "CLOSED", StringComparison.OrdinalIgnoreCase)) return;
        }
        var hours = entry.HoursWorked ?? RuralHrRules.WorkedHours(entry.StartedAt, entry.EndedAt!.Value, entry.BreakMinutes);
        var roleId = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "select role_id from agro360.rural_hr_people where tenant_id=@TenantId and id=@Id",
            new { tenant.TenantId, Id = entry.PersonId }, t, cancellationToken: ct)).ConfigureAwait(false);
        var day = DateOnly.FromDateTime(entry.StartedAt.UtcDateTime);
        var tariffs = (await c.QueryAsync<RuralHrTariffCandidate>(new CommandDefinition(
            """
            select id Id, role_id RoleId, activity_type ActivityType, rate_type RateType, rate_value RateValue, valid_from ValidFrom
            from agro360.rural_hr_tariffs
            where tenant_id=@TenantId and active and valid_from <= @Day and (valid_to is null or valid_to >= @Day)
            """,
            new { tenant.TenantId, Day = day }, t, cancellationToken: ct)).ConfigureAwait(false)).ToArray();
        string? block = null;
        decimal? amount = null;
        RuralHrTariffCandidate selected = default;
        var found = false;
        try
        {
            found = RuralHrRules.TrySelectTariff(tariffs, roleId, entry.ActivityType, out selected);
        }
        catch (InvalidOperationException ex)
        {
            block = ex.Message;
        }
        if (block is null && !found) block = "Sem tarifa aplicável.";
        if (block is null)
        {
            amount = RuralHrRules.QuoteLabor(selected.RateType, selected.RateValue, hours, entry.PieceQuantity);
            if (amount is null) block = "Quantidade de produção não informada.";
        }
        await c.ExecuteAsync(new CommandDefinition(
            """
            update agro360.rural_hr_time_entries
               set hours_worked=@Hours, tariff_id=@TariffId, rate_type=@RateType, rate_value=@RateValue,
                   cost_amount=@Amount, cost_status=@CostStatus, cost_block=@Block, rounding_rule=@Rule, updated_at=now()
             where tenant_id=@TenantId and id=@Id
            """,
            new
            {
                tenant.TenantId, Id = id, Hours = hours, TariffId = block is null ? selected.Id : (Guid?)null,
                RateType = block is null ? selected.RateType : null, RateValue = block is null ? selected.RateValue : (decimal?)null,
                Amount = amount, CostStatus = block is null ? "CALCULATED" : "UNAVAILABLE", Block = block, Rule = RoundingRule
            }, t, cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task<Guid> ResolvePropertyAsync(DbConnection c, DbTransaction t, Guid? requested, CancellationToken ct)
    {
        var scoped = tenant.FarmId;
        if (requested is null && scoped is null)
            throw new DomainException("Selecione a propriedade. O contexto autorizado não indica uma fazenda.", "rural_hr.property_required");
        if (requested is not null && scoped is not null && requested != scoped)
            throw new DomainException("A propriedade está fora do contexto de fazenda autorizado.", "rural_hr.property_out_of_scope");
        var id = requested ?? scoped!.Value;
        var ok = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from agro360.geo_farms where tenant_id=@TenantId and id=@Id and deleted_at is null)",
            new { tenant.TenantId, Id = id }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (!ok) throw new DomainException("Propriedade não encontrada neste tenant.", "rural_hr.property_invalid");
        return id;
    }

    private async Task<Guid> ResolveRoleByNameAsync(DbConnection c, DbTransaction t, string roleName, CancellationToken ct)
    {
        var name = roleName.Trim();
        if (name.Length < 2) throw new DomainException("Informe o nome do cargo.", "rural_hr.role_required");
        var lockKey = tenant.TenantId.ToString("D") + "|" + name.ToLowerInvariant();
        await c.ExecuteAsync(new CommandDefinition("select pg_advisory_xact_lock(hashtextextended(@LockKey, 0))", new { LockKey = lockKey }, t, cancellationToken: ct)).ConfigureAwait(false);
        var ids = (await c.QueryAsync<Guid>(new CommandDefinition(
            "select id from agro360.rural_hr_roles where tenant_id=@TenantId and lower(name)=lower(@Name) order by id",
            new { tenant.TenantId, Name = name }, t, cancellationToken: ct)).ConfigureAwait(false)).ToArray();
        if (ids.Length > 1)
            throw new ConflictException("Há cargos duplicados com esse nome. Resolva a revisão antes de cadastrar a pessoa.");
        if (ids.Length == 1) return ids[0];
        var created = Guid.CreateVersion7();
        try
        {
            await c.ExecuteAsync(new CommandDefinition(
                "insert into agro360.rural_hr_roles(id, tenant_id, name, active) values(@Id, @TenantId, @Name, true)",
                new { Id = created, tenant.TenantId, Name = name }, t, cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
        {
        }
        ids = (await c.QueryAsync<Guid>(new CommandDefinition(
            "select id from agro360.rural_hr_roles where tenant_id=@TenantId and lower(name)=lower(@Name) order by id",
            new { tenant.TenantId, Name = name }, t, cancellationToken: ct)).ConfigureAwait(false)).ToArray();
        if (ids.Length != 1)
            throw new ConflictException("Não foi possível obter um único cargo canônico após o conflito de gravação.");
        return ids[0];
    }

    private async Task<AllocationGraph> LoadAllocationGraphAsync(DbConnection c, DbTransaction t, Guid propertyId, Guid? plotId, Guid? seasonId, Guid? orderId, Guid? personId, Guid? teamId, CancellationToken ct)
    {
        await ResolvePropertyAsync(c, t, propertyId, ct).ConfigureAwait(false);
        if (plotId is not null && !await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from agro360.geo_fields where tenant_id=@TenantId and id=@Id and farm_id=@FarmId and deleted_at is null)",
            new { tenant.TenantId, Id = plotId, FarmId = propertyId }, t, cancellationToken: ct)).ConfigureAwait(false))
            throw new DomainException("O talhão não pertence à propriedade.", "rural_hr.plot_invalid");
        if (seasonId is not null && !await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from agro360.agriculture_seasons where tenant_id=@TenantId and id=@Id and farm_id=@FarmId and deleted_at is null and status not in (4, 5))",
            new { tenant.TenantId, Id = seasonId, FarmId = propertyId }, t, cancellationToken: ct)).ConfigureAwait(false))
            throw new DomainException("A safra não pertence à propriedade ou está encerrada.", "rural_hr.season_invalid");
        if (personId is not null && !await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from agro360.rural_hr_people where tenant_id=@TenantId and id=@Id and status='ACTIVE')",
            new { tenant.TenantId, Id = personId }, t, cancellationToken: ct)).ConfigureAwait(false))
            throw new DomainException("A pessoa não está disponível.", "rural_hr.person_unavailable");
        if (teamId is not null && !await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from agro360.rural_hr_records where tenant_id=@TenantId and id=@Id and kind='TEAM' and status in ('ACTIVE','IN_FIELD'))",
            new { tenant.TenantId, Id = teamId }, t, cancellationToken: ct)).ConfigureAwait(false))
            throw new DomainException("A equipe não está disponível.", "rural_hr.team_unavailable");
        if (orderId is null) return new AllocationGraph(plotId, seasonId, null);
        var order = await c.QuerySingleOrDefaultAsync<OrderLink>(new CommandDefinition(
            """
            select status Status, data->>'propertyId' PropertyId, data->>'fieldId' FieldId, data->>'cropSeasonId' SeasonId
            from agro360.agriculture_records
            where tenant_id=@TenantId and id=@Id and module='work-orders' and deleted_at is null
            """,
            new { tenant.TenantId, Id = orderId }, t, cancellationToken: ct)).ConfigureAwait(false)
            ?? throw new DomainException("A ordem de campo não pertence a este tenant.", "rural_hr.order_invalid");
        if (order.Status is "CANCELLED" or "CLOSED")
            throw new DomainException("A ordem de campo está encerrada.", "rural_hr.order_closed");
        if (!Guid.TryParse(order.PropertyId, out var orderProperty) || orderProperty != propertyId)
            throw new DomainException("A ordem de campo é de outra propriedade.", "rural_hr.order_property");
        var orderField = Guid.TryParse(order.FieldId, out var parsedField) ? parsedField : (Guid?)null;
        var orderSeason = Guid.TryParse(order.SeasonId, out var parsedSeason) ? parsedSeason : (Guid?)null;
        if (plotId is not null && orderField is not null && plotId != orderField)
            throw new DomainException("O talhão não é o da ordem de campo.", "rural_hr.order_plot");
        if (seasonId is not null && orderSeason is not null && seasonId != orderSeason)
            throw new DomainException("A safra não é a da ordem de campo.", "rural_hr.order_season");
        return new AllocationGraph(plotId ?? orderField, seasonId ?? orderSeason, orderId);
    }

    private async Task EnsureNoOverlapAsync(DbConnection c, DbTransaction t, Guid? id, Guid? personId, Guid? teamId, DateTimeOffset starts, DateTimeOffset ends, CancellationToken ct)
    {
        var conflict = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists(
                select 1 from agro360.rural_hr_records a
                where a.tenant_id=@TenantId and a.kind='ALLOCATION' and a.status='ACTIVE'
                  and (@Id is null or a.id<>@Id)
                  and a.starts_at < @Ends and a.ends_at > @Starts
                  and ((@PersonId is not null and a.person_id=@PersonId) or (@TeamId is not null and a.team_id=@TeamId)))
            """,
            new { tenant.TenantId, Id = id, PersonId = personId, TeamId = teamId, Starts = starts, Ends = ends }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (conflict)
            throw new ConflictException("Já existe alocação ativa da mesma pessoa ou equipe neste período.");
    }

    private async Task EnsureExecutionPreservedAsync(DbConnection c, DbTransaction t, Guid id, Guid? personId, DateTimeOffset starts, DateTimeOffset ends, CancellationToken ct)
    {
        var executed = await c.QuerySingleOrDefaultAsync<ExecutionSpan>(new CommandDefinition(
            """
            select count(*)::int Entries, min(started_at) Started, max(coalesce(ended_at, started_at)) Ended, min(person_id::text) PersonId
            from agro360.rural_hr_time_entries
            where tenant_id=@TenantId and allocation_id=@Id and status<>'CANCELLED'
            """,
            new { tenant.TenantId, Id = id }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (executed is null || executed.Entries == 0) return;
        if (personId is not null && executed.PersonId is not null && !string.Equals(executed.PersonId, personId.Value.ToString(), StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("A execução já registrada impede trocar a pessoa da alocação.");
        if (executed.Started < starts || executed.Ended > ends)
            throw new ConflictException("O novo período não cobre a jornada já executada.");
    }

    private async Task EnsureSeasonOpenAsync(DbConnection c, DbTransaction t, Guid seasonId, DateOnly competence, CancellationToken ct)
    {
        var closed = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists(select 1 from agro360.agriculture_seasons where tenant_id=@TenantId and id=@Id and status in (4, 5))
                or exists(
                    select 1 from agro360.harvest_closing_versions v
                    where v.tenant_id=@TenantId and v.season_id=@Id and v.state='CLOSED' and v.cutoff_date >= @Competence
                      and not exists(
                        select 1 from agro360.harvest_closing_versions n
                        where n.tenant_id=v.tenant_id and n.season_id=v.season_id and n.version>v.version))
            """,
            new { tenant.TenantId, Id = seasonId, Competence = competence }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (closed) throw new ConflictException("A safra está em período fechado. A correção não altera o fechamento.");
    }

    private async Task SyncFieldResourceAsync(DbConnection c, DbTransaction t, Guid allocationId, Guid? orderId, Guid? personId, DateTimeOffset starts, DateTimeOffset ends, CancellationToken ct)
    {
        if (orderId is null || personId is null) return;
        var existing = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
            "select id from agro360.field_work_order_resources where tenant_id=@TenantId and hr_allocation_id=@AllocationId and deleted_at is null and status='RESERVED'",
            new { tenant.TenantId, AllocationId = allocationId }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (existing is null)
        {
            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.field_work_order_resources(
                    id, tenant_id, work_order_id, resource_type, resource_id, starts_at, ends_at, status, created_by, updated_by, hr_allocation_id)
                values(@Id, @TenantId, @OrderId, 'PERSON', @PersonId, @Starts, @Ends, 'RESERVED', @UserId, @UserId, @AllocationId)
                """,
                new { Id = Guid.CreateVersion7(), tenant.TenantId, OrderId = orderId, PersonId = personId, Starts = starts, Ends = ends, tenant.UserId, AllocationId = allocationId }, t, cancellationToken: ct)).ConfigureAwait(false);
            return;
        }
        await c.ExecuteAsync(new CommandDefinition(
            "update agro360.field_work_order_resources set starts_at=@Starts, ends_at=@Ends, updated_by=@UserId, updated_at=now() where tenant_id=@TenantId and id=@Id",
            new { tenant.TenantId, Id = existing, Starts = starts, Ends = ends, tenant.UserId }, t, cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task UpsertPersonProjectionAsync(DbConnection c, DbTransaction t, Guid personId, string name, Guid propertyId, string status, CancellationToken ct)
    {
        await c.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.rural_hr_records(
                id, tenant_id, kind, name, person_id, property_id, status, canonical_table, canonical_id, created_by, updated_by)
            values(@Id, @TenantId, 'PERSON', @Name, @Id, @PropertyId, @Status, 'rural_hr_people', @Id, @UserId, @UserId)
            on conflict (id) do update set
                kind='PERSON', name=excluded.name, person_id=excluded.person_id, property_id=excluded.property_id,
                status=excluded.status, canonical_table='rural_hr_people', canonical_id=excluded.canonical_id,
                updated_by=excluded.updated_by, updated_at=now()
            """,
            new { Id = personId, tenant.TenantId, Name = name, PropertyId = propertyId, Status = status, tenant.UserId }, t, cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task UpsertJourneyProjectionAsync(DbConnection c, DbTransaction t, Guid entryId, CancellationToken ct)
    {
        await c.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.rural_hr_records(
                id, tenant_id, kind, name, person_id, team_id, property_id, resource_id, starts_at, started_at, ends_at, ended_at,
                break_minutes, activity_type, status, order_id, season_id, plot_id, canonical_table, canonical_id, created_by, updated_by)
            select e.id, e.tenant_id, 'TIME_ENTRY', coalesce(p.name, 'Jornada'), e.person_id, e.team_id, e.property_id, e.resource_id,
                   e.started_at, e.started_at, e.ended_at, e.ended_at, e.break_minutes, e.activity_type, e.status,
                   e.order_id, e.season_id, e.plot_id, 'rural_hr_time_entries', e.id, e.created_by, @UserId
            from agro360.rural_hr_time_entries e
            left join agro360.rural_hr_people p on p.tenant_id=e.tenant_id and p.id=e.person_id
            where e.tenant_id=@TenantId and e.id=@Id
            on conflict (id) do update set
                name=excluded.name, status=excluded.status, ends_at=excluded.ends_at, ended_at=excluded.ended_at,
                break_minutes=excluded.break_minutes, canonical_table='rural_hr_time_entries', canonical_id=excluded.canonical_id,
                updated_by=excluded.updated_by, updated_at=now()
            """,
            new { tenant.TenantId, Id = entryId, tenant.UserId }, t, cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task SyncProjectionAsync(DbConnection c, DbTransaction t, Guid id, string kind, string status, string? name, CancellationToken ct)
    {
        if (kind == "TIME_ENTRY")
        {
            await UpsertJourneyProjectionAsync(c, t, id, ct).ConfigureAwait(false);
            return;
        }
        await c.ExecuteAsync(new CommandDefinition(
            "update agro360.rural_hr_records set status=@Status, name=coalesce(@Name, name), updated_by=@UserId, updated_at=now() where tenant_id=@TenantId and canonical_id=@Id and kind=@Kind",
            new { tenant.TenantId, Id = id, Status = status, Name = name, Kind = kind, tenant.UserId }, t, cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task<JourneyRow> LockEntryAsync(DbConnection c, DbTransaction t, Guid id, CancellationToken ct)
    {
        var row = await c.QuerySingleOrDefaultAsync<JourneyRow>(new CommandDefinition(
            """
            select id Id, person_id PersonId, property_id PropertyId, season_id SeasonId, plot_id PlotId, activity_type ActivityType,
                   started_at StartedAt, ended_at EndedAt, break_minutes BreakMinutes, status Status, review_status ReviewStatus,
                   hours_worked HoursWorked, piece_quantity PieceQuantity, tariff_id TariffId, rate_type RateType, rate_value RateValue,
                   cost_amount CostAmount, cost_status CostStatus, cost_entry_id CostEntryId, allocation_batch_id AllocationBatchId, version Version
            from agro360.rural_hr_time_entries where tenant_id=@TenantId and id=@Id for update
            """,
            new { tenant.TenantId, Id = id }, t, cancellationToken: ct)).ConfigureAwait(false);
        return row ?? throw new KeyNotFoundException("Jornada não encontrada.");
    }

    private async Task<bool> BatchIsConfirmedAsync(DbConnection c, DbTransaction t, Guid batchId, CancellationToken ct) =>
        await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from agro360.cost_allocation_batches where tenant_id=@TenantId and id=@Id and status='CONFIRMED')",
            new { tenant.TenantId, Id = batchId }, t, cancellationToken: ct)).ConfigureAwait(false);

    private async Task AlignRecognizedCostAsync(DbConnection c, DbTransaction t, Guid entryId, Guid costId, CancellationToken ct)
    {
        var amount = await c.ExecuteScalarAsync<decimal?>(new CommandDefinition(
            "select cost_amount from agro360.rural_hr_time_entries where tenant_id=@TenantId and id=@Id",
            new { tenant.TenantId, Id = entryId }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (amount is null)
            throw new DomainException("A correção deixou o custo indisponível e já existe lançamento. Ajuste a tarifa ou informe a quantidade.", "rural_hr.cost_blocked");
        var updated = await c.ExecuteAsync(new CommandDefinition(
            """
            update agro360.cost_management_entries
               set recognized_amount=@Amount, row_version=row_version+1, updated_at=now(), updated_by=@UserId
             where tenant_id=@TenantId and id=@Id and status='OPEN' and deleted_at is null and allocated_amount=0 and allocated_amount<=@Amount
            """,
            new { tenant.TenantId, Id = costId, Amount = amount, tenant.UserId }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (updated == 0) throw new ConflictException("O lançamento de custo já foi apropriado ou não está aberto.");
        await c.ExecuteAsync(new CommandDefinition(
            "update agro360.rural_hr_time_corrections set new_cost_amount=@Amount where tenant_id=@TenantId and time_entry_id=@Id and new_cost_amount is null and created_at=(select max(created_at) from agro360.rural_hr_time_corrections where tenant_id=@TenantId and time_entry_id=@Id)",
            new { tenant.TenantId, Id = entryId, Amount = amount }, t, cancellationToken: ct)).ConfigureAwait(false);
    }

    private async Task WriteEventAsync(DbConnection c, DbTransaction t, Guid recordId, string type, string? reason, object snapshot, CancellationToken ct) =>
        await c.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.rural_hr_record_events(id, tenant_id, record_id, event_type, reason, snapshot, created_by)
            values(@Id, @TenantId, @RecordId, @Type, @Reason, cast(@Snapshot as jsonb), @UserId)
            """,
            new { Id = Guid.CreateVersion7(), tenant.TenantId, RecordId = recordId, Type = type, Reason = reason, Snapshot = JsonSerializer.Serialize(snapshot), tenant.UserId }, t, cancellationToken: ct)).ConfigureAwait(false);

    private async Task<Guid?> ReplayAsync(DbConnection c, DbTransaction t, string command, string key, string hash, CancellationToken ct)
    {
        var row = await c.QuerySingleOrDefaultAsync<ReplayRow>(new CommandDefinition(
            "select entity_id EntityId, request_hash RequestHash from agro360.rural_hr_command_replays where tenant_id=@TenantId and command_name=@Command and idempotency_key=@Key",
            new { tenant.TenantId, Command = command, Key = key.Trim() }, t, cancellationToken: ct)).ConfigureAwait(false);
        if (row is null) return null;
        if (!string.Equals(row.RequestHash, hash, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("A mesma chave de idempotência foi reenviada com dados diferentes.");
        return row.EntityId;
    }

    private async Task RememberAsync(DbConnection c, DbTransaction t, string command, string key, Guid entityId, string hash, CancellationToken ct) =>
        await c.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.rural_hr_command_replays(tenant_id, command_name, idempotency_key, entity_id, request_hash)
            values(@TenantId, @Command, @Key, @EntityId, @Hash)
            """,
            new { tenant.TenantId, Command = command, Key = key.Trim(), EntityId = entityId, Hash = hash }, t, cancellationToken: ct)).ConfigureAwait(false);

    private static string JourneyHash(TimeEntryCommand command) =>
        $"{command.PersonId}|{command.TeamId}|{command.PropertyId}|{command.StartedAt:O}|{command.EndedAt:O}|{command.BreakMinutes}|{command.ActivityType}|{command.AllocationId}|{command.PieceQuantity}";

    private static string Hash(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();

    private static string CountJourneys(string predicate) =>
        $"""
        select count(*) from agro360.rural_hr_time_entries t
        where t.tenant_id=@TenantId and {predicate}
          and (@PropertyId is null or t.property_id=@PropertyId) and (@SeasonId is null or t.season_id=@SeasonId) and (@TeamId is null or t.team_id=@TeamId)
          and (@From is null or (t.started_at at time zone 'UTC')::date >= @From) and (@To is null or (t.started_at at time zone 'UTC')::date <= @To)
        """;

    private static string SumJourneys(string predicate) =>
        $"""
        select sum(t.cost_amount) from agro360.rural_hr_time_entries t
        where t.tenant_id=@TenantId and {predicate}
          and (@PropertyId is null or t.property_id=@PropertyId) and (@SeasonId is null or t.season_id=@SeasonId) and (@TeamId is null or t.team_id=@TeamId)
          and (@From is null or (t.started_at at time zone 'UTC')::date >= @From) and (@To is null or (t.started_at at time zone 'UTC')::date <= @To)
        """;

    private static (string Sql, object Args) BoardItems(string situation, object filter, int page, int size)
    {
        var sql = situation switch
        {
            "ALLOCATED" => BoardAllocation("a.status='ACTIVE'"),
            "CONFLICT" => BoardAllocation("""
                a.status='ACTIVE' and exists(
                    select 1 from agro360.rural_hr_records b
                    where b.tenant_id=a.tenant_id and b.kind='ALLOCATION' and b.status='ACTIVE' and b.id<>a.id
                      and b.starts_at < a.ends_at and b.ends_at > a.starts_at
                      and ((a.person_id is not null and b.person_id=a.person_id) or (a.team_id is not null and b.team_id=a.team_id)))
                """),
            "AWAITING_REVIEW" => BoardJourney("t.status='CLOSED' and t.review_status='PENDING'"),
            "COST_REALIZED" => BoardJourney("t.review_status='CONFIRMED' and t.cost_status='CALCULATED'"),
            "COST_PENDING_TARIFF" => BoardJourney("t.status='CLOSED' and t.cost_status='UNAVAILABLE'"),
            "APPROPRIATED" => BoardJourney("t.allocation_batch_id is not null"),
            "AVAILABLE_BALANCE" => BoardJourney("t.cost_entry_id is not null and t.review_status='CONFIRMED'"),
            _ => BoardJourney("t.status='OPEN'")
        };
        var args = new DynamicParameters(filter);
        args.Add("Limit", size);
        args.Add("Offset", (page - 1) * size);
        return (sql, args);
    }

    private static string BoardCount(string situation)
    {
        var journey = (string predicate) =>
            $"""
            select count(*) from agro360.rural_hr_time_entries t
            where t.tenant_id=@TenantId and {predicate}
              and (@PropertyId is null or t.property_id=@PropertyId) and (@SeasonId is null or t.season_id=@SeasonId) and (@TeamId is null or t.team_id=@TeamId)
              and (@From is null or (t.started_at at time zone 'UTC')::date >= @From) and (@To is null or (t.started_at at time zone 'UTC')::date <= @To)
            """;
        var allocation = (string predicate) =>
            $"""
            select count(*) from agro360.rural_hr_records a
            where a.tenant_id=@TenantId and a.kind='ALLOCATION' and {predicate}
              and (@PropertyId is null or a.property_id=@PropertyId) and (@SeasonId is null or a.season_id=@SeasonId) and (@TeamId is null or a.team_id=@TeamId)
              and (@From is null or a.ends_at::date >= @From) and (@To is null or a.starts_at::date <= @To)
            """;
        return situation switch
        {
            "ALLOCATED" => allocation("a.status='ACTIVE'"),
            "CONFLICT" => allocation("""
                a.status='ACTIVE' and exists(
                    select 1 from agro360.rural_hr_records b
                    where b.tenant_id=a.tenant_id and b.kind='ALLOCATION' and b.status='ACTIVE' and b.id<>a.id
                      and b.starts_at < a.ends_at and b.ends_at > a.starts_at
                      and ((a.person_id is not null and b.person_id=a.person_id) or (a.team_id is not null and b.team_id=a.team_id)))
                """),
            "AWAITING_REVIEW" => journey("t.status='CLOSED' and t.review_status='PENDING'"),
            "COST_REALIZED" => journey("t.review_status='CONFIRMED' and t.cost_status='CALCULATED'"),
            "COST_PENDING_TARIFF" => journey("t.status='CLOSED' and t.cost_status='UNAVAILABLE'"),
            "APPROPRIATED" => journey("t.allocation_batch_id is not null"),
            "AVAILABLE_BALANCE" => journey("t.cost_entry_id is not null and t.review_status='CONFIRMED'"),
            _ => journey("t.status='OPEN'")
        };
    }

    private static string BoardJourney(string predicate) =>
        $"""
        select t.id Id, 'TIME_ENTRY' Kind, coalesce(p.name, 'Jornada') Name, t.status Status, t.person_id PersonId, t.team_id TeamId,
               t.started_at StartsAt, t.ended_at EndsAt, coalesce(t.cost_amount, 0) Amount, t.updated_at UpdatedAt,
               t.cost_status CostState, t.allocation_id AllocationId, t.cost_block BlockReason, t.review_status ReviewStatus, count(*) over() TotalCount
        from agro360.rural_hr_time_entries t
        left join agro360.rural_hr_people p on p.tenant_id=t.tenant_id and p.id=t.person_id
        where t.tenant_id=@TenantId and {predicate}
          and (@PropertyId is null or t.property_id=@PropertyId) and (@SeasonId is null or t.season_id=@SeasonId) and (@TeamId is null or t.team_id=@TeamId)
          and (@From is null or (t.started_at at time zone 'UTC')::date >= @From) and (@To is null or (t.started_at at time zone 'UTC')::date <= @To)
        order by t.started_at desc
        limit @Limit offset @Offset
        """;

    private static string BoardAllocation(string predicate) =>
        $"""
        select a.id Id, 'ALLOCATION' Kind, a.name Name, a.status Status, a.person_id PersonId, a.team_id TeamId,
               a.starts_at StartsAt, a.ends_at EndsAt, 0 Amount, a.updated_at UpdatedAt,
               null CostState, a.id AllocationId, null BlockReason, count(*) over() TotalCount
        from agro360.rural_hr_records a
        where a.tenant_id=@TenantId and a.kind='ALLOCATION' and {predicate}
          and (@PropertyId is null or a.property_id=@PropertyId) and (@SeasonId is null or a.season_id=@SeasonId) and (@TeamId is null or a.team_id=@TeamId)
          and (@From is null or a.ends_at::date >= @From) and (@To is null or a.starts_at::date <= @To)
        order by a.starts_at desc
        limit @Limit offset @Offset
        """;

    private sealed class BatchLink { public Guid Id { get; set; } public string Status { get; set; } = ""; }
    private sealed class ReplayRow { public Guid EntityId { get; set; } public string RequestHash { get; set; } = ""; }
    private sealed class PersonMatch { public Guid Id { get; set; } public string Name { get; set; } = ""; public Guid RoleId { get; set; } public Guid PropertyId { get; set; } }
    private sealed class AllocationRow { public Guid Id { get; set; } public Guid? PersonId { get; set; } public Guid? TeamId { get; set; } public string Status { get; set; } = ""; }
    private sealed record AllocationLink(Guid Id, Guid? PersonId, Guid? TeamId, Guid? PropertyId, Guid? PlotId, Guid? SeasonId, Guid? OrderId, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, string Status);
    private sealed record OrderLink(string Status, string? PropertyId, string? FieldId, string? SeasonId);
    private sealed record AllocationGraph(Guid? PlotId, Guid? SeasonId, Guid? OrderId);
    private sealed class ExecutionSpan { public int Entries { get; set; } public DateTimeOffset Started { get; set; } public DateTimeOffset Ended { get; set; } public string? PersonId { get; set; } }
    private sealed record JourneyRow(Guid Id, Guid PersonId, Guid PropertyId, Guid? SeasonId, Guid? PlotId, string ActivityType, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, int BreakMinutes, string Status, string ReviewStatus, decimal? HoursWorked, decimal? PieceQuantity, Guid? TariffId, string? RateType, decimal? RateValue, decimal? CostAmount, string? CostStatus, Guid? CostEntryId, Guid? AllocationBatchId, long Version);
    private sealed class BoardItem
    {
        public Guid Id { get; set; }
        public string Kind { get; set; } = "";
        public string Name { get; set; } = "";
        public string Status { get; set; } = "";
        public Guid? PersonId { get; set; }
        public Guid? TeamId { get; set; }
        public DateTimeOffset? StartsAt { get; set; }
        public DateTimeOffset? EndsAt { get; set; }
        public decimal Amount { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public string? CostState { get; set; }
        public Guid? AllocationId { get; set; }
        public string? BlockReason { get; set; }
        public string? ReviewStatus { get; set; }
        public int TotalCount { get; set; }
        public RuralHrRecord ToRecord() => new(Id, Kind, Name, Status, PersonId, TeamId, StartsAt, EndsAt, Amount, UpdatedAt, CostState, AllocationId, BlockReason, ReviewStatus);
    }
}
