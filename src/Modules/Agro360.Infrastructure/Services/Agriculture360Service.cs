using System.Text.Json;
using Agro360.Application.Contracts;
using Agro360.Domain.Agriculture;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;

namespace Agro360.Infrastructure.Services;

public sealed class LookupService(DatabaseExecutor database, ITenantContext tenant) : ILookupService
{
    private static readonly IReadOnlyDictionary<string, string> Sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["properties"] = "select id,name label,concat_ws(' · ',municipality,state) description,'ACTIVE' status,'{}'::jsonb metadata from agro360.geo_farms where tenant_id=@TenantId and deleted_at is null",
        ["production-areas"] = "select id,name label,area_ha||' ha' description,'ACTIVE' status,jsonb_build_object('farmId',farm_id,'areaHa',area_ha) metadata from agro360.geo_fields where tenant_id=@TenantId and deleted_at is null",
        ["fields"] = "select f.id,f.name label,concat(f.area_ha,' ha · ',p.name) description,'ACTIVE' status,jsonb_build_object('propertyId',f.farm_id,'areaHa',f.area_ha) metadata from agro360.geo_fields f join agro360.geo_farms p on p.id=f.farm_id and p.tenant_id=f.tenant_id where f.tenant_id=@TenantId and f.deleted_at is null",
        ["crops"] = "select id,name label,coalesce(description,'') description,case when active then 'ACTIVE' else 'INACTIVE' end status,'{}'::jsonb metadata from agro360.agriculture_crops where tenant_id=@TenantId",
        ["crop-seasons"] = "select id,name label,concat(crop,' · ',start_date,' a ',end_date) description,case when status in (1,2) then 'ACTIVE' else 'INACTIVE' end status,jsonb_build_object('propertyId',farm_id) metadata from agro360.agriculture_seasons where tenant_id=@TenantId and deleted_at is null",
        ["suppliers"] = "select id,name label,coalesce(document_number,'') description,status,'{}'::jsonb metadata from agro360.purchasing_suppliers where tenant_id=@TenantId and deleted_at is null",
        ["customers"] = "select id,name label,coalesce(tax_document,'') description,status,'{}'::jsonb metadata from agro360.crm_customers where tenant_id=@TenantId and deleted_at is null",
        ["purchase-orders"] = "select id,id::text label,concat(status,' · ',total) description,status,jsonb_build_object('supplierId',supplier_id,'total',total) metadata from agro360.purchasing_purchase_orders where tenant_id=@TenantId",
        ["inventory-items"] = "select id,name label,concat(sku,' · ',base_unit) description,'ACTIVE' status,jsonb_build_object('unit',base_unit) metadata from agro360.inventory_products where tenant_id=@TenantId and deleted_at is null",
        ["machines"] = "select id,name label,concat(asset_type,' · ',code) description,status,'{}'::jsonb metadata from agro360.fleet_assets where tenant_id=@TenantId",
        ["people"] = "select id,name label,email description,status,'{}'::jsonb metadata from agro360.identity_users where tenant_id=@TenantId and deleted_at is null",
        ["cost-centers"] = "select id,name label,concat(code,' · ',kind) description,case when active then 'ACTIVE' else 'INACTIVE' end status,'{}'::jsonb metadata from agro360.finance_cost_centers where tenant_id=@TenantId",
        ["livestock-batches"] = "select id,name label,concat(species,' · ',category) description,case when active then 'ACTIVE' else 'INACTIVE' end status,jsonb_build_object('headCount',head_count) metadata from agro360.livestock_herds where tenant_id=@TenantId",
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
            using var grid = await connection.QueryMultipleAsync(new CommandDefinition(sql, new { tenant.TenantId, IncludeInactive = includeInactive, Search = search?.Trim() ?? "", PageSize = pageSize, Offset = (page - 1) * pageSize }, transaction, cancellationToken: cancellationToken));
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

    public Task<PagedResult<AgricultureRecord>> ListAsync(string module, int page, int pageSize, CancellationToken cancellationToken)
    {
        EnsureModule(module); page = Math.Max(page, 1); pageSize = Math.Clamp(pageSize, 1, 100);
        return database.InTenantTransactionAsync(async (c, t) =>
        {
            using var grid = await c.QueryMultipleAsync(new CommandDefinition("select count(*) from agro360.agriculture_records where tenant_id=@TenantId and module=@Module and deleted_at is null; select id,module,status,created_at CreatedAt,data::text Data from agro360.agriculture_records where tenant_id=@TenantId and module=@Module and deleted_at is null order by created_at desc limit @PageSize offset @Offset", new { tenant.TenantId, Module = module, PageSize = pageSize, Offset = (page - 1) * pageSize }, t, cancellationToken: cancellationToken));
            var total = await grid.ReadSingleAsync<long>(); var rows = await grid.ReadAsync<RecordRow>();
            return new PagedResult<AgricultureRecord>(rows.Select(Map).ToArray(), page, pageSize, total);
        }, cancellationToken);
    }

    public Task<AgricultureRecord> CreateAsync(string module, AgricultureCommand command, CancellationToken cancellationToken)
    {
        EnsureModule(module); Validate(module, command); var id = Guid.CreateVersion7(); var status = NormalizeStatus(command.Status, module);
        return database.InTenantTransactionAsync(async (c, t) =>
        {
            await ValidateReferences(c, t, command, cancellationToken);
            await c.ExecuteAsync(new CommandDefinition("insert into agro360.agriculture_records(id,tenant_id,module,status,data,created_by) values(@Id,@TenantId,@Module,@Status,cast(@Data as jsonb),@UserId)", new { Id=id, tenant.TenantId, Module=module, Status=status, Data=JsonSerializer.Serialize(command, JsonOptions), tenant.UserId }, t, cancellationToken:cancellationToken));
            await ApplyEffects(c, t, module, id, command, status, cancellationToken);
            return new AgricultureRecord(id,module,status,DateTimeOffset.UtcNow,ToData(command));
        }, cancellationToken);
    }

    public Task<AgricultureRecord> UpdateAsync(string module, Guid id, AgricultureCommand command, CancellationToken cancellationToken)
    {
        EnsureModule(module); Validate(module, command); var status=NormalizeStatus(command.Status,module);
        return database.InTenantTransactionAsync(async (c,t) => { await ValidateReferences(c,t,command,cancellationToken); var changed=await c.ExecuteAsync(new CommandDefinition("update agro360.agriculture_records set status=@Status,data=cast(@Data as jsonb),updated_at=now(),updated_by=@UserId,version=version+1 where id=@Id and tenant_id=@TenantId and module=@Module and deleted_at is null and status not in ('APPROVED','COMPLETED','CANCELLED')",new {Id=id,tenant.TenantId,Module=module,Status=status,Data=JsonSerializer.Serialize(command, JsonOptions),tenant.UserId},t,cancellationToken:cancellationToken)); if(changed==0) throw new ConflictException("Registro inexistente ou bloqueado pelo estado atual."); return new AgricultureRecord(id,module,status,DateTimeOffset.UtcNow,ToData(command)); },cancellationToken);
    }

    public Task<AgricultureRecord> TransitionAsync(string module, Guid id, string action, AgricultureCommand? command, CancellationToken cancellationToken)
    {
        EnsureModule(module); var next=action.ToLowerInvariant() switch {"approve"=>"APPROVED","revise"=>"REVISION","start"=>"IN_PROGRESS","pause"=>"PAUSED","complete"=>"COMPLETED","cancel"=>"CANCELLED",_=>throw new DomainException("Transição inválida.", "agro360.agriculture_transition_invalid")};
        if(next=="COMPLETED" && module=="work-orders" && (command?.ResponsibleId is null || command.ChecklistRequired && !command.ChecklistCompleted)) throw new DomainException("Responsável e checklist obrigatório concluído são necessários.","agro360.agriculture_work_order_incomplete");
        if(next=="CANCELLED" && string.IsNullOrWhiteSpace(command?.CancellationReason)) throw new DomainException("Informe o motivo do cancelamento.","agro360.agriculture_cancellation_reason_required");
        return database.InTenantTransactionAsync(async(c,t)=>{var row=await c.QuerySingleOrDefaultAsync<RecordRow>(new CommandDefinition("update agro360.agriculture_records set status=@Status,updated_at=now(),updated_by=@UserId,version=version+1 where id=@Id and tenant_id=@TenantId and module=@Module and deleted_at is null returning id,module,status,created_at CreatedAt,data::text Data",new{Status=next,tenant.UserId,Id=id,tenant.TenantId,Module=module},t,cancellationToken:cancellationToken)); if(row is null) throw new NotFoundException("Registro agrícola", id); await c.ExecuteAsync(new CommandDefinition("insert into agro360.agriculture_status_history(id,tenant_id,record_id,from_status,to_status,reason,changed_by) select gen_random_uuid(),tenant_id,id,status,@Status,@Reason,@UserId from agro360.agriculture_records where id=@Id and tenant_id=@TenantId",new{Status=next,Reason=command?.CancellationReason,tenant.UserId,Id=id,tenant.TenantId},t,cancellationToken:cancellationToken)); return Map(row);},cancellationToken);
    }

    public Task<AgricultureDashboard> DashboardAsync(CancellationToken cancellationToken) => database.InTenantTransactionAsync(async(c,t)=> await c.QuerySingleAsync<AgricultureDashboard>(new CommandDefinition("select (select count(*) from agro360.agriculture_seasons where tenant_id=@TenantId and status=2) ActiveSeasons,coalesce((select sum(planned_area_ha) from agro360.agriculture_seasons where tenant_id=@TenantId and status=2),0) PlantedArea,count(*) filter(where module='field-notes' and status='PLANNED') PlannedActivities,count(*) filter(where module='field-notes' and status='PLANNED' and (data->>'plannedAt')::timestamptz<now()) OverdueActivities,count(*) filter(where module='field-notes' and status='COMPLETED' and updated_at>=date_trunc('month',now())) CompletedThisMonth,coalesce(sum((data->>'estimatedCost')::numeric),0) PlannedCost,coalesce(sum(case when status='COMPLETED' then (data->>'estimatedCost')::numeric else 0 end),0) ActualCost,case when coalesce(sum((data->>'area')::numeric),0)>0 then coalesce(sum(case when status='COMPLETED' then (data->>'estimatedCost')::numeric else 0 end),0)/sum((data->>'area')::numeric) else 0 end CostPerHectare,count(*) filter(where module='scouting' and status not in('CLOSED','CANCELLED')) OpenOccurrences,count(*) filter(where module='scouting' and data->>'severity'='CRITICAL') CriticalOccurrences,coalesce(sum(case when module='plans' then (data->>'quantity')::numeric else 0 end),0) PlannedInputs,coalesce(sum(case when module='applications' then (data->>'quantity')::numeric else 0 end),0) ActualInputs,count(*) filter(where module='applications' and status='COMPLETED') Applications,count(*) filter(where module='irrigations' and status='COMPLETED') Irrigations,coalesce((select sum(expected_yield_per_ha) from agro360.agriculture_seasons where tenant_id=@TenantId),0) ExpectedYield,0::numeric ActualYield,array_remove(array[case when count(*) filter(where module='scouting' and data->>'severity'='CRITICAL')>0 then 'Ocorrência fitossanitária crítica' end,case when count(*) filter(where module='field-notes' and status='PLANNED' and (data->>'plannedAt')::timestamptz<now())>0 then 'Atividades agrícolas atrasadas' end],null) Alerts from agro360.agriculture_records where tenant_id=@TenantId and deleted_at is null",new{tenant.TenantId},t,cancellationToken:cancellationToken)),cancellationToken);

    private static void Validate(string module,AgricultureCommand x){ Agriculture360Rules.Period(x.StartedAt,x.FinishedAt); if(module is "field-notes" or "plans" or "scouting" or "recommendations" or "applications" or "irrigations" or "work-orders") Agriculture360Rules.Required(x.PropertyId,"Propriedade"); if(module=="field-notes") Agriculture360Rules.Required(x.CropSeasonId,"Safra"); if(module is "field-notes" or "scouting" or "applications" or "irrigations" or "work-orders") Agriculture360Rules.Required(x.FieldId,"Talhão"); if(module=="plans"){Agriculture360Rules.Required(x.CropSeasonId,"Safra");Agriculture360Rules.Required(x.CropId,"Cultura");Agriculture360Rules.UniqueFields(x.FieldIds);} if(module=="applications"){Agriculture360Rules.Required(x.InventoryItemId,"Produto");Agriculture360Rules.Positive(x.Dose,"Dose");} if(module=="irrigations") Agriculture360Rules.Positive(x.Area,"Área"); if(module=="weather-records"){Agriculture360Rules.Required(x.PropertyId,"Propriedade");Agriculture360Rules.Weather(x.Rainfall,x.Temperature,x.Humidity,x.Wind);} if(module=="work-orders"&&x.Status=="COMPLETED") Agriculture360Rules.Required(x.ResponsibleId,"Responsável"); if(module=="scouting"&&!new[]{"LOW","MEDIUM","HIGH","CRITICAL"}.Contains(x.Severity)) throw new DomainException("Selecione uma severidade válida.", "agro360.agriculture_severity_invalid"); }
    private static string NormalizeStatus(string? status,string module)
    {
        var value=(status??(module=="work-orders"?"OPEN":"PLANNED")).Trim().ToUpperInvariant();
        if(!new[]{"OPEN","PLANNED","RELEASED","IN_PROGRESS","PAUSED","COMPLETED","CANCELLED","APPROVED","REVISION","CLOSED"}.Contains(value)) throw new DomainException("Status inválido.","agro360.agriculture_status_invalid");
        return value;
    }
    private static void EnsureModule(string module){if(!Modules.Contains(module,StringComparer.OrdinalIgnoreCase)) throw new NotFoundException("Módulo agrícola", Guid.Empty);}
    private async Task ValidateReferences(System.Data.IDbConnection c,System.Data.IDbTransaction t,AgricultureCommand x,CancellationToken ct){var valid=await c.ExecuteScalarAsync<bool>(new CommandDefinition("select (@PropertyId is null or exists(select 1 from agro360.geo_farms where id=@PropertyId and tenant_id=@TenantId and deleted_at is null)) and (@FieldId is null or exists(select 1 from agro360.geo_fields where id=@FieldId and tenant_id=@TenantId and deleted_at is null)) and (@ResponsibleId is null or exists(select 1 from agro360.identity_users where id=@ResponsibleId and tenant_id=@TenantId and status='ACTIVE')) and (@InventoryItemId is null or exists(select 1 from agro360.inventory_products where id=@InventoryItemId and tenant_id=@TenantId and deleted_at is null)) and (@CropSeasonId is null or exists(select 1 from agro360.agriculture_seasons where id=@CropSeasonId and tenant_id=@TenantId and deleted_at is null)) and (@CropId is null or exists(select 1 from agro360.agriculture_crops where id=@CropId and tenant_id=@TenantId and active)) and (@MachineId is null or exists(select 1 from agro360.fleet_assets where id=@MachineId and tenant_id=@TenantId and status not in ('SOLD','INACTIVE'))) and (@CostCenterId is null or exists(select 1 from agro360.finance_cost_centers where id=@CostCenterId and tenant_id=@TenantId and active))",new{x.PropertyId,x.FieldId,x.ResponsibleId,x.InventoryItemId,x.CropSeasonId,x.CropId,x.MachineId,x.CostCenterId,tenant.TenantId},t,cancellationToken:ct));if(!valid)throw new DomainException("Uma seleção não pertence ao tenant ou está inativa.","agro360.agriculture_lookup_invalid");}
    private async Task ApplyEffects(System.Data.IDbConnection c,System.Data.IDbTransaction t,string module,Guid id,AgricultureCommand x,string status,CancellationToken ct){if(module is not ("applications" or "field-notes")||status!="COMPLETED"||x.InventoryItemId is null||x.Quantity is null)return;Agriculture360Rules.Positive(x.Quantity,"Quantidade");var changed=await c.ExecuteAsync(new CommandDefinition("update agro360.inventory_stock_balances set available=available-@Quantity,version=version+1,updated_at=now() where tenant_id=@TenantId and product_id=@ProductId and available-reserved>=@Quantity",new{x.Quantity,tenant.TenantId,ProductId=x.InventoryItemId},t,cancellationToken:ct));if(changed==0)throw new ConflictException("Estoque insuficiente ou produto vencido para a aplicação.","agro360.agriculture_stock_unavailable");}
    private static AgricultureRecord Map(RecordRow x)=>new(x.Id,x.Module,x.Status,x.CreatedAt,JsonSerializer.Deserialize<Dictionary<string,object?>>(x.Data, JsonOptions)??[]);
    private static IReadOnlyDictionary<string,object?> ToData(AgricultureCommand x)=>JsonSerializer.Deserialize<Dictionary<string,object?>>(JsonSerializer.Serialize(x, JsonOptions), JsonOptions)??[];
    private sealed record RecordRow(Guid Id,string Module,string Status,DateTimeOffset CreatedAt,string Data);
}
