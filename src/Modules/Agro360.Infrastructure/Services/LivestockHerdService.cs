using System.Diagnostics;
using System.Globalization;
using System.Text;
using Agro360.Application.Contracts;
using Agro360.Domain.Livestock;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Agro360.Infrastructure.Services;

public sealed class LivestockHerdService(
    DatabaseExecutor db,
    ITenantContext tenant,
    ILogger<LivestockHerdService> logger) : ILivestockHerdService
{
    private static readonly Dictionary<string, (string Table, string Insert, string Update)> Catalogs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["species"] = ("agro360.livestock_species", "code,name,active,created_at,created_by", "name=@Name,active=@Active"),
        ["categories"] = ("agro360.livestock_categories", "species_code,code,name,sex,active,created_at,created_by", "name=@Name,sex=@Sex,active=@Active"),
        ["breeds"] = ("agro360.livestock_breeds", "species_code,code,name,active,created_at,created_by", "name=@Name,active=@Active"),
        ["reasons"] = ("agro360.livestock_movement_reasons", "code,name,direction,active,created_at,created_by", "name=@Name,active=@Active"),
        ["handling-types"] = ("agro360.livestock_handling_types", "code,name,active,created_at,created_by", "name=@Name,active=@Active"),
        ["protocols"] = ("agro360.livestock_health_protocols", "name,source,version_label,valid_from,valid_until,purpose,withdrawal_days,notes,demo_only,active,created_at,created_by", "notes=@Notes,active=@Active")
    };

    public Task<PagedResult<LookupItem>> LookupsAsync(string resource, string? search, int page, int pageSize, CancellationToken ct)
    {
        var source = LookupSql(resource);
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 50);
        var sql = $"""
            select count(*) from ({source}) q
            where (@Search='' or label ilike '%'||@Search||'%' or description ilike '%'||@Search||'%');
            select id, label, description, status from ({source}) q
            where (@Search='' or label ilike '%'||@Search||'%' or description ilike '%'||@Search||'%')
            order by label limit @PageSize offset @Offset;
            """;
        return Tx(async (c, t) =>
        {
            using var grid = await c.QueryMultipleAsync(new CommandDefinition(sql, new
            {
                tenant.TenantId,
                FarmId = tenant.FarmId,
                Search = search?.Trim() ?? "",
                PageSize = pageSize,
                Offset = (page - 1) * pageSize
            }, t, cancellationToken: ct));
            var total = await grid.ReadSingleAsync<long>();
            var rows = await grid.ReadAsync<LookupRow>();
            return new PagedResult<LookupItem>(
                rows.Select(x => new LookupItem(x.Id, x.Label, x.Description, x.Status, new Dictionary<string, object?>())).ToArray(),
                page, pageSize, total);
        }, ct);
    }

    public Task<IReadOnlyList<dynamic>> ListCatalogAsync(string kind, CancellationToken ct)
    {
        var table = Catalogs.TryGetValue(kind, out var spec) ? spec.Table : throw new NotFoundException("Catálogo", Guid.Empty);
        return List($"select * from {table} where tenant_id=@TenantId order by name", ct);
    }

    public Task<Guid> SaveCatalogAsync(string kind, Guid? id, LivestockCatalogCommand command, CancellationToken ct)
    {
        if (!Catalogs.TryGetValue(kind, out var spec))
            throw new NotFoundException("Catálogo", Guid.Empty);
        var code = Guard.Required(command.Code, nameof(command.Code), 40).ToUpperInvariant();
        var name = Guard.Required(command.Name, nameof(command.Name), 80);
        return Tx(async (c, t) =>
        {
            var key = id ?? Guid.CreateVersion7();
            var sql = (kind.ToLowerInvariant(), id is null) switch
            {
                ("categories", true) => "insert into agro360.livestock_categories(id,tenant_id,species_code,code,name,sex,active,created_by) values(@Id,@TenantId,@SpeciesCode,@Code,@Name,@Sex,true,@UserId)",
                ("categories", false) => "update agro360.livestock_categories set name=@Name,sex=@Sex,active=@Active,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id",
                ("breeds", true) => "insert into agro360.livestock_breeds(id,tenant_id,species_code,code,name,active,created_by) values(@Id,@TenantId,@SpeciesCode,@Code,@Name,true,@UserId)",
                ("reasons", true) => "insert into agro360.livestock_movement_reasons(id,tenant_id,code,name,direction,active,created_by) values(@Id,@TenantId,@Code,@Name,coalesce(@Sex,'ENTRY'),true,@UserId)",
                (_, true) => $"insert into {spec.Table}(id,tenant_id,code,name,active,created_by) values(@Id,@TenantId,@Code,@Name,true,@UserId)",
                _ => $"update {spec.Table} set name=@Name,active=@Active,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id"
            };
            var n = await c.ExecuteAsync(new CommandDefinition(sql, new
            {
                Id = key,
                tenant.TenantId,
                Code = code,
                Name = name,
                command.Active,
                SpeciesCode = command.SpeciesCode ?? "BOVINE",
                command.Sex,
                tenant.UserId
            }, t, cancellationToken: ct));
            if (n == 0) throw new NotFoundException(kind, key);
            await Audit(c, t, id is null ? "create" : "update", kind, key, command, ct);
            return key;
        }, ct);
    }

    public Task<IReadOnlyList<dynamic>> ListFacilitiesAsync(Guid? farmId, CancellationToken ct) =>
        List("""
            select id, farm_id as "farmId", name, kind, paddock_id as "paddockId",
                   capacity_head as "capacityHead", status, notes
            from agro360.livestock_facilities
            where tenant_id=@TenantId and deleted_at is null
              and (@FarmId is null or farm_id=@FarmId)
            order by name
            """, ct, farmId ?? tenant.FarmId);

    public Task<Guid> SaveFacilityAsync(Guid? id, FacilityCommand command, CancellationToken ct)
    {
        Guard.Required(command.FarmId, nameof(command.FarmId));
        var name = Guard.Required(command.Name, nameof(command.Name), 120);
        var kind = Guard.Required(command.Kind, nameof(command.Kind), 30).ToUpperInvariant();
        if (kind is not ("PASTURE" or "PADDOCK" or "CORRAL" or "BARN" or "PEN" or "OTHER"))
            throw new DomainException("Tipo de instalação inválido.", "livestock.facility_kind_invalid");
        return Tx(async (c, t) =>
        {
            await EnsureFarmAsync(c, t, command.FarmId, ct);
            var key = id ?? Guid.CreateVersion7();
            if (id is null)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    """
                    insert into agro360.livestock_facilities
                        (id,tenant_id,farm_id,name,kind,paddock_id,capacity_head,status,notes,created_by)
                    values (@Id,@TenantId,@FarmId,@Name,@Kind,@PaddockId,@CapacityHead,@Status,@Notes,@UserId)
                    """,
                    new { Id = key, tenant.TenantId, command.FarmId, Name = name, Kind = kind, command.PaddockId, command.CapacityHead, Status = string.IsNullOrWhiteSpace(command.Status) ? "AVAILABLE" : command.Status.ToUpperInvariant(), command.Notes, tenant.UserId },
                    t, cancellationToken: ct));
            }
            else
            {
                var n = await c.ExecuteAsync(new CommandDefinition(
                    """
                    update agro360.livestock_facilities
                    set name=@Name,kind=@Kind,paddock_id=@PaddockId,capacity_head=@CapacityHead,status=@Status,notes=@Notes,
                        updated_at=now(),updated_by=@UserId
                    where tenant_id=@TenantId and id=@Id and deleted_at is null
                    """,
                    new { Id = key, tenant.TenantId, Name = name, Kind = kind, command.PaddockId, command.CapacityHead, Status = command.Status.ToUpperInvariant(), command.Notes, tenant.UserId },
                    t, cancellationToken: ct));
                if (n == 0) throw new NotFoundException("Instalação", key);
            }
            await Audit(c, t, id is null ? "create" : "update", "Facility", key, command, ct);
            return key;
        }, ct);
    }

    public Task<IReadOnlyList<dynamic>> ListHandlingLotsAsync(Guid? farmId, CancellationToken ct) =>
        List("""
            select l.id, l.farm_id as "farmId", l.name, l.purpose, l.status, l.notes,
                   (select count(*) from agro360.livestock_handling_lot_members m
                    where m.tenant_id=l.tenant_id and m.lot_id=l.id and m.removed_on is null) as "headCount"
            from agro360.livestock_handling_lots l
            where l.tenant_id=@TenantId and (@FarmId is null or l.farm_id=@FarmId)
            order by l.name
            """, ct, farmId ?? tenant.FarmId);

    public Task<Guid> SaveHandlingLotAsync(Guid? id, HandlingLotCommand command, CancellationToken ct)
    {
        var name = Guard.Required(command.Name, nameof(command.Name), 120);
        var purpose = Guard.Required(command.Purpose, nameof(command.Purpose), 80);
        return Tx(async (c, t) =>
        {
            await EnsureFarmAsync(c, t, command.FarmId, ct);
            var key = id ?? Guid.CreateVersion7();
            if (id is null)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    """
                    insert into agro360.livestock_handling_lots(id,tenant_id,farm_id,name,purpose,status,notes,created_by)
                    values(@Id,@TenantId,@FarmId,@Name,@Purpose,'OPEN',@Notes,@UserId)
                    """,
                    new { Id = key, tenant.TenantId, command.FarmId, Name = name, Purpose = purpose, command.Notes, tenant.UserId },
                    t, cancellationToken: ct));
            }
            else
            {
                var n = await c.ExecuteAsync(new CommandDefinition(
                    "update agro360.livestock_handling_lots set name=@Name,purpose=@Purpose,notes=@Notes,updated_at=now(),updated_by=@UserId where tenant_id=@TenantId and id=@Id",
                    new { Id = key, tenant.TenantId, Name = name, Purpose = purpose, command.Notes, tenant.UserId },
                    t, cancellationToken: ct));
                if (n == 0) throw new NotFoundException("Lote de manejo", key);
            }
            await Audit(c, t, id is null ? "create" : "update", "HandlingLot", key, command, ct);
            return key;
        }, ct);
    }

    public Task ReconcileHerdAsync(ReconcileHerdCommand command, CancellationToken ct)
    {
        LivestockRules.EnsurePositiveQuantity(command.AnimalIds.Count, "Animais identificados");
        return Tx(async (c, t) =>
        {
            var herd = await c.QuerySingleOrDefaultAsync<HerdRow>(new CommandDefinition(
                "select id, control_mode as ControlMode, head_count as HeadCount, farm_id as FarmId from agro360.livestock_herds where tenant_id=@TenantId and id=@Id and deleted_at is null for update",
                new { tenant.TenantId, Id = command.HerdId }, t, cancellationToken: ct))
                ?? throw new NotFoundException("Grupo", command.HerdId);
            if (!string.Equals(herd.ControlMode, "QUANTITY", StringComparison.OrdinalIgnoreCase))
                throw new ConflictException("Somente grupo controlado por quantidade pode ser conciliado para indivíduos.", "livestock.reconcile_mode");
            if (command.AnimalIds.Count != herd.HeadCount)
                throw new DomainException("A conciliação deve identificar exatamente a quantidade atual do grupo, sem criar cabeças artificiais.", "livestock.reconcile_count");
            foreach (var animalId in command.AnimalIds.Distinct())
            {
                var animal = await LockAnimalAsync(c, t, animalId, ct);
                LivestockRules.EnsureOnFarm(animal.Status, "conciliar");
                if (animal.HerdId == command.HerdId)
                    continue;
                if (animal.HerdId is not null)
                    throw new ConflictException("Animal já pertence a outro grupo.", "livestock.animal_already_grouped");
                await c.ExecuteAsync(new CommandDefinition(
                    "update agro360.livestock_animals set herd_id=@HerdId,updated_at=now(),updated_by=@UserId,version=version+1 where tenant_id=@TenantId and id=@Id",
                    new { HerdId = command.HerdId, tenant.TenantId, tenant.UserId, Id = animalId }, t, cancellationToken: ct));
            }
            await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.livestock_herds
                set control_mode='INDIVIDUAL', head_count=@Count, updated_at=now(), updated_by=@UserId, version=version+1
                where tenant_id=@TenantId and id=@Id;
                insert into agro360.livestock_herd_movements
                    (id,tenant_id,herd_id,movement_kind,quantity,occurred_on,reason_code,origin_notes,responsible_id,created_by)
                values (@Movement,@TenantId,@Id,'RECONCILE',@Count,@On,'RECONCILE',@Notes,@UserId,@UserId)
                """,
                new { Count = command.AnimalIds.Count, tenant.TenantId, tenant.UserId, Id = command.HerdId, Movement = Guid.CreateVersion7(), On = command.OccurredOn, command.Notes },
                t, cancellationToken: ct));
            await Audit(c, t, "reconcile", "Herd", command.HerdId, command, ct);
        }, ct);
    }

    public Task<dynamic?> AnimalDetailAsync(Guid id, CancellationToken ct) =>
        Tx<dynamic?>(async (c, t) =>
        {
            var animal = await c.QuerySingleOrDefaultAsync(new CommandDefinition(
                """
                select a.id, a.farm_id as "farmId", a.herd_id as "herdId", a.code as "internalCode", a.tag, a.rfid, a.sisbov,
                       a.species, a.breed, a.sex, a.birth_date as "birthDate", a.birth_date_estimated as "birthDateEstimated",
                       a.category, a.origin_type as "originType", a.origin_notes as "originNotes", a.notes,
                       a.paddock_id as "paddockId", a.facility_id as "facilityId", a.handling_lot_id as "handlingLotId",
                       case a.status when 1 then 'ACTIVE' when 2 then 'QUARANTINE' when 3 then 'SOLD'
                            when 4 then 'DEAD' when 5 then 'SLAUGHTERED' when 6 then 'RESERVED' else 'UNKNOWN' end as status,
                       a.current_weight_kg as "currentWeightKg", a.last_weight_date as "lastWeightDate",
                       a.withdrawal_until as "withdrawalUntil", a.inactivated_at as "inactivatedAt",
                       a.inactivated_reason as "inactivatedReason", a.reservation_id as "reservationId",
                       a.mother_id as "motherId", a.father_id as "fatherId", a.version,
                       h.name as "herdName", h.control_mode as "herdControlMode",
                       f.name as "facilityName", p.name as "paddockName", farm.name as "farmName",
                       a.created_at as "createdAt", cu.name as "createdByName",
                       a.updated_at as "updatedAt", uu.name as "updatedByName",
                       a.deleted_at as "deletedAt", du.name as "deletedByName", a.deletion_reason as "deletionReason"
                from agro360.livestock_animals a
                left join agro360.livestock_herds h on h.id=a.herd_id and h.tenant_id=a.tenant_id
                left join agro360.livestock_facilities f on f.id=a.facility_id and f.tenant_id=a.tenant_id
                left join agro360.livestock_paddocks p on p.id=a.paddock_id and p.tenant_id=a.tenant_id
                join agro360.geo_farms farm on farm.id=a.farm_id and farm.tenant_id=a.tenant_id
                left join agro360.identity_users cu on cu.tenant_id=a.tenant_id and cu.id=a.created_by
                left join agro360.identity_users uu on uu.tenant_id=a.tenant_id and uu.id=a.updated_by
                left join agro360.identity_users du on du.tenant_id=a.tenant_id and du.id=a.deleted_by
                where a.tenant_id=@TenantId and a.id=@Id
                """,
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
            if (animal is null) return null;
            var events = await c.QueryAsync(new CommandDefinition(
                """
                select event_type as "eventType", occurred_on as "occurredOn", data, cost_amount as "costAmount", created_at as "createdAt"
                from agro360.livestock_animal_events
                where tenant_id=@TenantId and animal_id=@Id
                order by occurred_on desc, created_at desc
                """,
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
            var weighings = await c.QueryAsync(new CommandDefinition(
                """
                select id, weighed_at as "weighedAt", weight_kg as "weightKg", unit, method, source, notes, review_required as "reviewRequired"
                from agro360.livestock_weighings
                where tenant_id=@TenantId and animal_id=@Id
                order by weighed_at desc
                """,
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
            var movements = await c.QueryAsync(new CommandDefinition(
                """
                select movement_kind as "kind", reason_code as "reasonCode", moved_on as "movedOn", notes,
                       from_farm_id as "fromFarmId", to_farm_id as "toFarmId"
                from agro360.livestock_animal_movements
                where tenant_id=@TenantId and animal_id=@Id
                order by moved_on desc, created_at desc
                """,
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
            var restrictions = await c.QueryAsync(new CommandDefinition(
                """
                select id, purpose, reason, started_on as "startedOn", expected_until as "expectedUntil",
                       status, demo_only as "demoOnly"
                from agro360.livestock_restrictions
                where tenant_id=@TenantId and animal_id=@Id
                order by started_on desc
                """,
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
            var costs = await c.QueryAsync(new CommandDefinition(
                """
                select category, nature, amount, occurred_on as "occurredOn", source_type as "sourceType", basis
                from agro360.livestock_cost_allocations
                where tenant_id=@TenantId and animal_id=@Id
                order by occurred_on desc
                """,
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
            var identifiers = await c.QueryAsync(new CommandDefinition(
                """
                select kind, value, assigned_on as "assignedOn", retired_on as "retiredOn", reason
                from agro360.livestock_animal_identifiers
                where tenant_id=@TenantId and animal_id=@Id
                order by assigned_on desc
                """,
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
            var audit = await c.QueryAsync(new CommandDefinition(
                """
                select action, occurred_at as "occurredAt", u.name as "actorName"
                from agro360.audit_logs l
                left join agro360.identity_users u on u.tenant_id=l.tenant_id and u.id=l.user_id
                where l.tenant_id=@TenantId and l.entity_type='Animal' and l.entity_id=@Id
                order by l.occurred_at desc
                limit 50
                """,
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
            return (object)new
            {
                animal,
                timeline = events,
                audit,
                weighings,
                movements,
                restrictions,
                costs,
                identifiers
            };
        }, ct);

    public Task ChangeTagAsync(Guid id, ChangeTagCommand command, CancellationToken ct)
    {
        var tag = Guard.Required(command.NewTag, nameof(command.NewTag), 80);
        return Tx(async (c, t) =>
        {
            var animal = await LockAnimalAsync(c, t, id, ct);
            var exists = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from agro360.livestock_animals where tenant_id=@TenantId and lower(tag)=lower(@Tag) and id<>@Id and deleted_at is null)",
                new { tenant.TenantId, Tag = tag, Id = id }, t, cancellationToken: ct));
            if (exists)
                throw new ConflictException("Identificador já utilizado neste cliente.", "livestock.tag_not_unique");
            await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.livestock_animal_identifiers
                set retired_on=@On, reason=@Reason
                where tenant_id=@TenantId and animal_id=@Id and kind='TAG' and retired_on is null;
                insert into agro360.livestock_animal_identifiers(id,tenant_id,animal_id,kind,value,assigned_on,created_by)
                values(@Ident,@TenantId,@Id,'TAG',@Tag,@On,@UserId);
                update agro360.livestock_animals
                set tag=@Tag, updated_at=now(), updated_by=@UserId, version=version+1
                where tenant_id=@TenantId and id=@Id;
                insert into agro360.livestock_animal_events(id,tenant_id,animal_id,event_type,occurred_on,data,created_at,created_by)
                values(@Event,@TenantId,@Id,'TAG_CHANGE',@On,jsonb_build_object('previous',@Previous,'current',@Tag,'reason',@Reason),now(),@UserId)
                """,
                new
                {
                    On = command.ChangedOn,
                    command.Reason,
                    tenant.TenantId,
                    Id = id,
                    Ident = Guid.CreateVersion7(),
                    Tag = tag,
                    tenant.UserId,
                    Event = Guid.CreateVersion7(),
                    Previous = animal.Tag
                }, t, cancellationToken: ct));
            await Audit(c, t, "tag-change", "Animal", id, command, ct);
        }, ct);
    }

    public Task InactivateAsync(Guid id, InactivateAnimalCommand command, CancellationToken ct)
    {
        var reason = Guard.Required(command.Reason, nameof(command.Reason), 240);
        return Tx(async (c, t) =>
        {
            var animal = await LockAnimalAsync(c, t, id, ct);
            LivestockRules.EnsureOnFarm(animal.Status, "inativar");
            if (animal.Status == 6)
                throw new ConflictException("Cancele a reserva comercial antes de inativar o cadastro.", "livestock.reserved");
            await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.livestock_animals
                set inactivated_at=now(), inactivated_reason=@Reason, updated_at=now(), updated_by=@UserId, version=version+1
                where tenant_id=@TenantId and id=@Id;
                insert into agro360.livestock_animal_events(id,tenant_id,animal_id,event_type,occurred_on,data,created_at,created_by)
                values(@Event,@TenantId,@Id,'INACTIVATION',@On,jsonb_build_object('reason',@Reason),now(),@UserId)
                """,
                new { Reason = reason, tenant.TenantId, tenant.UserId, Id = id, Event = Guid.CreateVersion7(), On = command.OccurredOn },
                t, cancellationToken: ct));
            await Audit(c, t, "inactivate", "Animal", id, command, ct);
        }, ct);
    }

    public Task SoftDeleteAsync(Guid id, string reason, CancellationToken ct)
    {
        var text = Guard.Required(reason, nameof(reason), 240);
        return Tx(async (c, t) =>
        {
            var animal = await LockAnimalAsync(c, t, id, ct);
            var hasHistory = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from agro360.livestock_animal_events where tenant_id=@TenantId and animal_id=@Id)",
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
            await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.livestock_animals
                set deleted_at=now(), deleted_by=@UserId, deletion_reason=@Reason, updated_at=now(), updated_by=@UserId, version=version+1
                where tenant_id=@TenantId and id=@Id and deleted_at is null;
                insert into agro360.livestock_animal_events(id,tenant_id,animal_id,event_type,occurred_on,data,created_at,created_by)
                values(@Event,@TenantId,@Id,'SOFT_DELETE',current_date,jsonb_build_object('reason',@Reason,'preservedHistory',@History),now(),@UserId)
                """,
                new { tenant.UserId, Reason = text, tenant.TenantId, Id = id, Event = Guid.CreateVersion7(), History = hasHistory },
                t, cancellationToken: ct));
            await Audit(c, t, "soft-delete", "Animal", id, new { reason = text, hasHistory }, ct);
        }, ct);
    }

    public Task RestoreAsync(Guid id, string reason, CancellationToken ct)
    {
        var text = Guard.Required(reason, nameof(reason), 240);
        return Tx(async (c, t) =>
        {
            var animal = await LockAnimalAsync(c, t, id, ct, true);
            if (animal.DeletedAt is null) return;
            var duplicate = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from agro360.livestock_animals where tenant_id=@TenantId and tag=@Tag and id<>@Id and deleted_at is null)",
                new { tenant.TenantId, animal.Tag, Id = id }, t, cancellationToken: ct));
            if (duplicate)
                throw new ConflictException(
                    "Não é possível restaurar pois já existe outro animal ativo com a mesma identificação.",
                    "livestock.restore_conflict");
            await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.livestock_animals
                set deleted_at=null, deleted_by=null, deletion_reason=null, updated_at=now(), updated_by=@UserId, version=version+1
                where tenant_id=@TenantId and id=@Id;
                insert into agro360.livestock_animal_events(id,tenant_id,animal_id,event_type,occurred_on,data,created_at,created_by)
                values(@Event,@TenantId,@Id,'RESTORE',current_date,jsonb_build_object('reason',@Reason),now(),@UserId)
                """,
                new { tenant.UserId, Reason = text, tenant.TenantId, Id = id, Event = Guid.CreateVersion7() },
                t, cancellationToken: ct));
            await Audit(c, t, "restore", "Animal", id, new { reason = text }, ct);
        }, ct);
    }

    public Task<IReadOnlyList<dynamic>> ListMovementsAsync(Guid? farmId, string? kind, DateOnly? from, DateOnly? until, CancellationToken ct) =>
        Tx<IReadOnlyList<dynamic>>(async (c, t) =>
        {
            var animal = await c.QueryAsync(new CommandDefinition(
                """
                select m.id, 'ANIMAL' as "subjectKind", a.tag as "subject", m.movement_kind as "kind",
                       m.reason_code as "reasonCode", m.moved_on as "occurredOn", m.notes,
                       m.from_farm_id as "fromFarmId", m.to_farm_id as "toFarmId"
                from agro360.livestock_animal_movements m
                join agro360.livestock_animals a on a.id=m.animal_id and a.tenant_id=m.tenant_id
                where m.tenant_id=@TenantId
                  and (@FarmId is null or m.from_farm_id=@FarmId or m.to_farm_id=@FarmId)
                  and (@Kind is null or m.movement_kind=@Kind)
                  and (@From is null or m.moved_on>=@From)
                  and (@To is null or m.moved_on<=@To)
                order by m.moved_on desc, m.created_at desc
                limit 500
                """,
                new { tenant.TenantId, FarmId = farmId ?? tenant.FarmId, Kind = kind, From = from, To = until }, t, cancellationToken: ct));
            var herd = await c.QueryAsync(new CommandDefinition(
                """
                select m.id, 'HERD' as "subjectKind", h.name as "subject", m.movement_kind as "kind",
                       m.reason_code as "reasonCode", m.occurred_on as "occurredOn", m.origin_notes as notes,
                       m.from_farm_id as "fromFarmId", m.to_farm_id as "toFarmId", m.quantity
                from agro360.livestock_herd_movements m
                join agro360.livestock_herds h on h.id=m.herd_id and h.tenant_id=m.tenant_id
                where m.tenant_id=@TenantId
                  and (@FarmId is null or m.from_farm_id=@FarmId or m.to_farm_id=@FarmId or h.farm_id=@FarmId)
                  and (@Kind is null or m.movement_kind=@Kind)
                  and (@From is null or m.occurred_on>=@From)
                  and (@To is null or m.occurred_on<=@To)
                order by m.occurred_on desc
                limit 500
                """,
                new { tenant.TenantId, FarmId = farmId ?? tenant.FarmId, Kind = kind, From = from, To = until }, t, cancellationToken: ct));
            return animal.Concat(herd).AsList();
        }, ct);

    public Task<Guid> RegisterMovementAsync(HerdMovementCommand command, CancellationToken ct)
    {
        var kind = Guard.Required(command.Kind, nameof(command.Kind), 20).ToUpperInvariant();
        var reason = Guard.Required(command.ReasonCode, nameof(command.ReasonCode), 40).ToUpperInvariant();
        Guard.Required(command.ResponsibleId, nameof(command.ResponsibleId));
        if (command.AnimalId is null && command.HerdId is null)
            throw new DomainException("Informe o animal identificado ou o grupo controlado por quantidade.", "livestock.subject_required");
        if (command.AnimalId is not null && command.HerdId is not null && command.Quantity is > 0)
            throw new DomainException("Não misture indivíduo e quantidade na mesma operação.", "livestock.mixed_subject");
        return Tx(async (c, t) =>
        {
            if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                var existing = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                    "select id from agro360.livestock_animal_movements where tenant_id=@TenantId and notes=@Key union all select id from agro360.livestock_herd_movements where tenant_id=@TenantId and idempotency_key=@Key limit 1",
                    new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
                if (existing is Guid found) return found;
            }

            if (command.AnimalId is Guid animalId)
                return await MoveAnimalAsync(c, t, animalId, kind, reason, command, ct);

            return await MoveHerdAsync(c, t, command.HerdId!.Value, kind, reason, command, ct);
        }, ct);
    }

    public Task<IReadOnlyList<dynamic>> ListHandlingOrdersAsync(string? status, Guid? farmId, CancellationToken ct) =>
        List("""
            select o.id, o.farm_id as "farmId", o.handling_type as "handlingType", o.planned_on as "plannedOn",
                   o.priority, o.status, o.planned_head_count as "plannedHeadCount",
                   o.attended_head_count as "attendedHeadCount", o.not_attended_head_count as "notAttendedHeadCount",
                   o.blocked_head_count as "blockedHeadCount", o.mass_atomic as "massAtomic",
                   u.name as "responsibleName", o.instructions
            from agro360.livestock_handling_orders o
            join agro360.identity_users u on u.id=o.responsible_id and u.tenant_id=o.tenant_id
            where o.tenant_id=@TenantId
              and (@FarmId is null or o.farm_id=@FarmId)
              and (@Status is null or o.status=@Status)
            order by o.planned_on desc
            """, ct, farmId ?? tenant.FarmId, status);

    public Task<dynamic?> HandlingOrderAsync(Guid id, CancellationToken ct) =>
        Tx<dynamic?>(async (c, t) =>
        {
            var order = await c.QuerySingleOrDefaultAsync(new CommandDefinition(
                """
                select o.*, u.name as "responsibleName"
                from agro360.livestock_handling_orders o
                join agro360.identity_users u on u.id=o.responsible_id and u.tenant_id=o.tenant_id
                where o.tenant_id=@TenantId and o.id=@Id
                """,
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
            if (order is null) return null;
            var items = await c.QueryAsync(new CommandDefinition(
                """
                select i.id, i.animal_id as "animalId", a.tag, i.outcome, i.outcome_reason as "outcomeReason", i.planned
                from agro360.livestock_handling_order_items i
                left join agro360.livestock_animals a on a.id=i.animal_id and a.tenant_id=i.tenant_id
                where i.tenant_id=@TenantId and i.order_id=@Id
                order by a.tag
                """,
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
            return (object)new { order, items };
        }, ct);

    public Task<Guid> CreateHandlingOrderAsync(HandlingOrderCommand command, CancellationToken ct)
    {
        var type = Guard.Required(command.HandlingType, nameof(command.HandlingType), 40).ToUpperInvariant();
        var priority = string.IsNullOrWhiteSpace(command.Priority) ? "MEDIUM" : command.Priority.ToUpperInvariant();
        if (command.AnimalIds.Count == 0 && command.HerdId is null)
            throw new DomainException("Selecione animais ou um grupo.", "livestock.handling_empty");
        return Tx(async (c, t) =>
        {
            await EnsureFarmAsync(c, t, command.FarmId, ct);
            var animals = new List<Guid>(command.AnimalIds);
            if (command.HerdId is Guid herdId)
            {
                var herd = await c.QuerySingleOrDefaultAsync<HerdRow>(new CommandDefinition(
                    "select id, control_mode as ControlMode, head_count as HeadCount, farm_id as FarmId from agro360.livestock_herds where tenant_id=@TenantId and id=@Id and deleted_at is null",
                    new { tenant.TenantId, Id = herdId }, t, cancellationToken: ct))
                    ?? throw new NotFoundException("Grupo", herdId);
                LivestockRules.EnsureControlMode(herd.ControlMode);
                if (string.Equals(herd.ControlMode, "INDIVIDUAL", StringComparison.OrdinalIgnoreCase))
                {
                    var members = await c.QueryAsync<Guid>(new CommandDefinition(
                        "select id from agro360.livestock_animals where tenant_id=@TenantId and herd_id=@Herd and status in (1,2,6) and deleted_at is null",
                        new { tenant.TenantId, Herd = herdId }, t, cancellationToken: ct));
                    animals.AddRange(members);
                }
            }

            animals = animals.Distinct().ToList();
            foreach (var animalId in animals)
            {
                var animal = await LockAnimalAsync(c, t, animalId, ct);
                LivestockRules.EnsureOnFarm(animal.Status, "incluir no manejo");
                if (tenant.FarmId is Guid scoped && animal.FarmId != scoped)
                    throw new ForbiddenException("Animal fora da propriedade autorizada.");
            }

            var id = Guid.CreateVersion7();
            var taskId = Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.livestock_handling_orders
                    (id,tenant_id,farm_id,handling_type,facility_id,planned_on,responsible_id,instructions,priority,status,
                     planned_head_count,mass_atomic,notes,created_by,work_task_id)
                values (@Id,@TenantId,@FarmId,@Type,@FacilityId,@PlannedOn,@ResponsibleId,@Instructions,@Priority,'DRAFT',
                        @Count,@Atomic,@Notes,@UserId,@TaskId);
                insert into agro360.operations_operational_tasks
                    (id,tenant_id,title,description,responsible_id,priority,due_at,module,entity_type,entity_id,status,created_by)
                values (@TaskId,@TenantId,'Manejo '||@Type,'Ordem de manejo pecuário',@ResponsibleId,@Priority,(@PlannedOn::timestamp at time zone 'UTC'),
                        'LIVESTOCK','HANDLING_ORDER',@Id,'OPEN',@UserId)
                """,
                new
                {
                    Id = id,
                    tenant.TenantId,
                    command.FarmId,
                    Type = type,
                    command.FacilityId,
                    command.PlannedOn,
                    command.ResponsibleId,
                    command.Instructions,
                    Priority = priority,
                    Count = animals.Count,
                    Atomic = command.MassAtomic,
                    Notes = command.Instructions,
                    tenant.UserId,
                    TaskId = taskId
                }, t, cancellationToken: ct));
            foreach (var animalId in animals)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    """
                    insert into agro360.livestock_handling_order_items(id,tenant_id,order_id,animal_id,planned,outcome)
                    values (@Item,@TenantId,@Order,@Animal,true,'PLANNED')
                    """,
                    new { Item = Guid.CreateVersion7(), tenant.TenantId, Order = id, Animal = animalId }, t, cancellationToken: ct));
            }
            if (command.Materials is { Count: > 0 })
            {
                foreach (var material in command.Materials)
                {
                    LivestockRules.Positive(material.PlannedQuantity, "Quantidade planejada");
                    await c.ExecuteAsync(new CommandDefinition(
                        """
                        insert into agro360.livestock_handling_order_materials
                            (id,tenant_id,order_id,product_id,warehouse_id,planned_quantity,unit)
                        values (@Id,@TenantId,@Order,@ProductId,@WarehouseId,@PlannedQuantity,@Unit)
                        """,
                        new { Id = Guid.CreateVersion7(), tenant.TenantId, Order = id, material.ProductId, material.WarehouseId, material.PlannedQuantity, material.Unit },
                        t, cancellationToken: ct));
                }
            }
            await Audit(c, t, "create", "HandlingOrder", id, command, ct);
            return id;
        }, ct);
    }

    public Task TransitionHandlingOrderAsync(Guid id, HandlingOrderTransitionCommand command, CancellationToken ct)
    {
        var to = Guard.Required(command.Status, nameof(command.Status), 20).ToUpperInvariant();
        return Tx(async (c, t) =>
        {
            var order = await c.QuerySingleOrDefaultAsync<HandlingOrderRow>(new CommandDefinition(
                "select id, status, planned_head_count as PlannedHeadCount, attended_head_count as AttendedHeadCount, not_attended_head_count as NotAttendedHeadCount, blocked_head_count as BlockedHeadCount, work_task_id as WorkTaskId from agro360.livestock_handling_orders where tenant_id=@TenantId and id=@Id for update",
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct))
                ?? throw new NotFoundException("Ordem de manejo", id);
            LivestockRules.EnsureHandlingTransition(order.Status, to);
            if (to == "COMPLETED")
            {
                var pending = await c.ExecuteScalarAsync<int>(new CommandDefinition(
                    "select count(*) from agro360.livestock_handling_order_items where tenant_id=@TenantId and order_id=@Id and outcome='PLANNED'",
                    new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
                LivestockRules.EnsureCompletionCounts(order.PlannedHeadCount, order.AttendedHeadCount, order.NotAttendedHeadCount, order.BlockedHeadCount, pending);
            }
            if (to == "CANCELLED" && string.IsNullOrWhiteSpace(command.Reason))
                throw new DomainException("Informe o motivo do cancelamento. Consumo já realizado é preservado.", "livestock.cancel_reason");
            await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.livestock_handling_orders
                set status=@Status, notes=coalesce(@Reason,notes), updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Id;
                update agro360.operations_operational_tasks
                set status=case @Status when 'IN_PROGRESS' then 'IN_PROGRESS' when 'COMPLETED' then 'COMPLETED' when 'CANCELLED' then 'CANCELLED' else status end,
                    updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Task
                """,
                new { Status = to, command.Reason, tenant.TenantId, tenant.UserId, Id = id, Task = order.WorkTaskId },
                t, cancellationToken: ct));
            await Audit(c, t, to.ToLowerInvariant(), "HandlingOrder", id, command, ct);
        }, ct);
    }

    public Task<IReadOnlyList<dynamic>> PreviewHandlingAsync(Guid id, CancellationToken ct) =>
        Tx<IReadOnlyList<dynamic>>(async (c, t) =>
        {
            var rows = await c.QueryAsync(new CommandDefinition(
                """
                select i.animal_id as "animalId", a.tag, i.outcome,
                       case when a.status in (3,4,5) then 'Animal já saiu do rebanho'
                            when exists(select 1 from agro360.livestock_restrictions r
                                        where r.tenant_id=a.tenant_id and r.animal_id=a.id and r.status='ACTIVE')
                                 then 'Há restrição operacional ativa'
                            else 'Elegível' end as "eligibility"
                from agro360.livestock_handling_order_items i
                join agro360.livestock_animals a on a.id=i.animal_id and a.tenant_id=i.tenant_id
                where i.tenant_id=@TenantId and i.order_id=@Id
                order by a.tag
                """,
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
            return rows.AsList();
        }, ct);

    public Task<IReadOnlyList<dynamic>> ExecuteHandlingAsync(Guid id, HandlingExecutionCommand command, CancellationToken ct)
    {
        if (command.Items.Count == 0)
            throw new DomainException("Informe o resultado por animal.", "livestock.handling_items_required");
        return Tx(async (c, t) =>
        {
            var order = await c.QuerySingleOrDefaultAsync<HandlingOrderRow>(new CommandDefinition(
                "select id, status, planned_head_count as PlannedHeadCount, attended_head_count as AttendedHeadCount, not_attended_head_count as NotAttendedHeadCount, blocked_head_count as BlockedHeadCount, mass_atomic as MassAtomic, work_task_id as WorkTaskId from agro360.livestock_handling_orders where tenant_id=@TenantId and id=@Id for update",
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct))
                ?? throw new NotFoundException("Ordem de manejo", id);
            if (order.Status is not ("RELEASED" or "IN_PROGRESS" or "PAUSED"))
                throw new ConflictException("A ordem precisa estar liberada ou em execução.", "livestock.handling_not_executable");
            var results = new List<dynamic>();
            foreach (var item in command.Items)
            {
                var outcome = item.Outcome.ToUpperInvariant();
                if (!LivestockRules.HandlingOutcomes.Contains(outcome))
                    throw new DomainException("Resultado de atendimento inválido.", "livestock.handling_outcome_invalid");
                try
                {
                    var animal = await LockAnimalAsync(c, t, item.AnimalId, ct);
                    if (outcome == "ATTENDED")
                        LivestockRules.EnsureOnFarm(animal.Status, "atender");
                    if (outcome == "ATTENDED")
                    {
                        var blocked = await HasActiveRestrictionAsync(c, t, item.AnimalId, null, ct);
                        if (blocked)
                            throw new ConflictException("Restrição operacional impede o atendimento.", "livestock.restriction_active");
                    }
                    var updated = await c.ExecuteAsync(new CommandDefinition(
                        """
                        update agro360.livestock_handling_order_items
                        set outcome=@Outcome, outcome_reason=@Reason
                        where tenant_id=@TenantId and order_id=@Order and animal_id=@Animal
                        """,
                        new { Outcome = outcome, item.Reason, tenant.TenantId, Order = id, Animal = item.AnimalId },
                        t, cancellationToken: ct));
                    if (updated == 0)
                        throw new NotFoundException("Item da ordem", item.AnimalId);
                    results.Add(new { animalId = item.AnimalId, outcome, ok = true, detail = "Registrado" });
                }
                catch (Exception ex) when (ex is DomainException or ConflictException or NotFoundException)
                {
                    if (order.MassAtomic) throw;
                    results.Add(new { animalId = item.AnimalId, outcome = "FAILED", ok = false, detail = ex.Message });
                }
            }

            var counts = await c.QuerySingleAsync<HandlingOrderRow>(new CommandDefinition(
                """
                select count(*) filter(where outcome='ATTENDED') as AttendedHeadCount,
                       count(*) filter(where outcome='NOT_ATTENDED') as NotAttendedHeadCount,
                       count(*) filter(where outcome='BLOCKED') as BlockedHeadCount,
                       count(*) as PlannedHeadCount
                from agro360.livestock_handling_order_items
                where tenant_id=@TenantId and order_id=@Id
                """,
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
            var next = command.CompleteOrder ? "COMPLETED" : "IN_PROGRESS";
            if (command.CompleteOrder)
            {
                var pending = await c.ExecuteScalarAsync<int>(new CommandDefinition(
                    "select count(*) from agro360.livestock_handling_order_items where tenant_id=@TenantId and order_id=@Id and outcome in ('PLANNED','FAILED')",
                    new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
                LivestockRules.EnsureCompletionCounts(counts.PlannedHeadCount, counts.AttendedHeadCount, counts.NotAttendedHeadCount, counts.BlockedHeadCount, pending);
            }
            await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.livestock_handling_orders
                set status=@Status, attended_head_count=@Attended, not_attended_head_count=@NotAttended,
                    blocked_head_count=@Blocked, notes=coalesce(@Notes,notes), updated_at=now(), updated_by=@UserId
                where tenant_id=@TenantId and id=@Id
                """,
                new
                {
                    Status = next,
                    Attended = counts.AttendedHeadCount,
                    NotAttended = counts.NotAttendedHeadCount,
                    Blocked = counts.BlockedHeadCount,
                    command.Notes,
                    tenant.TenantId,
                    tenant.UserId,
                    Id = id
                }, t, cancellationToken: ct));
            await Audit(c, t, "execute", "HandlingOrder", id, command, ct);
            return (IReadOnlyList<dynamic>)results;
        }, ct);
    }

    public Task<IReadOnlyList<dynamic>> ListWeighingsAsync(Guid? animalId, Guid? herdId, DateOnly? from, DateOnly? until, CancellationToken ct) =>
        List("""
            select id, scope, animal_id as "animalId", herd_id as "herdId", weighed_at as "weighedAt",
                   weight_kg as "weightKg", unit, head_count as "headCount", method, source,
                   review_required as "reviewRequired", notes
            from agro360.livestock_weighings
            where tenant_id=@TenantId
              and (@AnimalId is null or animal_id=@AnimalId)
              and (@HerdId is null or herd_id=@HerdId)
              and (@From is null or weighed_at::date>=@From)
              and (@To is null or weighed_at::date<=@To)
            order by weighed_at desc
            limit 500
            """, ct, extra: new { AnimalId = animalId, HerdId = herdId, From = from, To = until });

    public Task<Guid> RecordWeighingAsync(WeighingCommand command, CancellationToken ct)
    {
        var scope = Guard.Required(command.Scope, nameof(command.Scope), 16).ToUpperInvariant();
        var unit = string.IsNullOrWhiteSpace(command.Unit) ? "kg" : command.Unit.Trim().ToLowerInvariant();
        var source = string.IsNullOrWhiteSpace(command.Source) ? "MANUAL" : command.Source.ToUpperInvariant();
        var weight = LivestockRules.ConvertWeight(command.Weight, unit, "kg", command.ConversionFactor);
        if (scope == "INDIVIDUAL" && command.AnimalId is null)
            throw new DomainException("Pesagem individual exige animal identificado.", "livestock.weighing_animal");
        if (scope == "COLLECTIVE" && command.AnimalId is not null)
            throw new DomainException("Pesagem coletiva não gera pesos individuais fictícios.", "livestock.collective_no_split");
        if (scope == "COLLECTIVE" && command.HerdId is null && command.HandlingLotId is null)
            throw new DomainException("Pesagem coletiva exige grupo ou lote de manejo.", "livestock.collective_subject");
        return Tx(async (c, t) =>
        {
            if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                var replay = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                    "select id from agro360.livestock_weighings where tenant_id=@TenantId and idempotency_key=@Key",
                    new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
                if (replay is Guid found) return found;
            }

            var review = false;
            string? species = "BOVINE";
            if (command.AnimalId is Guid animalId)
            {
                var animal = await LockAnimalAsync(c, t, animalId, ct);
                LivestockRules.EnsureOnFarm(animal.Status, "pesar");
                LivestockRules.EnsureEventNotBeforeBirth(DateOnly.FromDateTime(command.WeighedAt.UtcDateTime), animal.BirthDate, animal.BirthDateEstimated);
                species = animal.Species;
            }
            var limits = await c.QuerySingleOrDefaultAsync<WeightLimitRow>(new CommandDefinition(
                "select min_kg as MinKg, max_kg as MaxKg from agro360.livestock_weight_limits where tenant_id=@TenantId and species_code=@Species",
                new { tenant.TenantId, Species = species }, t, cancellationToken: ct));
            if (limits is not null && (weight < limits.MinKg || weight > limits.MaxKg))
            {
                if (string.IsNullOrWhiteSpace(command.ReviewJustification))
                    throw new DomainException("Peso fora do limite configurado exige justificativa para manutenção.", "livestock.weight_review");
                review = true;
            }

            var id = Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.livestock_weighings
                    (id,tenant_id,farm_id,scope,animal_id,herd_id,handling_lot_id,weighed_at,weight_kg,unit,
                     converted_from_unit,conversion_factor,head_count,method,equipment,source,responsible_id,
                     notes,review_required,review_justification,idempotency_key,created_by)
                values (@Id,@TenantId,@FarmId,@Scope,@AnimalId,@HerdId,@HandlingLotId,@WeighedAt,@Weight,'kg',
                        @FromUnit,@Factor,@HeadCount,@Method,@Equipment,@Source,@ResponsibleId,
                        @Notes,@Review,@Justification,@IdempotencyKey,@UserId)
                """,
                new
                {
                    Id = id,
                    tenant.TenantId,
                    command.FarmId,
                    Scope = scope,
                    command.AnimalId,
                    command.HerdId,
                    command.HandlingLotId,
                    command.WeighedAt,
                    Weight = weight,
                    FromUnit = unit,
                    Factor = command.ConversionFactor,
                    command.HeadCount,
                    command.Method,
                    command.Equipment,
                    Source = source,
                    command.ResponsibleId,
                    command.Notes,
                    Review = review,
                    Justification = command.ReviewJustification,
                    command.IdempotencyKey,
                    tenant.UserId
                }, t, cancellationToken: ct));

            if (scope == "INDIVIDUAL" && command.AnimalId is Guid weighed)
            {
                decimal? gain = null;
                var previous = await c.QuerySingleOrDefaultAsync<PreviousWeighingRow>(new CommandDefinition(
                    """
                    select weight_kg as WeightKg, weighed_at as WeighedAt from agro360.livestock_weighings
                    where tenant_id=@TenantId and animal_id=@Animal and id<>@Id
                    order by weighed_at desc limit 1
                    """,
                    new { tenant.TenantId, Animal = weighed, Id = id }, t, cancellationToken: ct));
                if (previous is not null)
                {
                    var days = (command.WeighedAt - previous.WeighedAt).TotalDays;
                    if (days > 0)
                        gain = decimal.Round((weight - previous.WeightKg) / (decimal)days, 4);
                }
                await c.ExecuteAsync(new CommandDefinition(
                    """
                    update agro360.livestock_animals
                    set current_weight_kg=@Weight, last_weight_date=@On, updated_at=now(), updated_by=@UserId, version=version+1
                    where tenant_id=@TenantId and id=@Animal;
                    insert into agro360.livestock_animal_events(id,tenant_id,animal_id,event_type,occurred_on,data,idempotency_key,created_at,created_by)
                    values (@Event,@TenantId,@Animal,'WEIGHING',@On,jsonb_build_object('weightKg',@Weight,'dailyGainKg',@Gain,'source',@Source),@IdempotencyKey,now(),@UserId)
                    """,
                    new
                    {
                        Weight = weight,
                        On = DateOnly.FromDateTime(command.WeighedAt.UtcDateTime),
                        tenant.UserId,
                        tenant.TenantId,
                        Animal = weighed,
                        Event = Guid.CreateVersion7(),
                        Gain = gain,
                        Source = source,
                        command.IdempotencyKey
                    }, t, cancellationToken: ct));
            }
            await Audit(c, t, "weigh", "Weighing", id, command, ct);
            return id;
        }, ct);
    }

    public Task CorrectWeighingAsync(Guid id, WeighingCorrectionCommand command, CancellationToken ct)
    {
        var justification = Guard.Required(command.Justification, nameof(command.Justification), 240);
        var unit = string.IsNullOrWhiteSpace(command.Unit) ? "kg" : command.Unit.Trim().ToLowerInvariant();
        var weight = LivestockRules.ConvertWeight(command.WeightKg, unit, "kg", command.ConversionFactor);
        return Tx(async (c, t) =>
        {
            var current = await c.QuerySingleOrDefaultAsync<WeighingRow>(new CommandDefinition(
                "select id, animal_id as AnimalId, weight_kg as WeightKg, scope from agro360.livestock_weighings where tenant_id=@TenantId and id=@Id for update",
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct))
                ?? throw new NotFoundException("Pesagem", id);
            var correctionId = Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.livestock_weighings
                    (id,tenant_id,farm_id,scope,animal_id,weighed_at,weight_kg,unit,source,responsible_id,notes,
                     previous_weight_kg,corrected_from_id,created_by)
                select @Correction, tenant_id, farm_id, scope, animal_id, weighed_at, @Weight, 'kg', 'MANUAL', @Responsible,
                       @Justification, weight_kg, id, @UserId
                from agro360.livestock_weighings where tenant_id=@TenantId and id=@Id;
                update agro360.livestock_weighings
                set notes=coalesce(notes,'') || ' [substituída por correção auditada]'
                where tenant_id=@TenantId and id=@Id
                """,
                new
                {
                    Correction = correctionId,
                    Weight = weight,
                    Responsible = command.ResponsibleId ?? tenant.UserId,
                    Justification = justification,
                    tenant.UserId,
                    tenant.TenantId,
                    Id = id
                }, t, cancellationToken: ct));
            if (current.Scope == "INDIVIDUAL" && current.AnimalId is Guid animalId)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    "update agro360.livestock_animals set current_weight_kg=@Weight, updated_at=now(), updated_by=@UserId, version=version+1 where tenant_id=@TenantId and id=@Animal",
                    new { Weight = weight, tenant.UserId, tenant.TenantId, Animal = animalId }, t, cancellationToken: ct));
            }
            await Audit(c, t, "correct", "Weighing", id, new { previous = current.WeightKg, current = weight, justification }, ct);
        }, ct);
    }

    public Task<IReadOnlyList<dynamic>> PerformanceAsync(Guid? animalId, Guid? herdId, DateOnly? from, DateOnly? until, CancellationToken ct) =>
        Tx<IReadOnlyList<dynamic>>(async (c, t) =>
        {
            var rows = await c.QueryAsync(new CommandDefinition(
                """
                select animal_id as "animalId",
                       min(weighed_at) as "periodStart",
                       max(weighed_at) as "periodEnd",
                       count(*)::int as "measurements",
                       (array_agg(weight_kg order by weighed_at))[1] as "initialWeightKg",
                       (array_agg(weight_kg order by weighed_at desc))[1] as "finalWeightKg",
                       case when extract(epoch from max(weighed_at)-min(weighed_at))/86400 > 0
                            then round(((array_agg(weight_kg order by weighed_at desc))[1]
                                 - (array_agg(weight_kg order by weighed_at))[1])
                                 / (extract(epoch from max(weighed_at)-min(weighed_at))/86400), 4)
                            else null end as "averageDailyGainKg",
                       'Pesagens individuais comparáveis, intervalo positivo, ordenadas pela data da pesagem' as criteria
                from agro360.livestock_weighings
                where tenant_id=@TenantId and scope='INDIVIDUAL' and animal_id is not null
                  and (@AnimalId is null or animal_id=@AnimalId)
                  and (@From is null or weighed_at::date>=@From)
                  and (@To is null or weighed_at::date<=@To)
                group by animal_id
                having count(*)>1
                """,
                new { tenant.TenantId, AnimalId = animalId, From = from, To = until }, t, cancellationToken: ct));
            if (herdId is not null)
            {
                return rows.Select(r => (dynamic)new { weighing = r, compositionChangeWarning = "Médias de grupo exigem composição estável; a composição atual pode diferir do período." }).AsList();
            }
            return rows.AsList();
        }, ct);

    public Task<IReadOnlyList<dynamic>> ListRestrictionsAsync(Guid? animalId, string? purpose, bool onlyActive, CancellationToken ct) =>
        List("""
            select r.id, r.animal_id as "animalId", a.tag, r.purpose, r.reason, r.started_on as "startedOn",
                   r.expected_until as "expectedUntil", r.status, r.demo_only as "demoOnly",
                   r.release_authority as "releaseAuthority"
            from agro360.livestock_restrictions r
            left join agro360.livestock_animals a on a.id=r.animal_id and a.tenant_id=r.tenant_id
            where r.tenant_id=@TenantId
              and (@AnimalId is null or r.animal_id=@AnimalId)
              and (@Purpose is null or r.purpose=@Purpose)
              and (@OnlyActive=false or r.status='ACTIVE')
            order by r.started_on desc
            """, ct, extra: new { AnimalId = animalId, Purpose = purpose, OnlyActive = onlyActive });

    public Task ReleaseRestrictionAsync(Guid id, RestrictionReleaseCommand command, CancellationToken ct)
    {
        var authority = Guard.Required(command.Authority, nameof(command.Authority), 120);
        var criteria = Guard.Required(command.Criteria, nameof(command.Criteria), 240);
        return Tx(async (c, t) =>
        {
            var n = await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.livestock_restrictions
                set status='RELEASED', released_on=@On, release_authority=@Authority, release_criteria=@Criteria
                where tenant_id=@TenantId and id=@Id and status='ACTIVE'
                """,
                new { On = command.ReleasedOn, Authority = authority, Criteria = criteria, tenant.TenantId, Id = id },
                t, cancellationToken: ct));
            if (n == 0)
                throw new ConflictException("Restrição inexistente ou já encerrada. Permissão administrativa não a elimina automaticamente.", "livestock.restriction_not_active");
            await Audit(c, t, "release", "Restriction", id, command, ct);
        }, ct);
    }

    public Task ReturnFeedingAsync(Guid id, FeedingReturnCommand command, CancellationToken ct)
    {
        LivestockRules.NonNegative(command.ReturnedQuantity, "Devolução");
        LivestockRules.NonNegative(command.LostQuantity, "Perda");
        return Tx(async (c, t) =>
        {
            var feeding = await c.QuerySingleOrDefaultAsync<FeedingRow>(new CommandDefinition(
                """
                select id, warehouse_id as WarehouseId, product_id as ProductId, lot_number as LotNumber, unit,
                       supplied_quantity as SuppliedQuantity, returned_quantity as ReturnedQuantity,
                       lost_quantity as LostQuantity, consumed_quantity as ConsumedQuantity, plan_id as PlanId
                from agro360.livestock_feedings
                where tenant_id=@TenantId and id=@Id for update
                """,
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct))
                ?? throw new NotFoundException("Fornecimento", id);
            var supplied = feeding.SuppliedQuantity ?? 0;
            var returned = feeding.ReturnedQuantity + command.ReturnedQuantity;
            var lost = feeding.LostQuantity + command.LostQuantity;
            if (returned + lost > supplied)
                throw new DomainException("Devolução e perda não podem exceder o fornecido.", "livestock.feeding_return_excess");
            var consumed = supplied - returned - lost;
            if (command.ReturnedQuantity > 0 && command.Reusable)
            {
                if (feeding is { WarehouseId: Guid warehouse, ProductId: Guid product })
                    await AdjustStockAsync(c, t, warehouse, product, feeding.Unit ?? "kg", command.ReturnedQuantity, feeding.LotNumber, id, "FEEDING_RETURN", ct);
            }
            await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.livestock_feedings
                set returned_quantity=@Returned, lost_quantity=@Lost, consumed_quantity=@Consumed,
                    return_reusable=@Reusable, notes=coalesce(notes,'') || coalesce(' '||@Notes,'')
                where tenant_id=@TenantId and id=@Id
                """,
                new
                {
                    Returned = returned,
                    Lost = lost,
                    Consumed = consumed,
                    command.Reusable,
                    command.Notes,
                    tenant.TenantId,
                    Id = id
                }, t, cancellationToken: ct));
            await Audit(c, t, "feeding-return", "Feeding", id, command, ct);
        }, ct);
    }

    public Task<IReadOnlyList<dynamic>> EligibleForSaleAsync(Guid farmId, string? search, CancellationToken ct) =>
        List("""
            select a.id, a.tag, a.species, a.category, a.current_weight_kg as "currentWeightKg",
                   case a.status when 1 then 'ACTIVE' when 2 then 'QUARANTINE' when 6 then 'RESERVED' end as status,
                   exists(select 1 from agro360.livestock_restrictions r
                          where r.tenant_id=a.tenant_id and r.animal_id=a.id and r.status='ACTIVE'
                            and r.purpose in ('SALE','SLAUGHTER')) as "restricted"
            from agro360.livestock_animals a
            where a.tenant_id=@TenantId and a.farm_id=@FarmId and a.deleted_at is null
              and a.status=1 and a.inactivated_at is null
              and (@Search='' or a.tag ilike '%'||@Search||'%')
              and not exists(select 1 from agro360.livestock_sale_reservations r
                             where r.tenant_id=a.tenant_id and r.animal_id=a.id and r.status='ACTIVE')
            order by a.tag
            """, ct, farmId, extra: new { Search = search?.Trim() ?? "" });

    public Task<Guid> ReserveAsync(SaleReservationCommand command, CancellationToken ct)
    {
        LivestockRules.EnsurePositiveQuantity(command.Quantity, "Quantidade");
        if (command.AnimalId is null && command.HerdId is null)
            throw new DomainException("Selecione animal ou quantidade de um grupo.", "livestock.subject_required");
        return Tx(async (c, t) =>
        {
            if (!string.IsNullOrWhiteSpace(command.IdempotencyKey))
            {
                var replay = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                    "select id from agro360.livestock_sale_reservations where tenant_id=@TenantId and idempotency_key=@Key",
                    new { tenant.TenantId, Key = command.IdempotencyKey }, t, cancellationToken: ct));
                if (replay is Guid found) return found;
            }
            if (command.AnimalId is Guid animalId)
            {
                var animal = await LockAnimalAsync(c, t, animalId, ct);
                if (animal.Status != 1)
                    throw new ConflictException("Somente animal ativo e sem reserva pode ser reservado.", "livestock.reserve_status");
                if (await HasActiveRestrictionAsync(c, t, animalId, "SALE", ct))
                    throw new ConflictException("Restrição impede a reserva comercial.", "livestock.restriction_active");
                if (animal.WithdrawalUntil is DateOnly until && command.ReservedOn <= until)
                    throw new ConflictException($"Carência sanitária até {until:dd/MM/yyyy}.", "livestock.withdrawal_period_active");
            }
            if (command.HerdId is Guid herdId)
            {
                var herd = await c.QuerySingleOrDefaultAsync<HerdRow>(new CommandDefinition(
                    "select id, control_mode as ControlMode, head_count as HeadCount, farm_id as FarmId from agro360.livestock_herds where tenant_id=@TenantId and id=@Id for update",
                    new { tenant.TenantId, Id = herdId }, t, cancellationToken: ct))
                    ?? throw new NotFoundException("Grupo", herdId);
                LivestockRules.PreventDoubleCount(herd.ControlMode, command.AnimalId is not null);
                if (string.Equals(herd.ControlMode, "QUANTITY", StringComparison.OrdinalIgnoreCase) && command.Quantity > herd.HeadCount)
                    throw new ConflictException("Quantidade reservada excede o saldo do grupo.", "livestock.negative_balance");
            }
            var id = Guid.CreateVersion7();
            try
            {
                await c.ExecuteAsync(new CommandDefinition(
                    """
                    insert into agro360.livestock_sale_reservations
                        (id,tenant_id,farm_id,animal_id,herd_id,quantity,status,reserved_on,buyer_name,notes,idempotency_key,created_by)
                    values (@Id,@TenantId,@FarmId,@AnimalId,@HerdId,@Quantity,'ACTIVE',@ReservedOn,@BuyerName,@Notes,@IdempotencyKey,@UserId)
                    """,
                    new
                    {
                        Id = id,
                        tenant.TenantId,
                        command.FarmId,
                        command.AnimalId,
                        command.HerdId,
                        command.Quantity,
                        command.ReservedOn,
                        command.BuyerName,
                        command.Notes,
                        command.IdempotencyKey,
                        tenant.UserId
                    }, t, cancellationToken: ct));
            }
            catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                throw new ConflictException("Já existe reserva vigente para este animal.", "livestock.reservation_conflict");
            }
            if (command.AnimalId is Guid reserved)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    "update agro360.livestock_animals set status=6, reservation_id=@Reservation, updated_at=now(), updated_by=@UserId, version=version+1 where tenant_id=@TenantId and id=@Id and status=1",
                    new { Reservation = id, tenant.UserId, tenant.TenantId, Id = reserved }, t, cancellationToken: ct));
            }
            if (command.HerdId is Guid qtyHerd && command.AnimalId is null)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    "update agro360.livestock_herds set head_count=head_count-@Quantity, updated_at=now(), updated_by=@UserId, version=version+1 where tenant_id=@TenantId and id=@Id and head_count>=@Quantity",
                    new { command.Quantity, tenant.UserId, tenant.TenantId, Id = qtyHerd }, t, cancellationToken: ct));
            }
            await Audit(c, t, "reserve", "SaleReservation", id, command, ct);
            return id;
        }, ct);
    }

    public Task CancelReservationAsync(Guid id, string reason, CancellationToken ct)
    {
        var text = Guard.Required(reason, nameof(reason), 240);
        return Tx(async (c, t) =>
        {
            var reservation = await c.QuerySingleOrDefaultAsync<ReservationRow>(new CommandDefinition(
                "select id, animal_id as AnimalId, herd_id as HerdId, quantity, status from agro360.livestock_sale_reservations where tenant_id=@TenantId and id=@Id for update",
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct))
                ?? throw new NotFoundException("Reserva", id);
            if (reservation.Status != "ACTIVE")
                throw new ConflictException("Somente reserva vigente pode ser cancelada. Saída física já executada exige retorno ou ajuste.", "livestock.reservation_not_active");
            await c.ExecuteAsync(new CommandDefinition(
                """
                update agro360.livestock_sale_reservations
                set status='CANCELLED', cancelled_at=now(), cancelled_by=@UserId, notes=coalesce(notes,'') || ' '||@Reason
                where tenant_id=@TenantId and id=@Id
                """,
                new { tenant.UserId, Reason = text, tenant.TenantId, Id = id }, t, cancellationToken: ct));
            if (reservation.AnimalId is Guid animalId)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    "update agro360.livestock_animals set status=1, reservation_id=null, updated_at=now(), updated_by=@UserId, version=version+1 where tenant_id=@TenantId and id=@Animal and status=6",
                    new { tenant.UserId, tenant.TenantId, Animal = animalId }, t, cancellationToken: ct));
            }
            if (reservation.HerdId is Guid herdId && reservation.AnimalId is null)
            {
                await c.ExecuteAsync(new CommandDefinition(
                    "update agro360.livestock_herds set head_count=head_count+@Quantity, updated_at=now(), updated_by=@UserId, version=version+1 where tenant_id=@TenantId and id=@Herd",
                    new { reservation.Quantity, tenant.UserId, tenant.TenantId, Herd = herdId }, t, cancellationToken: ct));
            }
            await Audit(c, t, "cancel-reservation", "SaleReservation", id, new { reason = text }, ct);
        }, ct);
    }

    public Task ConfirmPhysicalExitAsync(Guid id, PhysicalExitCommand command, CancellationToken ct) =>
        Tx(async (c, t) =>
        {
            var reservation = await c.QuerySingleOrDefaultAsync<ReservationRow>(new CommandDefinition(
                "select id, farm_id as FarmId, animal_id as AnimalId, herd_id as HerdId, quantity, status from agro360.livestock_sale_reservations where tenant_id=@TenantId and id=@Id for update",
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct))
                ?? throw new NotFoundException("Reserva", id);
            if (reservation.Status != "ACTIVE")
                throw new ConflictException("A reserva não está vigente.", "livestock.reservation_not_active");
            if (reservation.AnimalId is Guid animalId)
            {
                var animal = await LockAnimalAsync(c, t, animalId, ct);
                if (await HasActiveRestrictionAsync(c, t, animalId, "SALE", ct))
                    throw new ConflictException("Elegibilidade revalidada: restrição impede a saída física.", "livestock.restriction_active");
                if (animal.WithdrawalUntil is DateOnly until && command.OccurredOn <= until)
                    throw new ConflictException($"Elegibilidade revalidada: carência até {until:dd/MM/yyyy}.", "livestock.withdrawal_period_active");
                await c.ExecuteAsync(new CommandDefinition(
                    """
                    update agro360.livestock_animals
                    set status=3, updated_at=now(), updated_by=@UserId, version=version+1
                    where tenant_id=@TenantId and id=@Animal and status=6;
                    insert into agro360.livestock_animal_movements
                        (id,tenant_id,animal_id,from_farm_id,to_farm_id,moved_on,notes,movement_kind,reason_code,responsible_id,created_by)
                    values (@Movement,@TenantId,@Animal,@Farm,@Farm,@On,@Notes,'EXIT','SALE',@Responsible,@UserId);
                    insert into agro360.livestock_animal_events(id,tenant_id,animal_id,event_type,occurred_on,data,created_at,created_by)
                    values (@Event,@TenantId,@Animal,'SALE',@On,jsonb_build_object('reservationId',@Id,'physicalOnly',true),now(),@UserId)
                    """,
                    new
                    {
                        tenant.UserId,
                        tenant.TenantId,
                        Animal = animalId,
                        Movement = Guid.CreateVersion7(),
                        Farm = reservation.FarmId,
                        On = command.OccurredOn,
                        command.Notes,
                        Responsible = command.ResponsibleId,
                        Event = Guid.CreateVersion7(),
                        Id = id
                    }, t, cancellationToken: ct));
            }
            await c.ExecuteAsync(new CommandDefinition(
                "update agro360.livestock_sale_reservations set status='EXIT_CONFIRMED' where tenant_id=@TenantId and id=@Id",
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
            await Audit(c, t, "physical-exit", "SaleReservation", id, command, ct);
        }, ct);

    public Task ConfirmFinancialAsync(Guid id, FinancialConfirmCommand command, CancellationToken ct)
    {
        var buyer = Guard.Required(command.BuyerName, nameof(command.BuyerName), 160);
        var price = LivestockRules.Positive(command.UnitPrice, "Preço");
        return Tx(async (c, t) =>
        {
            var reservation = await c.QuerySingleOrDefaultAsync<ReservationRow>(new CommandDefinition(
                "select id, farm_id as FarmId, animal_id as AnimalId, herd_id as HerdId, quantity, status, sale_id as SaleId from agro360.livestock_sale_reservations where tenant_id=@TenantId and id=@Id for update",
                new { tenant.TenantId, Id = id }, t, cancellationToken: ct))
                ?? throw new NotFoundException("Reserva", id);
            if (reservation.Status is not ("EXIT_CONFIRMED" or "FINANCIAL_PENDING"))
                throw new ConflictException("Confirme a saída física antes da obrigação financeira. Reserva não é venda e venda não é pagamento.", "livestock.financial_not_ready");
            if (reservation.SaleId is not null)
                throw new ConflictException("Obrigação financeira já registrada. Não duplicar contas a receber.", "livestock.financial_duplicate");
            var saleId = Guid.CreateVersion7();
            var receivableId = Guid.CreateVersion7();
            var origin = reservation.AnimalId ?? reservation.HerdId ?? Guid.Empty;
            var total = decimal.Round(price * reservation.Quantity, 2, MidpointRounding.AwayFromZero);
            await c.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.commercial_sales
                    (id, tenant_id, farm_id, product_type, origin_id, quantity, unit, unit_price, total_amount,
                     currency, buyer_name, buyer_document, due_date, status, confirmed_at, created_at, created_by, version)
                values
                    (@SaleId,@TenantId,@FarmId,'ANIMAL',@Origin,@Quantity,'head',@Price,@Total,
                     'BRL',@Buyer,@Document,@Due,'CONFIRMED',now(),now(),@UserId,1);
                insert into agro360.finance_commercial_receivables
                    (id, tenant_id, farm_id, sale_id, description, amount, currency, due_date, status, created_at, created_by, version)
                values
                    (@ReceivableId,@TenantId,@FarmId,@SaleId,'Venda pecuária para '||@Buyer,@Total,'BRL',@Due,'OPEN',now(),@UserId,1);
                update agro360.livestock_sale_reservations
                set status='FINANCIAL_CONFIRMED', sale_id=@SaleId, receivable_id=@ReceivableId
                where tenant_id=@TenantId and id=@Id
                """,
                new
                {
                    SaleId = saleId,
                    tenant.TenantId,
                    reservation.FarmId,
                    Origin = origin,
                    reservation.Quantity,
                    Price = price,
                    Total = total,
                    Buyer = buyer,
                    Document = command.BuyerDocument,
                    Due = command.DueDate,
                    tenant.UserId,
                    ReceivableId = receivableId,
                    Id = id
                }, t, cancellationToken: ct));
            await Audit(c, t, "financial-confirm", "SaleReservation", id, command, ct);
        }, ct);
    }

    public Task<IReadOnlyList<dynamic>> CostSummaryAsync(Guid? farmId, Guid? herdId, Guid? animalId, DateOnly? from, DateOnly? until, CancellationToken ct) =>
        List("""
            select category, nature, sum(amount) as amount, count(*) as "entries",
                   min(occurred_on) as "periodStart", max(occurred_on) as "periodEnd",
                   string_agg(distinct coalesce(basis,'sem base informada'), ' · ') as basis
            from agro360.livestock_cost_allocations
            where tenant_id=@TenantId
              and (@FarmId is null or farm_id=@FarmId)
              and (@HerdId is null or herd_id=@HerdId)
              and (@AnimalId is null or animal_id=@AnimalId)
              and (@From is null or occurred_on>=@From)
              and (@To is null or occurred_on<=@To)
            group by category, nature
            order by category, nature
            """, ct, farmId ?? tenant.FarmId, extra: new { HerdId = herdId, AnimalId = animalId, From = from, To = until });

    public Task<IReadOnlyList<dynamic>> HerdReportAsync(DateOnly asOf, Guid? farmId, CancellationToken ct) =>
        List("""
            select farm.name as "farmName", coalesce(a.category,h.category,'NÃO INFORMADA') as category,
                   count(*) filter (
                     where a.created_at::date <= @AsOf
                       and (a.deleted_at is null or a.deleted_at::date > @AsOf)
                       and not exists (
                         select 1 from agro360.livestock_animal_events e
                         where e.tenant_id=a.tenant_id and e.animal_id=a.id
                           and e.event_type in ('SALE','DEAD','DISCARDED','EXIT')
                           and e.occurred_on <= @AsOf)) as "headCount"
            from agro360.livestock_animals a
            join agro360.geo_farms farm on farm.id=a.farm_id and farm.tenant_id=a.tenant_id
            left join agro360.livestock_herds h on h.id=a.herd_id and h.tenant_id=a.tenant_id
            where a.tenant_id=@TenantId and (@FarmId is null or a.farm_id=@FarmId)
            group by farm.name, coalesce(a.category,h.category,'NÃO INFORMADA')
            order by farm.name, category
            """, ct, farmId ?? tenant.FarmId, extra: new { AsOf = asOf });

    public Task<byte[]> ExportCsvAsync(LivestockExportFilter filter, CancellationToken ct) =>
        Tx(async (c, t) =>
        {
            var kind = string.IsNullOrWhiteSpace(filter.Kind) ? "animals" : filter.Kind.ToLowerInvariant();
            var sql = kind switch
            {
                "animals" => """
                    select a.tag, a.species, a.breed, a.sex, a.category, a.status::text, a.current_weight_kg::text,
                           farm.name as farm, h.name as herd
                    from agro360.livestock_animals a
                    join agro360.geo_farms farm on farm.id=a.farm_id and farm.tenant_id=a.tenant_id
                    left join agro360.livestock_herds h on h.id=a.herd_id and h.tenant_id=a.tenant_id
                    where a.tenant_id=@TenantId and a.deleted_at is null
                      and (@FarmId is null or a.farm_id=@FarmId)
                    order by a.tag
                    """,
                "movements" => """
                    select a.tag, m.movement_kind, m.reason_code, m.moved_on::text, m.notes
                    from agro360.livestock_animal_movements m
                    join agro360.livestock_animals a on a.id=m.animal_id and a.tenant_id=m.tenant_id
                    where m.tenant_id=@TenantId
                      and (@From is null or m.moved_on>=@From)
                      and (@To is null or m.moved_on<=@To)
                    order by m.moved_on
                    """,
                "weighings" => """
                    select coalesce(a.tag,h.name) as subject, w.scope, w.weighed_at::text, w.weight_kg::text, w.unit, w.source
                    from agro360.livestock_weighings w
                    left join agro360.livestock_animals a on a.id=w.animal_id
                    left join agro360.livestock_herds h on h.id=w.herd_id
                    where w.tenant_id=@TenantId
                      and (@From is null or w.weighed_at::date>=@From)
                      and (@To is null or w.weighed_at::date<=@To)
                    order by w.weighed_at
                    """,
                "restrictions" => """
                    select a.tag, r.purpose, r.reason, r.started_on::text, r.expected_until::text, r.status
                    from agro360.livestock_restrictions r
                    left join agro360.livestock_animals a on a.id=r.animal_id
                    where r.tenant_id=@TenantId and (@Status is null or r.status=@Status)
                    order by r.started_on
                    """,
                _ => throw new DomainException("Relatório não suportado.", "livestock.report_invalid")
            };
            var rows = (await c.QueryAsync(new CommandDefinition(sql, new
            {
                tenant.TenantId,
                FarmId = filter.FarmId ?? tenant.FarmId,
                filter.From,
                filter.To,
                filter.Status
            }, t, cancellationToken: ct))).ToArray();
            var csv = new StringBuilder();
            if (rows.Length == 0)
            {
                csv.AppendLine("mensagem");
                csv.AppendLine(EscapeCsv("Nenhum registro autorizado para os filtros informados."));
                return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
            }
            var names = ((IDictionary<string, object>)rows[0]).Keys.ToArray();
            csv.AppendLine(string.Join(',', names.Select(EscapeCsv)));
            foreach (var row in rows)
            {
                var map = (IDictionary<string, object>)row;
                csv.AppendLine(string.Join(',', names.Select(name => EscapeCsv(Convert.ToString(map[name], CultureInfo.InvariantCulture)))));
            }
            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        }, ct);

    private async Task<Guid> MoveAnimalAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid animalId, string kind, string reason, HerdMovementCommand command, CancellationToken ct)
    {
        var animal = await LockAnimalAsync(c, t, animalId, ct);
        LivestockRules.EnsureEventNotBeforeBirth(command.OccurredOn, animal.BirthDate, animal.BirthDateEstimated);
        if (kind is "TRANSFER" or "LOT_CHANGE" or "LOCATION_CHANGE")
            LivestockRules.EnsureOnFarm(animal.Status, "movimentar");
        if (kind == "EXIT")
            LivestockRules.EnsureOnFarm(animal.Status, "dar baixa");
        if (kind == "EXIT" && reason == "SALE")
            throw new ConflictException("Saída comercial usa reserva, conferência e confirmação física — não transferência interna.", "livestock.sale_requires_reservation");
        if (command.ToFarmId is Guid toFarm)
            await EnsureFarmAsync(c, t, toFarm, ct);
        if (kind == "TRANSFER" && command.ToFarmId is Guid dest && dest != animal.FarmId)
        {
            var sameTenant = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from agro360.geo_farms where tenant_id=@TenantId and id=@Farm)",
                new { tenant.TenantId, Farm = dest }, t, cancellationToken: ct));
            if (!sameTenant)
                throw new ForbiddenException("Transferência interna não atravessa o isolamento entre clientes.");
        }
        if (command.ToHerdId is Guid toHerd)
        {
            var herd = await c.QuerySingleOrDefaultAsync<HerdRow>(new CommandDefinition(
                "select id, control_mode as ControlMode, head_count as HeadCount, farm_id as FarmId from agro360.livestock_herds where tenant_id=@TenantId and id=@Id for update",
                new { tenant.TenantId, Id = toHerd }, t, cancellationToken: ct))
                ?? throw new NotFoundException("Grupo", toHerd);
            LivestockRules.PreventDoubleCount(herd.ControlMode, true);
        }

        var later = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists(
              select 1 from agro360.livestock_animal_movements
              where tenant_id=@TenantId and animal_id=@Id and moved_on > @On)
            """,
            new { tenant.TenantId, Id = animalId, On = command.OccurredOn }, t, cancellationToken: ct));
        if (later)
            throw new ConflictException("Evento retroativo afetaria movimentações posteriores. Use ajuste ou estorno auditado.", "livestock.retroactive_conflict");

        var exitStatus = reason switch { "DEATH" => 4, "DISCARD" => 5, _ => (int?)null };
        var movementId = Guid.CreateVersion7();
        await c.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.livestock_animal_movements
                (id,tenant_id,animal_id,from_farm_id,to_farm_id,from_paddock_id,to_paddock_id,moved_on,notes,
                 movement_kind,reason_code,responsible_id,from_herd_id,to_herd_id,from_facility_id,to_facility_id,created_by)
            values (@Movement,@TenantId,@Animal,@FromFarm,coalesce(@ToFarm,@FromFarm),@FromPaddock,@ToPaddock,@On,@Notes,
                    @Kind,@Reason,@Responsible,@FromHerd,@ToHerd,@FromFacility,@ToFacility,@UserId);
            update agro360.livestock_animals
            set farm_id=coalesce(@ToFarm,farm_id),
                herd_id=coalesce(@ToHerd,herd_id),
                facility_id=coalesce(@ToFacility,facility_id),
                paddock_id=coalesce(@ToPaddock,paddock_id),
                status=coalesce(@ExitStatus,status),
                origin_type=case when @Kind='ENTRY' then @Reason else origin_type end,
                updated_at=now(), updated_by=@UserId, version=version+1
            where tenant_id=@TenantId and id=@Animal;
            insert into agro360.livestock_animal_events(id,tenant_id,animal_id,event_type,occurred_on,data,created_at,created_by)
            values (@Event,@TenantId,@Animal,@Kind,@On,jsonb_build_object('reason',@Reason,'notes',@Notes),now(),@UserId)
            """,
            new
            {
                Movement = movementId,
                tenant.TenantId,
                Animal = animalId,
                FromFarm = animal.FarmId,
                ToFarm = command.ToFarmId,
                FromPaddock = animal.PaddockId,
                ToPaddock = command.ToPaddockId,
                On = command.OccurredOn,
                command.Notes,
                Kind = kind,
                Reason = reason,
                Responsible = command.ResponsibleId,
                FromHerd = animal.HerdId,
                ToHerd = command.ToHerdId,
                FromFacility = animal.FacilityId,
                ToFacility = command.ToFacilityId,
                tenant.UserId,
                ExitStatus = exitStatus,
                Event = Guid.CreateVersion7()
            }, t, cancellationToken: ct));
        await SyncIndividualHerdCountAsync(c, t, animal.HerdId, ct);
        await SyncIndividualHerdCountAsync(c, t, command.ToHerdId, ct);
        await Audit(c, t, kind.ToLowerInvariant(), "Animal", animalId, command, ct);
        return movementId;
    }

    private async Task<Guid> MoveHerdAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid herdId, string kind, string reason, HerdMovementCommand command, CancellationToken ct)
    {
        var quantity = command.Quantity ?? throw new DomainException("Informe a quantidade do grupo.", "livestock.quantity_required");
        LivestockRules.EnsurePositiveQuantity(quantity, "Quantidade");
        var herd = await c.QuerySingleOrDefaultAsync<HerdRow>(new CommandDefinition(
            "select id, control_mode as ControlMode, head_count as HeadCount, farm_id as FarmId from agro360.livestock_herds where tenant_id=@TenantId and id=@Id and deleted_at is null for update",
            new { tenant.TenantId, Id = herdId }, t, cancellationToken: ct))
            ?? throw new NotFoundException("Grupo", herdId);
        if (!string.Equals(herd.ControlMode, "QUANTITY", StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Movimentação por quantidade aplica-se somente a grupos coletivos.", "livestock.quantity_mode_required");
        var delta = kind switch
        {
            "ENTRY" => quantity,
            "EXIT" or "TRANSFER" => -quantity,
            "ADJUST" => quantity,
            _ => throw new DomainException("Tipo de movimentação coletiva inválido.", "livestock.herd_movement_invalid")
        };
        if (herd.HeadCount + delta < 0)
            throw new ConflictException("A operação geraria saldo negativo no grupo.", "livestock.negative_balance");
        if (kind == "TRANSFER" && command.ToFarmId is null)
            throw new DomainException("Informe a propriedade de destino.", "livestock.transfer_destination");
        var id = Guid.CreateVersion7();
        await c.ExecuteAsync(new CommandDefinition(
            """
            insert into agro360.livestock_herd_movements
                (id,tenant_id,herd_id,movement_kind,quantity,occurred_on,reason_code,origin_notes,responsible_id,
                 from_farm_id,to_farm_id,from_facility_id,to_facility_id,idempotency_key,notes,created_by)
            values (@Id,@TenantId,@Herd,@Kind,@Quantity,@On,@Reason,@Origin,@Responsible,
                    @FromFarm,@ToFarm,@FromFacility,@ToFacility,@IdempotencyKey,@Notes,@UserId);
            update agro360.livestock_herds
            set head_count=head_count+@Delta,
                farm_id=case when @Kind='TRANSFER' then coalesce(@ToFarm,farm_id) else farm_id end,
                updated_at=now(), updated_by=@UserId, version=version+1
            where tenant_id=@TenantId and id=@Herd
            """,
            new
            {
                Id = id,
                tenant.TenantId,
                Herd = herdId,
                Kind = kind,
                Quantity = quantity,
                On = command.OccurredOn,
                Reason = reason,
                Origin = command.OriginNotes,
                Responsible = command.ResponsibleId,
                FromFarm = herd.FarmId,
                ToFarm = command.ToFarmId,
                FromFacility = (Guid?)null,
                ToFacility = command.ToFacilityId,
                command.IdempotencyKey,
                command.Notes,
                tenant.UserId,
                Delta = delta
            }, t, cancellationToken: ct));
        await Audit(c, t, kind.ToLowerInvariant(), "Herd", herdId, command, ct);
        return id;
    }

    private Task SyncIndividualHerdCountAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid? herdId, CancellationToken ct)
    {
        if (herdId is null) return Task.CompletedTask;
        return c.ExecuteAsync(new CommandDefinition(
            """
            update agro360.livestock_herds h
            set head_count = (select count(*) from agro360.livestock_animals a
                              where a.tenant_id=h.tenant_id and a.herd_id=h.id and a.status in (1,2,6) and a.deleted_at is null),
                updated_at=now()
            where h.tenant_id=@TenantId and h.id=@Id and h.control_mode='INDIVIDUAL'
            """,
            new { tenant.TenantId, Id = herdId }, t, cancellationToken: ct));
    }

    private async Task<AnimalLockRow> LockAnimalAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid id, CancellationToken ct, bool includeDeleted = false)
    {
        var animal = await c.QuerySingleOrDefaultAsync<AnimalLockRow>(new CommandDefinition(
            $"""
            select id, farm_id as FarmId, herd_id as HerdId, tag, species, status, birth_date as BirthDate,
                   birth_date_estimated as BirthDateEstimated, paddock_id as PaddockId, facility_id as FacilityId,
                   withdrawal_until as WithdrawalUntil, version, deleted_at as DeletedAt
            from agro360.livestock_animals
            where tenant_id=@TenantId and id=@Id {(includeDeleted ? "" : "and deleted_at is null")}
            for update
            """,
            new { tenant.TenantId, Id = id }, t, cancellationToken: ct));
        return animal ?? throw new NotFoundException("Animal", id);
    }

    private Task<bool> HasActiveRestrictionAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid animalId, string? purpose, CancellationToken ct) =>
        c.ExecuteScalarAsync<bool>(new CommandDefinition(
            """
            select exists(
              select 1 from agro360.livestock_restrictions
              where tenant_id=@TenantId and animal_id=@Animal and status='ACTIVE'
                and (@Purpose is null or purpose=@Purpose or purpose='OPERATIONAL'))
            """,
            new { tenant.TenantId, Animal = animalId, Purpose = purpose }, t, cancellationToken: ct));

    private async Task EnsureFarmAsync(NpgsqlConnection c, NpgsqlTransaction t, Guid farmId, CancellationToken ct)
    {
        var ok = await c.ExecuteScalarAsync<bool>(new CommandDefinition(
            "select exists(select 1 from agro360.geo_farms where tenant_id=@TenantId and id=@Id and deleted_at is null)",
            new { tenant.TenantId, Id = farmId }, t, cancellationToken: ct));
        if (!ok) throw new NotFoundException("Propriedade", farmId);
        if (tenant.FarmId is Guid scoped && scoped != farmId)
            throw new ForbiddenException("Propriedade fora do contexto autorizado.");
    }

    private async Task AdjustStockAsync(
        NpgsqlConnection c, NpgsqlTransaction t, Guid warehouse, Guid product, string unit, decimal quantity,
        string? lotNumber, Guid reference, string type, CancellationToken ct)
    {
        var row = await c.QuerySingleOrDefaultAsync<StockRow>(new CommandDefinition(
            """
            select b.id, b.available, b.reserved, b.average_cost as AverageCost, b.version, b.unit,
                   l.quality_status as QualityStatus
            from agro360.inventory_stock_balances b
            left join agro360.inventory_stock_lots l
              on l.tenant_id=b.tenant_id and l.warehouse_id=b.warehouse_id and l.product_id=b.product_id
             and (@Lot is null or l.lot_number=@Lot)
            where b.tenant_id=@TenantId and b.warehouse_id=@Warehouse and b.product_id=@Product
            for update of b
            """,
            new { tenant.TenantId, Warehouse = warehouse, Product = product, Lot = lotNumber }, t, cancellationToken: ct))
            ?? throw new ConflictException("Insumo sem saldo.", "agro360.inventory_insufficient_stock");
        if (row.QualityStatus is "BLOCKED" or "PENDING" or "REJECTED" or "QUARANTINE")
            throw new ConflictException("Lote bloqueado, em quarentena ou incompatível não pode retornar ao uso livre.", "agro360.inventory_lot_blocked");
        if (!string.Equals(row.Unit, unit, StringComparison.OrdinalIgnoreCase))
            throw new DomainException("Unidade incompatível. Informe conversão explícita.", "agro360.inventory_unit_mismatch");
        var balance = row.Available + quantity;
        await c.ExecuteAsync(new CommandDefinition(
            """
            update agro360.inventory_stock_balances
            set available=@Balance, version=version+1, updated_at=now()
            where id=@Id and tenant_id=@TenantId and version=@Version;
            insert into agro360.inventory_stock_movements
                (id,tenant_id,warehouse_id,product_id,movement_type,quantity,unit,unit_cost,total_cost,lot_number,
                 reference_type,reference_id,balance_after,average_cost_after,balance_version,occurred_at,created_by)
            values (@Movement,@TenantId,@Warehouse,@Product,'ADJUSTMENT_IN',@Quantity,@Unit,@Cost,@Total,@Lot,
                    @Type,@Reference,@Balance,@Cost,@NewVersion,now(),@UserId)
            """,
            new
            {
                Balance = balance,
                Id = row.Id,
                tenant.TenantId,
                row.Version,
                Movement = Guid.CreateVersion7(),
                Warehouse = warehouse,
                Product = product,
                Quantity = quantity,
                Unit = unit,
                Cost = row.AverageCost,
                Total = quantity * row.AverageCost,
                Lot = lotNumber,
                Type = type,
                Reference = reference,
                NewVersion = row.Version + 1,
                tenant.UserId
            }, t, cancellationToken: ct));
    }

    private static string LookupSql(string resource) => resource.ToLowerInvariant() switch
    {
        "animals" => "select id, tag label, concat(species,' · ',coalesce(category,'')) description, case status when 1 then 'ACTIVE' when 6 then 'RESERVED' else 'INACTIVE' end status from agro360.livestock_animals where tenant_id=@TenantId and deleted_at is null and (@FarmId is null or farm_id=@FarmId)",
        "herds" => "select id, name label, concat(control_mode,' · ',head_count,' cab.') description, status from agro360.livestock_herds where tenant_id=@TenantId and deleted_at is null and (@FarmId is null or farm_id=@FarmId)",
        "facilities" => "select id, name label, kind description, status from agro360.livestock_facilities where tenant_id=@TenantId and deleted_at is null and (@FarmId is null or farm_id=@FarmId)",
        "paddocks" => "select id, name label, status description, status from agro360.livestock_paddocks where tenant_id=@TenantId",
        "handling-lots" => "select id, name label, purpose description, status from agro360.livestock_handling_lots where tenant_id=@TenantId and (@FarmId is null or farm_id=@FarmId)",
        "reasons" => "select id, name label, direction description, case when active then 'ACTIVE' else 'INACTIVE' end status from agro360.livestock_movement_reasons where tenant_id=@TenantId",
        "handling-types" => "select id, name label, code description, case when active then 'ACTIVE' else 'INACTIVE' end status from agro360.livestock_handling_types where tenant_id=@TenantId",
        "people" => "select id, name label, email description, status from agro360.identity_users where tenant_id=@TenantId and deleted_at is null",
        "properties" => "select id, name label, concat(municipality,' · ',state) description, 'ACTIVE' status from agro360.geo_farms where tenant_id=@TenantId and deleted_at is null",
        "products" => "select id, name label, concat(sku,' · ',base_unit) description, 'ACTIVE' status from agro360.inventory_products where tenant_id=@TenantId and deleted_at is null",
        "warehouses" => "select id, name label, type description, 'ACTIVE' status from agro360.inventory_warehouses where tenant_id=@TenantId and deleted_at is null",
        _ => throw new NotFoundException("Lookup", Guid.Empty)
    };

    private static string EscapeCsv(string? value)
    {
        var text = value ?? string.Empty;
        LivestockRules.EnsureCsvSafe(ref text);
        var escaped = text.Replace("\"", "\"\"", StringComparison.Ordinal);
        return escaped.Contains(',', StringComparison.Ordinal)
            || escaped.Contains('"', StringComparison.Ordinal)
            || escaped.Contains('\n', StringComparison.Ordinal)
            || escaped.Contains('\r', StringComparison.Ordinal)
            || escaped.Contains(';', StringComparison.Ordinal)
                ? $"\"{escaped}\""
                : escaped;
    }

    private Task<IReadOnlyList<dynamic>> List(string sql, CancellationToken ct, Guid? farmId = null, string? status = null, object? extra = null) =>
        Tx<IReadOnlyList<dynamic>>(async (c, t) =>
        {
            var args = new DynamicParameters(extra ?? new { });
            args.Add("TenantId", tenant.TenantId);
            args.Add("FarmId", farmId ?? tenant.FarmId);
            args.Add("Status", status);
            args.Add("UserId", tenant.UserId);
            return (await c.QueryAsync(new CommandDefinition(sql, args, t, cancellationToken: ct))).AsList();
        }, ct);

    private Task Audit(NpgsqlConnection c, NpgsqlTransaction t, string action, string entity, Guid id, object value, CancellationToken ct)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? "unavailable";
        InfrastructureLogMessages.LivestockOperation(logger, action, tenant.TenantId, tenant.UserId, traceId);
        return c.WriteAuditAsync(t, tenant, action, entity, id, null, value, ct);
    }

    private Task<T> Tx<T>(Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> work, CancellationToken ct) =>
        db.InTenantTransactionAsync(work, ct);

    private Task Tx(Func<NpgsqlConnection, NpgsqlTransaction, Task> work, CancellationToken ct) =>
        db.InTenantTransactionAsync(work, ct);

    private sealed record LookupRow(Guid Id, string Label, string Description, string Status);
    private sealed class HerdRow
    {
        public Guid Id { get; set; }
        public string ControlMode { get; set; } = "INDIVIDUAL";
        public int HeadCount { get; set; }
        public Guid FarmId { get; set; }
    }
    private sealed class AnimalLockRow
    {
        public Guid Id { get; set; }
        public Guid FarmId { get; set; }
        public Guid? HerdId { get; set; }
        public string Tag { get; set; } = string.Empty;
        public string Species { get; set; } = "BOVINE";
        public short Status { get; set; }
        public DateOnly BirthDate { get; set; }
        public bool BirthDateEstimated { get; set; }
        public Guid? PaddockId { get; set; }
        public Guid? FacilityId { get; set; }
        public DateOnly? WithdrawalUntil { get; set; }
        public long Version { get; set; }
        public DateTimeOffset? DeletedAt { get; set; }
    }
    private sealed class HandlingOrderRow
    {
        public Guid Id { get; set; }
        public string Status { get; set; } = "DRAFT";
        public int PlannedHeadCount { get; set; }
        public int AttendedHeadCount { get; set; }
        public int NotAttendedHeadCount { get; set; }
        public int BlockedHeadCount { get; set; }
        public bool MassAtomic { get; set; }
        public Guid? WorkTaskId { get; set; }
    }
    private sealed class WeighingRow
    {
        public Guid Id { get; set; }
        public Guid? AnimalId { get; set; }
        public decimal WeightKg { get; set; }
        public string Scope { get; set; } = "INDIVIDUAL";
    }
    private sealed class FeedingRow
    {
        public Guid Id { get; set; }
        public Guid? WarehouseId { get; set; }
        public Guid? ProductId { get; set; }
        public string? LotNumber { get; set; }
        public string? Unit { get; set; }
        public decimal? SuppliedQuantity { get; set; }
        public decimal ReturnedQuantity { get; set; }
        public decimal LostQuantity { get; set; }
        public decimal? ConsumedQuantity { get; set; }
        public Guid PlanId { get; set; }
    }
    private sealed class ReservationRow
    {
        public Guid Id { get; set; }
        public Guid FarmId { get; set; }
        public Guid? AnimalId { get; set; }
        public Guid? HerdId { get; set; }
        public int Quantity { get; set; }
        public string Status { get; set; } = "ACTIVE";
        public Guid? SaleId { get; set; }
    }
    private sealed class StockRow
    {
        public Guid Id { get; set; }
        public decimal Available { get; set; }
        public decimal Reserved { get; set; }
        public decimal AverageCost { get; set; }
        public long Version { get; set; }
        public string Unit { get; set; } = "kg";
        public string? QualityStatus { get; set; }
    }
    private sealed class WeightLimitRow
    {
        public decimal MinKg { get; set; }
        public decimal MaxKg { get; set; }
    }
    private sealed class PreviousWeighingRow
    {
        public decimal WeightKg { get; set; }
        public DateTimeOffset WeighedAt { get; set; }
    }
}
