using System.Text.Json;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;

namespace Agro360.Infrastructure.Services;

public sealed class SeasonTrackingService(DatabaseExecutor database, ITenantContext tenant) : ISeasonTrackingService
{
    public Task<SeasonTrackingOverview> OverviewAsync(Guid seasonId, DateOnly? referenceDate, CancellationToken ct) =>
        database.InTenantTransactionAsync(async (c, t) =>
        {
            var cutoff = referenceDate ?? DateOnly.FromDateTime(DateTime.UtcNow);
            var season = await c.QuerySingleOrDefaultAsync<SeasonRow>(new CommandDefinition("""
                select s.id SeasonId,s.farm_id FarmId,f.name Farm,s.name Season,s.crop Crop,s.start_date StartsOn,
                 s.end_date EndsOn,case s.status when 1 then 'PLANNED' when 2 then 'ACTIVE' when 3 then 'COMPLETED' when 4 then 'CANCELLED' else 'CLOSED' end Status,
                 s.planned_area_ha PlannedPhysicalAreaHa
                from agro360.agriculture_seasons s join agro360.geo_farms f on f.tenant_id=s.tenant_id and f.id=s.farm_id
                where s.tenant_id=@TenantId and s.id=@SeasonId and s.deleted_at is null
                """, new { tenant.TenantId, SeasonId = seasonId }, t, cancellationToken: ct));
            if (season is null) throw new NotFoundException("Safra", seasonId);
            var fields = (await c.QueryAsync(new CommandDefinition("""
                select distinct f.id,f.name,f.area_ha from agro360.agriculture_plan_operations o
                join agro360.geo_fields f on f.tenant_id=o.tenant_id and f.id=o.field_id
                where o.tenant_id=@TenantId and o.season_id=@SeasonId and o.deleted_at is null order by f.name,f.id
                """, new { tenant.TenantId, SeasonId = seasonId }, t, cancellationToken: ct))).ToArray();
            var operations = (await c.QueryAsync<OperationRow>(new CommandDefinition(OperationSql + " where o.tenant_id=@TenantId and o.season_id=@SeasonId and o.deleted_at is null order by o.planned_start,o.id", new { tenant.TenantId, SeasonId = seasonId }, t, cancellationToken: ct))).Select(Map).ToArray();
            var materials = (await c.QueryAsync(new CommandDefinition("""
                select m.product_id,p.name,m.unit,sum(m.planned_quantity) planned_quantity,sum(m.consumed_quantity) consumed_quantity,
                 bool_or(m.unit_cost is null) cost_pending
                from agro360.agriculture_operation_orders l join agro360.field_work_order_materials m on m.tenant_id=l.tenant_id and m.work_order_id=l.work_order_id and m.deleted_at is null
                join agro360.inventory_products p on p.tenant_id=m.tenant_id and p.id=m.product_id
                join agro360.agriculture_plan_operations o on o.tenant_id=l.tenant_id and o.id=l.operation_id
                where o.tenant_id=@TenantId and o.season_id=@SeasonId and o.deleted_at is null group by m.product_id,p.name,m.unit order by p.name
                """, new { tenant.TenantId, SeasonId = seasonId }, t, cancellationToken: ct))).ToArray();
            var costs = (await c.QueryAsync(new CommandDefinition("""
                select b.currency,sum(a.amount) amount from agro360.cost_allocations a
                join agro360.cost_allocation_batches b on b.tenant_id=a.tenant_id and b.id=a.batch_id
                join agro360.cost_management_entries e on e.tenant_id=a.tenant_id and e.id=a.entry_id
                where a.tenant_id=@TenantId and a.season_id=@SeasonId and a.status='CONFIRMED' and b.status='CONFIRMED' and e.deleted_at is null and e.competence_date<=@Cutoff
                group by b.currency order by b.currency
                """, new { tenant.TenantId, SeasonId = seasonId, Cutoff = cutoff }, t, cancellationToken: ct))).ToArray();
            var production = (await c.QueryAsync(new CommandDefinition("""
                select r.unit,sum(r.harvested_quantity) quantity from agro360.harvest_records r join agro360.harvest_plans p on p.tenant_id=r.tenant_id and p.id=r.plan_id
                where r.tenant_id=@TenantId and p.season_id=@SeasonId and r.status<>'CANCELLED' and r.operational_at::date<=@Cutoff group by r.unit order by r.unit
                """, new { tenant.TenantId, SeasonId = seasonId, Cutoff = cutoff }, t, cancellationToken: ct))).ToArray();
            var history = (await c.QueryAsync(new CommandDefinition("""
                select r.operation_id,r.version,r.reason,r.before_value,r.after_value,r.impact,r.created_at,r.created_by
                from agro360.agriculture_plan_revisions r join agro360.agriculture_plan_operations o on o.tenant_id=r.tenant_id and o.id=r.operation_id
                where r.tenant_id=@TenantId and o.season_id=@SeasonId order by r.created_at desc
                """, new { tenant.TenantId, SeasonId = seasonId }, t, cancellationToken: ct))).ToArray();
            var completed = operations.Count(x => x.Status == "COMPLETED");
            var inProgress = operations.Count(x => x.Status is "IN_PROGRESS" or "PARTIAL");
            var overdue = operations.Count(x => x.Status is not ("COMPLETED" or "CANCELLED") && DateOnly.FromDateTime(x.PlannedEnd.UtcDateTime) < cutoff);
            var executed = operations.Sum(x => x.ExecutedAreaHa); // worked area, intentionally not physical area
            var metrics = new List<SeasonMetric>
            {
                new("planned-operations","Operações previstas",operations.Length,null,"AVAILABLE","Quantidade de operações ativas do plano.","#season-operations",null),
                new("completed-operations","Operações concluídas",completed,null,"AVAILABLE","Operações cujo estado vigente é concluído.","#season-operations",null),
                new("in-progress-operations","Operações em andamento",inProgress,null,"AVAILABLE","Operações parciais ou em execução.","#season-operations",null),
                new("overdue-operations","Operações atrasadas",overdue,null,"AVAILABLE","Operações abertas com fim previsto anterior à data de referência.","#season-pending",null),
                new("worked-area","Área trabalhada acumulada",operations.Length == 0 ? null : executed,"ha",operations.Length == 0 ? "UNAVAILABLE" : "AVAILABLE","Soma dos apontamentos de área por operação; não representa área física distinta.","#season-operations",operations.Length == 0 ? "Não há operações planejadas." : null),
                new("physical-area","Área física planejada",season.PlannedPhysicalAreaHa,"ha","AVAILABLE","Área física registrada na safra; não soma passadas operacionais.","#season-fields",null)
            };
            var pending = operations.Where(x => x.BlockReason is not null).Select(x => new SeasonPending("PLANNING", x.BlockReason!, x.PlannedStart, x.ResponsibleId?.ToString(), "Impede a liberação da operação.", "Abrir a predecessora e regularizar ou solicitar exceção.", x.BlockUrl!, "BLOCKER")).ToList();
            pending.AddRange(operations.Where(x => x.ResponsibleId is null && x.Status != "CANCELLED").Select(x => new SeasonPending("PLANNING", "Operação sem responsável.", x.PlannedStart, null, "A ordem não pode ser liberada.", "Definir responsável no planejamento.", $"/Agriculture?operationId={x.Id}", "BLOCKER")));
            pending.AddRange(operations.Where(x => x.Status is not ("COMPLETED" or "CANCELLED") && DateOnly.FromDateTime(x.PlannedEnd.UtcDateTime) < cutoff).Select(x => new SeasonPending("SCHEDULE", "Atividade atrasada.", x.PlannedEnd, x.ResponsibleId?.ToString(), "Pode comprometer a próxima operação.", "Executar ou reprogramar com justificativa.", $"/Agriculture?operationId={x.Id}", "WARNING")));
            return new(season.SeasonId, season.FarmId, season.Farm, season.Season, season.Crop, season.StartsOn, season.EndsOn,
                season.Status, season.PlannedPhysicalAreaHa, fields, operations, metrics, pending, materials, costs, production, history);
        }, ct);

    public Task<PlanOperationDto> AddOperationAsync(Guid planId, PlanOperationCommand x, CancellationToken ct)
    {
        ValidateOperation(x.PlannedStart, x.PlannedEnd, x.PlannedAreaHa);
        return database.InTenantTransactionAsync(async (c, t) =>
        {
            var scope = await ValidateScope(c, t, planId, x.SeasonId, x.FarmId, x.FieldId, ct);
            if ((x.PlannedStart.Date < scope.StartsOn.ToDateTime(TimeOnly.MinValue) || x.PlannedEnd.Date > scope.EndsOn.ToDateTime(TimeOnly.MinValue)) && string.IsNullOrWhiteSpace(x.OutsideSeasonReason))
                throw new DomainException("Operação fora da janela da safra exige justificativa explícita.", "agriculture.outside_season_reason_required");
            var id = Guid.CreateVersion7();
            await c.ExecuteAsync(new CommandDefinition("""
                insert into agro360.agriculture_plan_operations(id,tenant_id,plan_record_id,season_id,farm_id,field_id,name,operation_type,planned_start,planned_end,original_start,original_end,planned_area_ha,original_area_ha,planned_hours,responsible_id,outside_season_reason,created_by,updated_by)
                values(@Id,@TenantId,@PlanId,@SeasonId,@FarmId,@FieldId,@Name,@OperationType,@PlannedStart,@PlannedEnd,@PlannedStart,@PlannedEnd,@PlannedAreaHa,@PlannedAreaHa,@PlannedHours,@ResponsibleId,@OutsideSeasonReason,@UserId,@UserId)
                """, new { Id = id, tenant.TenantId, PlanId = planId, x.SeasonId, x.FarmId, x.FieldId, Name = x.Name.Trim(), OperationType = x.OperationType.Trim(), x.PlannedStart, x.PlannedEnd, x.PlannedAreaHa, x.PlannedHours, x.ResponsibleId, x.OutsideSeasonReason, tenant.UserId }, t, cancellationToken: ct));
            return Map(await GetOperation(c, t, id, ct));
        }, ct);
    }

    public Task AddDependencyAsync(Guid operationId, OperationDependencyCommand x, CancellationToken ct) => database.InTenantTransactionAsync(async (c, t) =>
    {
        var kind = x.BlockingType.Trim().ToUpperInvariant(); var condition = x.ReleaseCondition.Trim().ToUpperInvariant();
        if (operationId == x.PredecessorId) throw new DomainException("Uma atividade não pode depender dela mesma.", "agriculture.dependency_self");
        if (kind is not ("REQUIRED" or "ADVISORY") || condition is not ("COMPLETED" or "REVIEWED")) throw new DomainException("Tipo ou condição de dependência inválida.");
        await GetOperation(c, t, operationId, ct); await GetOperation(c, t, x.PredecessorId, ct);
        var cycle = await c.ExecuteScalarAsync<bool>(new CommandDefinition("""
            with recursive path(id) as (select @OperationId union select d.predecessor_id from agro360.agriculture_operation_dependencies d join path p on p.id=d.operation_id where d.tenant_id=@TenantId and d.deleted_at is null)
            select exists(select 1 from path where id=@PredecessorId)
            """, new { tenant.TenantId, OperationId = x.PredecessorId, PredecessorId = operationId }, t, cancellationToken: ct));
        if (cycle) throw new ConflictException("A dependência criaria um ciclo no planejamento.", "agriculture.dependency_cycle");
        await c.ExecuteAsync(new CommandDefinition("insert into agro360.agriculture_operation_dependencies(id,tenant_id,operation_id,predecessor_id,blocking_type,release_condition,created_by,updated_by) values(@Id,@TenantId,@OperationId,@PredecessorId,@Kind,@Condition,@UserId,@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, OperationId = operationId, x.PredecessorId, Kind = kind, Condition = condition, tenant.UserId }, t, cancellationToken: ct));
    }, ct);

    public Task<Guid> GenerateOrderAsync(Guid operationId, GenerateOperationOrderCommand x, CancellationToken ct)
    {
        if (x.CoveredAreaHa <= 0 || string.IsNullOrWhiteSpace(x.IdempotencyKey)) throw new DomainException("Cobertura e chave de idempotência são obrigatórias.");
        return database.InTenantTransactionAsync(async (c, t) =>
        {
            var replay = await c.ExecuteScalarAsync<Guid?>(new CommandDefinition("select work_order_id from agro360.agriculture_operation_orders where tenant_id=@TenantId and idempotency_key=@Key", new { tenant.TenantId, Key = x.IdempotencyKey }, t, cancellationToken: ct));
            if (replay is not null) return replay.Value;
            var o = await GetOperation(c, t, operationId, ct);
            var covered = await c.ExecuteScalarAsync<decimal>(new CommandDefinition("select coalesce(sum(covered_area_ha),0) from agro360.agriculture_operation_orders where tenant_id=@TenantId and operation_id=@Id", new { tenant.TenantId, Id = operationId }, t, cancellationToken: ct));
            if (covered + x.CoveredAreaHa > o.PlannedAreaHa) throw new ConflictException("A cobertura das ordens supera a área planejada.", "agriculture.order_coverage_exceeded");
            var id = Guid.CreateVersion7(); var number = await c.ExecuteScalarAsync<long>(new CommandDefinition("select nextval('agro360.field_work_order_number_seq')", transaction: t, cancellationToken: ct));
            await c.ExecuteAsync(new CommandDefinition("""
                insert into agro360.agriculture_records(id,tenant_id,module,status,data,created_by)
                values(@Id,@TenantId,'work-orders','PLANNED',jsonb_build_object('number',@Number,'name',@Name,'type',@Type,'propertyId',@FarmId,'fieldId',@FieldId,'cropSeasonId',@SeasonId,'responsibleId',@ResponsibleId,'plannedAt',@Starts,'finishedAt',@Ends,'area',@Area,'unit','ha','origin','Plano de safra','planOperationId',@OperationId),@UserId)
                """, new { Id = id, tenant.TenantId, Number = $"OC-{DateTime.UtcNow:yyyy}-{number:000000}", o.Name, Type = o.OperationType, o.FarmId, o.FieldId, o.SeasonId, o.ResponsibleId, Starts = o.PlannedStart, Ends = o.PlannedEnd, Area = x.CoveredAreaHa, OperationId = operationId, tenant.UserId }, t, cancellationToken: ct));
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.agriculture_operation_orders(tenant_id,operation_id,work_order_id,covered_area_ha,idempotency_key,created_by) values(@TenantId,@OperationId,@Id,@Area,@Key,@UserId)", new { tenant.TenantId, OperationId = operationId, Id = id, Area = x.CoveredAreaHa, Key = x.IdempotencyKey, tenant.UserId }, t, cancellationToken: ct));
            return id;
        }, ct);
    }

    public Task<PlanOperationDto> RescheduleAsync(Guid operationId, RescheduleOperationCommand x, CancellationToken ct)
    {
        ValidateOperation(x.PlannedStart, x.PlannedEnd, x.PlannedAreaHa); if (string.IsNullOrWhiteSpace(x.Reason)) throw new DomainException("Informe o motivo da reprogramação.");
        return database.InTenantTransactionAsync(async (c, t) =>
        {
            var before = await GetOperation(c, t, operationId, ct);
            var seasonWindow = await c.QuerySingleAsync<ScopeRow>(new CommandDefinition("select start_date StartsOn,end_date EndsOn from agro360.agriculture_seasons where tenant_id=@TenantId and id=@SeasonId and deleted_at is null", new { tenant.TenantId, before.SeasonId }, t, cancellationToken: ct));
            if (x.PlannedStart.Date < seasonWindow.StartsOn.ToDateTime(TimeOnly.MinValue) || x.PlannedEnd.Date > seasonWindow.EndsOn.ToDateTime(TimeOnly.MinValue))
                throw new DomainException("A reprogramação fora da safra exige uma nova justificativa de exceção no planejamento.", "agriculture.outside_season_reason_required");
            if (x.PlannedAreaHa < before.ScheduledAreaHa) throw new ConflictException("A área revisada não pode ser menor que a cobertura das ordens já vinculadas.", "agriculture.order_coverage_exceeded");
            var released = await c.ExecuteScalarAsync<int>(new CommandDefinition("select count(*) from agro360.agriculture_operation_orders l join agro360.agriculture_records r on r.tenant_id=l.tenant_id and r.id=l.work_order_id where l.tenant_id=@TenantId and l.operation_id=@Id and r.status not in('OPEN','PLANNED','AWAITING_RESOURCES','CANCELLED')", new { tenant.TenantId, Id = operationId }, t, cancellationToken: ct));
            var changed = await c.ExecuteAsync(new CommandDefinition("update agro360.agriculture_plan_operations set planned_start=@PlannedStart,planned_end=@PlannedEnd,planned_area_ha=@PlannedAreaHa,responsible_id=@ResponsibleId,updated_at=now(),updated_by=@UserId,version=version+1 where tenant_id=@TenantId and id=@Id and version=@Version and deleted_at is null", new { x.PlannedStart, x.PlannedEnd, x.PlannedAreaHa, x.ResponsibleId, tenant.UserId, tenant.TenantId, Id = operationId, x.Version }, t, cancellationToken: ct));
            if (changed != 1) throw new ConflictException("O planejamento foi alterado. Recarregue antes de reprogramar.", "agriculture.version_conflict");
            var after = await GetOperation(c, t, operationId, ct);
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.agriculture_plan_revisions(id,tenant_id,operation_id,version,reason,before_value,after_value,impact,created_by) values(@Id,@TenantId,@OperationId,@Version,@Reason,cast(@Before as jsonb),cast(@After as jsonb),jsonb_build_object('linkedOrders',@LinkedOrders,'releasedOrdersNotChanged',@Released),@UserId)", new { Id = Guid.CreateVersion7(), tenant.TenantId, OperationId = operationId, Version = after.Version, Reason = x.Reason.Trim(), Before = JsonSerializer.Serialize(before), After = JsonSerializer.Serialize(after), LinkedOrders = await c.ExecuteScalarAsync<int>(new CommandDefinition("select count(*) from agro360.agriculture_operation_orders where tenant_id=@TenantId and operation_id=@OperationId", new { tenant.TenantId, OperationId = operationId }, t, cancellationToken: ct)), Released = released, tenant.UserId }, t, cancellationToken: ct));
            return Map(after);
        }, ct);
    }

    private const string OperationSql = """select o.*,coalesce((select sum(l.physical_area_ha) from agro360.agriculture_operation_orders x join agro360.field_work_logs l on l.tenant_id=x.tenant_id and l.work_order_id=x.work_order_id and l.deleted_at is null where x.tenant_id=o.tenant_id and x.operation_id=o.id),0) ExecutedAreaHa,coalesce((select sum(x.covered_area_ha) from agro360.agriculture_operation_orders x where x.tenant_id=o.tenant_id and x.operation_id=o.id),0) ScheduledAreaHa,(select 'Predecessora '||p.name||' está '||p.status from agro360.agriculture_operation_dependencies d join agro360.agriculture_plan_operations p on p.tenant_id=d.tenant_id and p.id=d.predecessor_id where d.tenant_id=o.tenant_id and d.operation_id=o.id and d.blocking_type='REQUIRED' and d.deleted_at is null and d.exception_authorized_at is null and p.status<>'COMPLETED' order by p.planned_start limit 1) BlockReason,(select '/Agriculture?operationId='||p.id from agro360.agriculture_operation_dependencies d join agro360.agriculture_plan_operations p on p.tenant_id=d.tenant_id and p.id=d.predecessor_id where d.tenant_id=o.tenant_id and d.operation_id=o.id and d.blocking_type='REQUIRED' and d.deleted_at is null and d.exception_authorized_at is null and p.status<>'COMPLETED' order by p.planned_start limit 1) BlockUrl from agro360.agriculture_plan_operations o""";
    private async Task<OperationRow> GetOperation(System.Data.IDbConnection c, System.Data.IDbTransaction t, Guid id, CancellationToken ct) => await c.QuerySingleOrDefaultAsync<OperationRow>(new CommandDefinition(OperationSql + " where o.tenant_id=@TenantId and o.id=@Id and o.deleted_at is null", new { tenant.TenantId, Id = id }, t, cancellationToken: ct)) ?? throw new NotFoundException("Operação planejada", id);
    private async Task<ScopeRow> ValidateScope(System.Data.IDbConnection c, System.Data.IDbTransaction t, Guid plan, Guid season, Guid farm, Guid field, CancellationToken ct) => await c.QuerySingleOrDefaultAsync<ScopeRow>(new CommandDefinition("select s.start_date StartsOn,s.end_date EndsOn from agro360.agriculture_records r join agro360.agriculture_seasons s on s.tenant_id=r.tenant_id and s.id=@Season join agro360.geo_fields f on f.tenant_id=r.tenant_id and f.id=@Field and f.farm_id=@Farm where r.tenant_id=@TenantId and r.id=@Plan and r.module='plans' and r.deleted_at is null and s.farm_id=@Farm and s.deleted_at is null and nullif(r.data->>'cropSeasonId','')::uuid=@Season", new { tenant.TenantId, Plan = plan, Season = season, Farm = farm, Field = field }, t, cancellationToken: ct)) ?? throw new ConflictException("Plano, safra, propriedade e talhão não pertencem ao mesmo contexto autorizado.", "agriculture.scope_invalid");
    private static void ValidateOperation(DateTimeOffset start, DateTimeOffset end, decimal area) { if (end < start) throw new DomainException("A data final não pode ser anterior à inicial."); if (area <= 0) throw new DomainException("A área planejada deve ser positiva."); }
    private static PlanOperationDto Map(OperationRow x) => new(x.Id,x.PlanRecordId,x.SeasonId,x.FarmId,x.FieldId,x.Name,x.OperationType,x.PlannedStart,x.PlannedEnd,x.PlannedAreaHa,x.PlannedHours,x.ResponsibleId,x.Status,x.Version,x.ExecutedAreaHa,x.ScheduledAreaHa,Math.Max(0,x.PlannedAreaHa-x.ScheduledAreaHa),x.BlockReason,x.BlockUrl);
    private sealed record ScopeRow(DateOnly StartsOn, DateOnly EndsOn);
    private sealed record SeasonRow(Guid SeasonId,Guid FarmId,string Farm,string Season,string Crop,DateOnly StartsOn,DateOnly EndsOn,string Status,decimal PlannedPhysicalAreaHa);
    private sealed record OperationRow(Guid Id,Guid PlanRecordId,Guid SeasonId,Guid FarmId,Guid FieldId,string Name,string OperationType,DateTimeOffset PlannedStart,DateTimeOffset PlannedEnd,decimal PlannedAreaHa,decimal? PlannedHours,Guid? ResponsibleId,string Status,long Version,decimal ExecutedAreaHa,decimal ScheduledAreaHa,string? BlockReason,string? BlockUrl);
}
