using System.Text.Json;
using Agro360.Application.Contracts;
using Agro360.Domain.Agriculture;
using Agro360.Infrastructure.Persistence;
using Agro360.Infrastructure.Security;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Npgsql;

namespace Agro360.Infrastructure.Services;

public sealed class LookupService(DatabaseExecutor database, ITenantContext tenant) : ILookupService
{
    private static readonly Dictionary<string, string> Sources = new(StringComparer.OrdinalIgnoreCase)
    {
        // Escopo operacional canônico (OperationalScopePolicy): o contexto X-Farm-Id restringe e os unit scopes do usuário limitam a lista.
        ["properties"] = "select f.id,f.name label,concat_ws(' · ',f.municipality,f.state) description,'ACTIVE' status,'{}'::jsonb metadata from agro360.geo_farms f where f.tenant_id=@TenantId and f.deleted_at is null" + OperationalScopePolicy.FarmSql("f"),
        ["production-areas"] = "select g.id,g.name label,g.area_ha||' ha' description,'ACTIVE' status,jsonb_build_object('farmId',g.farm_id,'areaHa',g.area_ha) metadata from agro360.geo_fields g where g.tenant_id=@TenantId and g.deleted_at is null" + OperationalScopePolicy.FarmIdSql("g"),
        ["fields"] = "select f.id,f.name label,concat(f.area_ha,' ha · ',p.name) description,'ACTIVE' status,jsonb_build_object('propertyId',f.farm_id,'areaHa',f.area_ha) metadata from agro360.geo_fields f join agro360.geo_farms p on p.id=f.farm_id and p.tenant_id=f.tenant_id where f.tenant_id=@TenantId and f.deleted_at is null" + OperationalScopePolicy.FarmIdSql("f"),
        ["crops"] = "select id,name label,coalesce(description,'') description,case when active then 'ACTIVE' else 'INACTIVE' end status,'{}'::jsonb metadata from agro360.agriculture_crops where tenant_id=@TenantId",
        ["crop-seasons"] = "select s.id,s.name label,concat(s.crop,' · ',s.start_date,' a ',s.end_date) description,case when s.status in (1,2) then 'ACTIVE' else 'INACTIVE' end status,jsonb_build_object('propertyId',s.farm_id) metadata from agro360.agriculture_seasons s where s.tenant_id=@TenantId and s.deleted_at is null" + OperationalScopePolicy.FarmIdSql("s"),
        ["suppliers"] = "select id,name label,coalesce(document_number,'') description,status,'{}'::jsonb metadata from agro360.purchasing_suppliers where tenant_id=@TenantId and deleted_at is null",
        ["customers"] = "select id,name label,coalesce(tax_document,'') description,status,'{}'::jsonb metadata from agro360.crm_customers where tenant_id=@TenantId and deleted_at is null",
        ["purchase-orders"] = "select id,id::text label,concat(status,' · ',total) description,status,jsonb_build_object('supplierId',supplier_id,'total',total) metadata from agro360.purchasing_purchase_orders where tenant_id=@TenantId",
        ["inventory-items"] = "select id,name label,concat(sku,' · ',base_unit) description,'ACTIVE' status,jsonb_build_object('unit',base_unit) metadata from agro360.inventory_products where tenant_id=@TenantId and deleted_at is null",
        ["warehouses"] = "select w.id,w.name label,concat(w.code,' · ',w.type) description,'ACTIVE' status,jsonb_build_object('propertyId',w.farm_id) metadata from agro360.inventory_warehouses w where w.tenant_id=@TenantId and w.deleted_at is null" + OperationalScopePolicy.FarmIdSql("w"),
        ["quality-specifications"] = "select id,name label,concat('versão ',version,' · ',application_type) description,status,jsonb_build_object('productId',product_id,'version',version) metadata from agro360.quality_specifications where tenant_id=@TenantId and deleted_at is null",
        ["machines"] = "select a.id,a.name label,concat(coalesce(t.name,a.asset_type,''),' · ',coalesce(a.internal_code,a.code,'')) description,a.status,jsonb_build_object('cadastralStatus',a.cadastral_status) metadata from agro360.fleet_assets a left join agro360.fleet_asset_types t on t.id=a.asset_type_id and t.tenant_id=a.tenant_id where a.tenant_id=@TenantId and a.deleted_at is null and a.cadastral_status='ACTIVE' and a.status not in ('INACTIVE','WRITTEN_OFF','SOLD')",
        ["people"] = "select id,name label,email description,status,'{}'::jsonb metadata from agro360.identity_users where tenant_id=@TenantId and deleted_at is null",
        ["cost-centers"] = "select id,name label,concat(code,' · ',kind) description,case when active then 'ACTIVE' else 'INACTIVE' end status,'{}'::jsonb metadata from agro360.finance_cost_centers where tenant_id=@TenantId",
        ["livestock-batches"] = "select id,name label,concat(species,' · ',category,' · ',control_mode) description,status,jsonb_build_object('headCount',head_count,'controlMode',control_mode) metadata from agro360.livestock_herds where tenant_id=@TenantId and deleted_at is null",
        ["storage-lots"] = "select id,code label,concat(current_balance,' disponível') description,status,jsonb_build_object('productId',product_id,'balance',current_balance) metadata from agro360.storage_lots where tenant_id=@TenantId",
        ["routes"] = "select id,name label,concat(code,' · ',modal) description,'ACTIVE' status,jsonb_build_object('estimatedMinutes',estimated_minutes) metadata from agro360.regional_logistics_routes where tenant_id=@TenantId"
    };

    public Task<PagedResult<LookupItem>> SearchAsync(string resource, string? search, bool includeInactive, int page, int pageSize, CancellationToken cancellationToken)
    {
        if (!Sources.TryGetValue(resource, out var source)) throw new NotFoundException("Lookup", Guid.Empty);
        page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 50);
        var sql = $"select count(*) from ({source}) q where (@IncludeInactive or status='ACTIVE') and (@Search='' or label ilike '%'||@Search||'%' or description ilike '%'||@Search||'%'); select id,label,description,status,metadata::text Metadata from ({source}) q where (@IncludeInactive or status='ACTIVE') and (@Search='' or label ilike '%'||@Search||'%' or description ilike '%'||@Search||'%') order by label limit @PageSize offset @Offset;";
        return database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            using var grid = await connection.QueryMultipleAsync(new CommandDefinition(sql, new { tenant.TenantId, tenant.UserId, FarmId = tenant.FarmId, IncludeInactive = includeInactive, Search = search?.Trim() ?? "", PageSize = pageSize, Offset = (page - 1) * pageSize }, transaction, cancellationToken: cancellationToken));
            var total = await grid.ReadSingleAsync<long>();
            var rows = await grid.ReadAsync<LookupRow>();
            return new PagedResult<LookupItem>(rows.Select(x => new LookupItem(x.Id, x.Label, x.Description, x.Status.ToLowerInvariant(), JsonSerializer.Deserialize<Dictionary<string, object?>>(x.Metadata) ?? [])).ToArray(), page, pageSize, total);
        }, cancellationToken);
    }
    private sealed record LookupRow(Guid Id, string Label, string Description, string Status, string Metadata);
}

public sealed class Agriculture360Service(DatabaseExecutor database, ITenantContext tenant) : IAgriculture360Service
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] Modules = ["field-notes", "plans", "scouting", "recommendations", "applications", "irrigations", "weather-records", "work-orders"];

    public Task<PagedResult<AgricultureRecord>> ListAsync(string moduleCode, int page, int pageSize, CancellationToken cancellationToken)
    {
        EnsureModule(moduleCode); page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 100);
        return database.InTenantTransactionAsync(async (c, t) =>
        {
            using var grid = await c.QueryMultipleAsync(new CommandDefinition($"select count(*) from agro360.agriculture_records where tenant_id=@TenantId and module=@Module and deleted_at is null{RecordScope}; select id,module,status,created_at CreatedAt,data::text Data from agro360.agriculture_records where tenant_id=@TenantId and module=@Module and deleted_at is null{RecordScope} order by created_at desc limit @PageSize offset @Offset", new { tenant.TenantId, tenant.UserId, FarmId = tenant.FarmId, Module = moduleCode, PageSize = pageSize, Offset = (page - 1) * pageSize }, t, cancellationToken: cancellationToken));
            var total = await grid.ReadSingleAsync<long>(); var rows = await grid.ReadAsync<RecordRow>();
            return new PagedResult<AgricultureRecord>(rows.Select(Map).ToArray(), page, pageSize, total);
        }, cancellationToken);
    }

    public Task<AgricultureRecord> CreateAsync(string moduleCode, AgricultureCommand command, CancellationToken cancellationToken)
    {
        EnsureModule(moduleCode); Validate(moduleCode, command); var id = Guid.CreateVersion7(); var status = NormalizeStatus(command.Status, moduleCode);
        if (status is "APPROVED" or "CANCELLED" or "CLOSED" or "REVISION" || status == "COMPLETED" && moduleCode is not ("applications" or "field-notes")) throw new DomainException("Crie o registro em estado inicial; use as ações da operação para aprovar, concluir ou cancelar.", "agriculture.create_state_invalid");
        return database.InTenantTransactionAsync(async (c, t) =>
        {
            await ValidateReferences(c, t, command, cancellationToken);
            var data = ToData(command);
            if (moduleCode == "work-orders")
            {
                var sequence = await c.ExecuteScalarAsync<long>(new CommandDefinition("select nextval('agro360.field_work_order_number_seq')", transaction: t, cancellationToken: cancellationToken));
                data["number"] = $"OC-{DateTime.UtcNow:yyyy}-{sequence:000000}";
            }
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.agriculture_records(id,tenant_id,module,status,data,created_by) values(@Id,@TenantId,@Module,@Status,cast(@Data as jsonb),@UserId)", new { Id = id, tenant.TenantId, Module = moduleCode, Status = status, Data = JsonSerializer.Serialize(data, JsonOptions), tenant.UserId }, t, cancellationToken: cancellationToken));
            if (status == "COMPLETED") await ApplyMaterialEffectAsync(c, t, moduleCode, id, command.InventoryItemId, command.WarehouseId, command.Quantity, command.InventoryLot, cancellationToken);
            return new AgricultureRecord(id, moduleCode, status, DateTimeOffset.UtcNow, data);
        }, cancellationToken);
    }

    public Task<AgricultureRecord> UpdateAsync(string moduleCode, Guid id, AgricultureCommand command, CancellationToken cancellationToken)
    {
        EnsureModule(moduleCode); Validate(moduleCode, command);
        return database.InTenantTransactionAsync(async (c, t) =>
        {
            await ValidateReferences(c, t, command, cancellationToken);
            // Edição altera apenas o conteúdo; o estado muda exclusivamente pelas ações da operação (transições).
            var sql = moduleCode == "work-orders"
                ? "update agro360.agriculture_records set data=data||cast(@Data as jsonb),updated_at=now(),updated_by=@UserId,version=version+1 where id=@Id and tenant_id=@TenantId and module=@Module and deleted_at is null and status in ('OPEN','PLANNED','AWAITING_RESOURCES') and (@Version is null or version=@Version)"
                : "update agro360.agriculture_records set data=cast(@Data as jsonb),updated_at=now(),updated_by=@UserId,version=version+1 where id=@Id and tenant_id=@TenantId and module=@Module and deleted_at is null and status not in ('APPROVED','COMPLETED','CANCELLED','CLOSED') and (@Version is null or version=@Version)";
            var changed = await c.ExecuteAsync(new CommandDefinition(sql, new { Id = id, tenant.TenantId, Module = moduleCode, Data = JsonSerializer.Serialize(command, JsonOptions), tenant.UserId, command.Version }, t, cancellationToken: cancellationToken));
            if (changed == 0) throw new ConflictException("O registro mudou ou está bloqueado. Recarregue antes de editar.", "agriculture.version_conflict");
            var saved = await c.QuerySingleAsync<RecordRow>(new CommandDefinition("select id,module,status,created_at CreatedAt,data::text Data from agro360.agriculture_records where tenant_id=@TenantId and id=@Id", new { tenant.TenantId, Id = id }, t, cancellationToken: cancellationToken));
            return Map(saved);
        }, cancellationToken);
    }

    public Task<AgricultureRecord> TransitionAsync(string moduleCode, Guid id, string action, AgricultureCommand? command, CancellationToken cancellationToken)
    {
        EnsureModule(moduleCode); var next = action.ToLowerInvariant() switch { "approve" => moduleCode == "work-orders" ? "RELEASED" : "APPROVED", "revise" => moduleCode == "work-orders" ? "PLANNED" : "REVISION", "start" => "IN_PROGRESS", "pause" => "PAUSED", "complete" => moduleCode == "work-orders" ? "AWAITING_REVIEW" : "COMPLETED", "cancel" => "CANCELLED", _ => throw new DomainException("Transição inválida.", "agro360.agriculture_transition_invalid") };
        if (moduleCode == "work-orders" && command?.Version is null) throw new DomainException("A versão da ordem é obrigatória. Recarregue os dados antes de executar a ação.", "agriculture.version_required");
        if (next == "COMPLETED" && moduleCode == "work-orders" && (command?.ResponsibleId is null || command.ChecklistRequired && !command.ChecklistCompleted)) throw new DomainException("Responsável e checklist obrigatório concluído são necessários.", "agro360.agriculture_work_order_incomplete");
        if (next == "CANCELLED" && string.IsNullOrWhiteSpace(command?.CancellationReason)) throw new DomainException("Informe o motivo do cancelamento.", "agro360.agriculture_cancellation_reason_required");
        return database.InTenantTransactionAsync(async (c, t) =>
        {
            var current = await c.ExecuteScalarAsync<string?>(new CommandDefinition("select status from agro360.agriculture_records where id=@Id and tenant_id=@TenantId and module=@Module and deleted_at is null for update", new { Id = id, tenant.TenantId, Module = moduleCode }, t, cancellationToken: cancellationToken));
            if (current is null) throw new NotFoundException("Registro agrícola", id);
            if (moduleCode == "work-orders")
            {
                var allowed = (current, next) is ("OPEN", "RELEASED") or ("PLANNED", "RELEASED") or ("AWAITING_RESOURCES", "RELEASED") or ("RELEASED", "IN_PROGRESS") or ("PAUSED", "IN_PROGRESS") or ("IN_PROGRESS", "PAUSED") or ("IN_PROGRESS", "AWAITING_REVIEW") or ("PAUSED", "AWAITING_REVIEW") || next == "CANCELLED" && current is not ("COMPLETED" or "CANCELLED");
                if (!allowed) throw new ConflictException($"A transição de {current} para {next} não é permitida.", "agriculture.transition_conflict");
                if (next == "PAUSED" && string.IsNullOrWhiteSpace(command?.PauseReason)) throw new DomainException("Informe o motivo da pausa.", "agriculture.pause_reason_required");
                if (next == "RELEASED")
                {
                    var ready = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select coalesce(nullif(data->>'propertyId',''),'')<>'' and coalesce(nullif(data->>'fieldId',''),'')<>'' and coalesce(nullif(data->>'responsibleId',''),'')<>'' and coalesce((data->>'plannedAt')::timestamptz,now())>=now()-interval '1 day' from agro360.agriculture_records where tenant_id=@TenantId and id=@Id", new { tenant.TenantId, Id = id }, t, cancellationToken: cancellationToken));
                    if (!ready) throw new ConflictException("Complete propriedade, talhão, responsável e programação antes de liberar.", "agriculture.release_prerequisite");
                    var dependency = await c.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition("""
                        select p.id,p.name,p.status from agro360.agriculture_operation_orders l
                        join agro360.agriculture_operation_dependencies d on d.tenant_id=l.tenant_id and d.operation_id=l.operation_id and d.blocking_type='REQUIRED' and d.deleted_at is null and d.exception_authorized_at is null
                        join agro360.agriculture_plan_operations p on p.tenant_id=d.tenant_id and p.id=d.predecessor_id
                        where l.tenant_id=@TenantId and l.work_order_id=@Id and p.status<>'COMPLETED' order by p.planned_start limit 1
                        """, new { tenant.TenantId, Id = id }, t, cancellationToken: cancellationToken));
                    if (dependency is not null) throw new ConflictException($"A predecessora {dependency.name} ({dependency.id}) está {dependency.status}. Abra /Agriculture?operationId={dependency.id}.", "agriculture.dependency_blocked");
                }
            }
            else
            {
                // Máquina mínima dos módulos genéricos: terminais são irreversíveis e o fluxo só avança em ordem.
                var allowedGeneric = (current, next) switch
                {
                    (_, "CANCELLED") => current is not ("COMPLETED" or "CANCELLED" or "CLOSED"),
                    (_, "IN_PROGRESS") => current is "OPEN" or "PLANNED" or "RELEASED" or "PAUSED",
                    (_, "PAUSED") => current == "IN_PROGRESS",
                    (_, "COMPLETED") => current is "OPEN" or "PLANNED" or "RELEASED" or "AWAITING_RESOURCES" or "IN_PROGRESS" or "PAUSED" or "AWAITING_REVIEW",
                    (_, "APPROVED") => current is "OPEN" or "PLANNED" or "REVISION",
                    (_, "REVISION") => current == "APPROVED",
                    (_, "CLOSED") => current is "COMPLETED" or "APPROVED",
                    _ => false
                };
                if (!allowedGeneric) throw new ConflictException($"A transição de {current} para {next} não é permitida.", "agriculture.transition_conflict");
            }
            var reason = next switch { "CANCELLED" => command?.CancellationReason, "PAUSED" => command?.PauseReason, _ => command?.Notes };
            var changed = await c.QuerySingleOrDefaultAsync<RecordRow>(new CommandDefinition("""
                update agro360.agriculture_records
                   set status=@Status,
                       data=case
                         when @Status='IN_PROGRESS' and coalesce(data->>'actualStartedAt','')='' then jsonb_set(data,'{actualStartedAt}',to_jsonb(now()),true)
                         else data
                       end,
                       updated_at=now(),updated_by=@UserId,version=version+1
                 where id=@Id and tenant_id=@TenantId and module=@Module
                   and (@Version is null or version=@Version)
                returning id,module,status,created_at CreatedAt,data::text Data
                """, new { Status = next, tenant.UserId, Id = id, tenant.TenantId, Module = moduleCode, Version = moduleCode == "work-orders" ? command?.Version : null }, t, cancellationToken: cancellationToken));
            if (changed is null) throw new ConflictException("A ordem foi alterada por outro usuário. Recarregue antes de executar a ação.", "agriculture.version_conflict");
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.agriculture_status_history(id,tenant_id,record_id,from_status,to_status,reason,changed_by) values(gen_random_uuid(),@TenantId,@Id,@FromStatus,@Status,@Reason,@UserId)", new { Status = next, FromStatus = current, Reason = reason, tenant.UserId, Id = id, tenant.TenantId }, t, cancellationToken: cancellationToken));
            if (next is "CANCELLED" or "COMPLETED") await c.ExecuteAsync(new CommandDefinition("update agro360.field_work_order_resources set status='RELEASED',updated_at=now(),updated_by=@UserId,version=version+1 where tenant_id=@TenantId and work_order_id=@Id and status='RESERVED'", new { tenant.UserId, tenant.TenantId, Id = id }, t, cancellationToken: cancellationToken));
            // Mesma rotina do create-as-COMPLETED: concluir por transição baixa o material uma única vez (chave idempotente por registro).
            if (next == "COMPLETED" && moduleCode is "applications" or "field-notes")
            {
                var completed = JsonSerializer.Deserialize<Dictionary<string, object?>>(changed.Data, JsonOptions);
                await ApplyMaterialEffectAsync(c, t, moduleCode, id, DataGuid(completed, "inventoryItemId"), DataGuid(completed, "warehouseId"), DataDecimal(completed, "quantity"), DataString(completed, "inventoryLot"), cancellationToken);
            }
            return Map(changed);
        }, cancellationToken);
    }

    public Task<AgricultureDashboard> DashboardAsync(CancellationToken cancellationToken) => database.InTenantTransactionAsync(async (c, t) =>
    {
        // Regra das métricas: planejado x realizado x não calculável. Nulo indica "sem dados compatíveis" (jamais zero);
        // custo reconhecido vem de cost_management_entries; área efetiva de ordens executadas (conferidas);
        // totais físicos agrupados por unidade base do produto (nunca misturam unidades diferentes).
        var seasonScope = OperationalScopePolicy.FarmIdSql("s");
        var core = await c.QuerySingleAsync<DashboardCore>(new CommandDefinition($"""
            select
             (select count(*) from agro360.agriculture_seasons s where s.tenant_id=@TenantId and s.status=2{seasonScope}) ActiveSeasons,
             coalesce((select sum(s.planned_area_ha) from agro360.agriculture_seasons s where s.tenant_id=@TenantId and s.status=2{seasonScope}),0) PlannedAreaHa,
             count(*) filter(where module='field-notes' and status='PLANNED') PlannedActivities,
             count(*) filter(where module='field-notes' and status='PLANNED' and (data->>'plannedAt')::timestamptz<now()) OverdueActivities,
             count(*) filter(where module='field-notes' and status='COMPLETED' and updated_at>=date_trunc('month',now())) CompletedThisMonth,
             sum((data->>'estimatedCost')::numeric) filter(where status not in ('CANCELLED','CLOSED')) PlannedCost,
             (select sum(e.recognized_amount) from agro360.cost_management_entries e where e.tenant_id=@TenantId and (@FarmId::uuid is null or e.farm_id=@FarmId)) RecognizedCost,
              (select sum(e.recognized_amount-e.reversed_amount) from agro360.cost_management_entries e where e.tenant_id=@TenantId and (@FarmId::uuid is null or e.farm_id=@FarmId)) RecognizedNetCost,
             coalesce((select sum(op.covered_area_ha) from agro360.agriculture_operation_orders op
                       join agro360.agriculture_plan_operations po on po.tenant_id=op.tenant_id and po.id=op.operation_id
                       join agro360.agriculture_records wo on wo.tenant_id=op.tenant_id and wo.id=op.work_order_id
                      where op.tenant_id=@TenantId and po.deleted_at is null and wo.deleted_at is null and wo.status='COMPLETED' and (@FarmId::uuid is null or po.farm_id=@FarmId)),0) ExecutedAreaHa,
             case when (select count(distinct s.crop) from agro360.agriculture_seasons s where s.tenant_id=@TenantId and s.status=2{seasonScope})=1
                  then (select sum(s.expected_yield_per_ha * s.planned_area_ha) from agro360.agriculture_seasons s where s.tenant_id=@TenantId and s.status=2{seasonScope}) end ExpectedProduction,
             case when (select count(distinct lower(pr.unit)) from agro360.production_receipts pr
                        join agro360.harvest_records hr on hr.tenant_id=pr.tenant_id and hr.id=pr.harvest_record_id
                        join agro360.harvest_plans hp on hp.tenant_id=hr.tenant_id and hp.id=hr.plan_id
                       where pr.tenant_id=@TenantId and (@FarmId::uuid is null or hp.farm_id=@FarmId))=1
                  then (select sum(pr.received_quantity) from agro360.production_receipts pr
                        join agro360.harvest_records hr on hr.tenant_id=pr.tenant_id and hr.id=pr.harvest_record_id
                        join agro360.harvest_plans hp on hp.tenant_id=hr.tenant_id and hp.id=hr.plan_id
                       where pr.tenant_id=@TenantId and (@FarmId::uuid is null or hp.farm_id=@FarmId)) end ActualProduction,
             count(*) filter(where module='scouting' and status not in('CLOSED','CANCELLED')) OpenOccurrences,
             count(*) filter(where module='scouting' and data->>'severity'='CRITICAL' and status not in('CLOSED','CANCELLED')) CriticalOccurrences,
             count(*) filter(where module='applications' and status='COMPLETED') Applications,
             count(*) filter(where module='irrigations' and status='COMPLETED') Irrigations
            from agro360.agriculture_records where tenant_id=@TenantId and deleted_at is null{RecordScope}
            """, new { tenant.TenantId, tenant.UserId, FarmId = tenant.FarmId }, t, cancellationToken: cancellationToken));
        var inputs = (await c.QueryAsync<AgricultureInputUnit>(new CommandDefinition($"""
            select coalesce(nullif(p.base_unit,''),'un') Unit,
             coalesce(sum((r.data->>'quantity')::numeric) filter(where r.module='plans' and r.status not in ('CANCELLED','CLOSED')),0) Planned,
             coalesce(sum((r.data->>'quantity')::numeric) filter(where r.module='applications' and r.status='COMPLETED'),0) Consumed
            from agro360.agriculture_records r
            left join agro360.inventory_products p on p.tenant_id=r.tenant_id and p.id=nullif(r.data->>'inventoryItemId','')::uuid
            where r.tenant_id=@TenantId and r.deleted_at is null and r.module in ('plans','applications'){RecordScope}
              and nullif(r.data->>'inventoryItemId','') is not null
            group by 1 order by 1
            """, new { tenant.TenantId, tenant.UserId, FarmId = tenant.FarmId }, t, cancellationToken: cancellationToken))).ToList();
        var alerts = new List<string>();
        if (core.CriticalOccurrences > 0) alerts.Add("Ocorrência fitossanitária crítica em andamento");
        if (core.OverdueActivities > 0) alerts.Add("Atividades agrícolas atrasadas");
        if (!core.RecognizedCost.HasValue) alerts.Add("Nenhum custo reconhecido ainda: realizado indisponível");
        return new AgricultureDashboard(
            core.ActiveSeasons, core.PlannedAreaHa, core.PlannedActivities, core.OverdueActivities, core.CompletedThisMonth,
            core.PlannedCost, core.RecognizedCost,
            core.RecognizedCost.HasValue && core.ExecutedAreaHa > 0 ? Agro360.Domain.Finance.SeasonCostRules.RoundMoney(core.RecognizedCost.Value / core.ExecutedAreaHa) : null,
            core.OpenOccurrences, core.CriticalOccurrences, inputs,
            core.Applications, core.Irrigations, core.ExpectedProduction, core.ActualProduction, alerts, core.RecognizedNetCost);
    }, cancellationToken);

    private static void Validate(string module, AgricultureCommand x) { Agriculture360Rules.Period(module == "work-orders" ? x.PlannedAt : x.StartedAt, x.FinishedAt); if (module is "field-notes" or "plans" or "scouting" or "recommendations" or "applications" or "irrigations" or "work-orders") Agriculture360Rules.Required(x.PropertyId, "Propriedade"); if (module == "field-notes") Agriculture360Rules.Required(x.CropSeasonId, "Safra"); if (module is "field-notes" or "scouting" or "applications" or "irrigations" or "work-orders") Agriculture360Rules.Required(x.FieldId, "Talhão"); if (module == "plans") { Agriculture360Rules.Required(x.CropSeasonId, "Safra"); Agriculture360Rules.Required(x.CropId, "Cultura"); Agriculture360Rules.UniqueFields(x.FieldIds); } if (module == "applications") { Agriculture360Rules.Required(x.InventoryItemId, "Produto"); Agriculture360Rules.Positive(x.Dose, "Dose"); } if (module == "irrigations") Agriculture360Rules.Positive(x.Area, "Área"); if (module == "weather-records") { Agriculture360Rules.Required(x.PropertyId, "Propriedade"); Agriculture360Rules.Weather(x.Rainfall, x.Temperature, x.Humidity, x.Wind); } if (module == "work-orders" && x.Status == "COMPLETED") Agriculture360Rules.Required(x.ResponsibleId, "Responsável"); if (module == "scouting" && !new[] { "LOW", "MEDIUM", "HIGH", "CRITICAL" }.Contains(x.Severity)) throw new DomainException("Selecione uma severidade válida.", "agro360.agriculture_severity_invalid"); }
    private static string NormalizeStatus(string? status, string module)
    {
        var value = (status ?? (module == "work-orders" ? "OPEN" : "PLANNED")).Trim().ToUpperInvariant();
        if (!new[] { "OPEN", "PLANNED", "AWAITING_RESOURCES", "RELEASED", "IN_PROGRESS", "PAUSED", "AWAITING_REVIEW", "COMPLETED", "CANCELLED", "APPROVED", "REVISION", "CLOSED" }.Contains(value)) throw new DomainException("Status inválido.", "agro360.agriculture_status_invalid");
        return value;
    }
    private sealed class DashboardCore
    {
        public long ActiveSeasons { get; init; }
        public decimal PlannedAreaHa { get; init; }
        public long PlannedActivities { get; init; }
        public long OverdueActivities { get; init; }
        public long CompletedThisMonth { get; init; }
        public decimal? PlannedCost { get; init; }
        public decimal? RecognizedCost { get; init; }
        public decimal? RecognizedNetCost { get; init; }
        public decimal ExecutedAreaHa { get; init; }
        public decimal? ExpectedProduction { get; init; }
        public decimal? ActualProduction { get; init; }
        public long OpenOccurrences { get; init; }
        public long CriticalOccurrences { get; init; }
        public long Applications { get; init; }
        public long Irrigations { get; init; }
    }
    private static void EnsureModule(string module) { if (!Modules.Contains(module, StringComparer.OrdinalIgnoreCase)) throw new NotFoundException("Módulo agrícola", Guid.Empty); }
    // Escopo de unidade sobre registros polimórficos: o contexto X-Farm-Id restringe e o unit scope do usuário limita.
    private const string RecordScope = " " + """
        and (
            exists(select 1 from agro360.identity_user_unit_scopes sc where sc.tenant_id=tenant_id and sc.user_id=@UserId and sc.scope_type='ALL')
            or exists(select 1 from agro360.geo_farms gf where gf.tenant_id=tenant_id and gf.id=nullif(data->>'propertyId','')::uuid and gf.deleted_at is null
              and (@FarmId::uuid is null or gf.id=@FarmId)
              and (exists(select 1 from agro360.identity_user_unit_scopes scd where scd.tenant_id=tenant_id and scd.user_id=@UserId and scd.scope_type='FARM' and scd.farm_id=gf.id)
                   or exists(select 1 from agro360.identity_user_unit_scopes sco where sco.tenant_id=tenant_id and sco.user_id=@UserId and sco.scope_type='ORGANIZATION' and sco.organization_id=gf.organization_id)))
        )
        """;
    private async Task ValidateReferences(System.Data.IDbConnection c, System.Data.IDbTransaction t, AgricultureCommand x, CancellationToken ct) { var valid = await c.ExecuteScalarAsync<bool>(new CommandDefinition("select (@PropertyId is null or exists(select 1 from agro360.geo_farms where id=@PropertyId and tenant_id=@TenantId and deleted_at is null)) and (@FieldId is null or exists(select 1 from agro360.geo_fields where id=@FieldId and tenant_id=@TenantId and farm_id=@PropertyId and deleted_at is null)) and (@ResponsibleId is null or exists(select 1 from agro360.identity_users where id=@ResponsibleId and tenant_id=@TenantId and status='ACTIVE')) and (@InventoryItemId is null or exists(select 1 from agro360.inventory_products where id=@InventoryItemId and tenant_id=@TenantId and deleted_at is null)) and (@CropSeasonId is null or exists(select 1 from agro360.agriculture_seasons where id=@CropSeasonId and tenant_id=@TenantId and farm_id=@PropertyId and deleted_at is null)) and (@CropId is null or exists(select 1 from agro360.agriculture_crops where id=@CropId and tenant_id=@TenantId and active)) and (@MachineId is null or exists(select 1 from agro360.fleet_assets where id=@MachineId and tenant_id=@TenantId and status not in ('SOLD','INACTIVE'))) and (@CostCenterId is null or exists(select 1 from agro360.finance_cost_centers where id=@CostCenterId and tenant_id=@TenantId and active)) and (@WarehouseId is null or exists(select 1 from agro360.inventory_warehouses w where w.id=@WarehouseId and w.tenant_id=@TenantId and w.deleted_at is null and (@PropertyId is null or w.farm_id=@PropertyId))) and (@PropertyId is null or exists(select 1 from agro360.geo_farms scope_farm where scope_farm.tenant_id=@TenantId and scope_farm.id=@PropertyId and scope_farm.deleted_at is null and (@FarmId::uuid is null or scope_farm.id=@FarmId) and (exists(select 1 from agro360.identity_user_unit_scopes sc where sc.tenant_id=@TenantId and sc.user_id=@UserId and sc.scope_type='ALL') or exists(select 1 from agro360.identity_user_unit_scopes sc where sc.tenant_id=@TenantId and sc.user_id=@UserId and sc.scope_type='FARM' and sc.farm_id=scope_farm.id) or exists(select 1 from agro360.identity_user_unit_scopes sc where sc.tenant_id=@TenantId and sc.user_id=@UserId and sc.scope_type='ORGANIZATION' and sc.organization_id=scope_farm.organization_id)))) and (@FieldIds is null or not exists(select 1 from unnest(@FieldIds::uuid[]) fid where not exists(select 1 from agro360.geo_fields gf2 where gf2.tenant_id=@TenantId and gf2.id=fid and gf2.deleted_at is null and (@PropertyId is null or gf2.farm_id=@PropertyId))))", new { x.PropertyId, x.FieldId, x.ResponsibleId, x.InventoryItemId, x.CropSeasonId, x.CropId, x.MachineId, x.CostCenterId, WarehouseId = x.WarehouseId, FieldIds = x.FieldIds?.Cast<Guid>().ToArray(), tenant.TenantId, tenant.UserId, FarmId = tenant.FarmId }, t, cancellationToken: ct)); if (!valid) throw new DomainException("Uma seleção não pertence ao tenant, à unidade autorizada ou está inativa.", "agro360.agriculture_lookup_invalid"); }
    // Baixa canônica do material consumido por uma aplicação/caderno concluído: um único efeito por registro,
    // sempre no depósito de origem, via inventory_apply_stock_movement (ledger + lote + custo médio + trava de contagem).
    private async Task ApplyMaterialEffectAsync(System.Data.IDbConnection c, System.Data.IDbTransaction t, string module, Guid id, Guid? productId, Guid? warehouseId, decimal? quantity, string? lot, CancellationToken ct)
    {
        if (module is not ("applications" or "field-notes") || productId is null || quantity is null) return;
        Agriculture360Rules.Positive(quantity.Value, "Quantidade");
        if (warehouseId is null) throw new DomainException("Informe o depósito de origem do material consumido.", "agriculture.warehouse_required");
        var key = $"agri:{id:N}";
        var existing = await c.QuerySingleOrDefaultAsync<dynamic>(new CommandDefinition("select product_id ProductId, warehouse_id WarehouseId, quantity Quantity from agro360.inventory_stock_movements where tenant_id=@TenantId and idempotency_key=@Key", new { tenant.TenantId, Key = key }, t, cancellationToken: ct));
        if (existing is not null)
        {
            var same = existing.ProductId.ToString() == productId.ToString() && existing.WarehouseId.ToString() == warehouseId.ToString() && Convert.ToDecimal(existing.Quantity) == quantity.Value;
            if (!same) throw new ConflictException("O material desta operação já foi baixado com parâmetros diferentes; não é possível reenviar com alterações.", "agriculture.material_effect_conflict");
            return;
        }
        try
        {
            var movementId = await c.ExecuteScalarAsync<Guid>(new CommandDefinition("select agro360.inventory_apply_stock_movement(@TenantId,@WarehouseId,@ProductId,-@Quantity,0,'EXIT',@RecordId,@Lot,null,@UserId,@Reason)", new { tenant.TenantId, WarehouseId = warehouseId.Value, ProductId = productId.Value, quantity.Value, RecordId = id, Lot = string.IsNullOrWhiteSpace(lot) ? null : lot.Trim(), tenant.UserId, Reason = $"Conclusão de {module} {id}" }, t, cancellationToken: ct));
            await c.ExecuteAsync(new CommandDefinition("update agro360.inventory_stock_movements set idempotency_key=@Key where tenant_id=@TenantId and id=@Movement", new { tenant.TenantId, Key = key, Movement = movementId }, t, cancellationToken: ct));
        }
        catch (PostgresException ex) when (ex.MessageText is "insufficient stock" or "insufficient stock in lot")
        {
            throw new ConflictException("Estoque insuficiente no depósito selecionado para esta quantidade.", "agro360.agriculture_stock_unavailable");
        }
        catch (PostgresException ex) when (ex.MessageText == "movement would consume reserved stock")
        {
            throw new ConflictException("A baixa consome estoque reservado; libere as reservas antes de concluir a operação.", "inventory.reservation_shortage");
        }
        catch (PostgresException ex) when (ex.MessageText == "physical count blocks movements in this scope")
        {
            throw new ConflictException("Uma contagem física em andamento bloqueia a movimentação neste depósito.", "count.movement_blocked");
        }
        catch (PostgresException ex) when (ex.MessageText == "lot is required for this product")
        {
            throw new DomainException("Este produto exige lote para movimentação de estoque.", "agriculture.lot_required");
        }
    }
    private static Guid? DataGuid(Dictionary<string, object?>? d, string key) => d is null || !d.TryGetValue(key, out var v) || v is null ? null : Guid.TryParse(DataText(v), out var g) ? g : null;
    private static decimal? DataDecimal(Dictionary<string, object?>? d, string key) => d is null || !d.TryGetValue(key, out var v) || v is null ? null : decimal.TryParse(DataText(v), System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
    private static string? DataString(Dictionary<string, object?>? d, string key) => d is null || !d.TryGetValue(key, out var v) || v is null ? null : DataText(v);
    private static string DataText(object v) => v is JsonElement el ? (el.ValueKind == JsonValueKind.String ? el.GetString() ?? "" : el.GetRawText()) : Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? "";
    private static AgricultureRecord Map(RecordRow x) => new(x.Id, x.Module, x.Status, x.CreatedAt, JsonSerializer.Deserialize<Dictionary<string, object?>>(x.Data, JsonOptions) ?? []);
    private static Dictionary<string, object?> ToData(AgricultureCommand x) => JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(x, JsonOptions), JsonOptions) ?? [];
    private sealed class RecordRow
    {
        public Guid Id { get; init; }
        public string Module { get; init; } = default!;
        public string Status { get; init; } = default!;
        public DateTimeOffset CreatedAt { get; init; }
        public string Data { get; init; } = default!;
    }
}
