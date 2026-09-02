using System.Globalization;
using System.Text;
using Agro360.Application.Contracts;
using Agro360.Domain.Sustainability;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Dapper;

namespace Agro360.Infrastructure.Services;

public sealed class SustainabilityService(DatabaseExecutor db, ITenantContext tenant) : ISustainabilityService
{
    public Task<SustainabilityDashboard> DashboardAsync(CancellationToken ct) => db.InTenantTransactionAsync((c,t) => c.QuerySingleAsync<SustainabilityDashboard>("""
        select
        (select count(*) from agro360.sustainability_environmental_compliances where tenant_id=@TenantId and deleted_at is null and status='REGULAR') regularproperties,
        (select count(*) from agro360.sustainability_environmental_compliances where tenant_id=@TenantId and deleted_at is null and status in('UNDER_REVIEW','PENDING','EXPIRED','BLOCKED','REJECTED')) pendingproperties,
        (select count(*) from agro360.sustainability_environmental_documents where tenant_id=@TenantId and deleted_at is null and valid_until<current_date) expireddocuments,
        (select count(*) from agro360.sustainability_environmental_documents where tenant_id=@TenantId and deleted_at is null and valid_until between current_date and current_date+30) expiringdocuments,
        (select count(*) from agro360.sustainability_lot_traceability where tenant_id=@TenantId and deleted_at is null and status='BLOCKED') blockedlots,
        (select count(*) from agro360.sustainability_lot_traceability where tenant_id=@TenantId and deleted_at is null and status='COMPLIANT') compliantlots,
        (select count(*) from agro360.sustainability_supplier_assessments where tenant_id=@TenantId and deleted_at is null and status='BLOCKED') blockedsuppliers,
        (select count(*) from agro360.sustainability_esg_indicators where tenant_id=@TenantId and deleted_at is null and status='CRITICAL') criticalindicators,
        coalesce((select sum(quantity) from agro360.sustainability_resource_usages where tenant_id=@TenantId and deleted_at is null and resource_type='WATER'),0) waterconsumption,
        coalesce((select sum(quantity) from agro360.sustainability_resource_usages where tenant_id=@TenantId and deleted_at is null and resource_type='ENERGY'),0) energyconsumption,
        coalesce((select sum(estimated_emission) from agro360.sustainability_emission_inventories where tenant_id=@TenantId and deleted_at is null),0) estimatedemissions,
        coalesce((select sum(quantity) from agro360.sustainability_waste_records where tenant_id=@TenantId and deleted_at is null),0) wastequantity,
        (select count(*) from agro360.sustainability_audits where tenant_id=@TenantId and deleted_at is null and status in('PLANNED','IN_PROGRESS','PENDING')) openaudits,
        (select count(*) from agro360.sustainability_action_plans where tenant_id=@TenantId and deleted_at is null and due_on<current_date and status not in('COMPLETED','CANCELLED')) overdueactions,
        (select count(*) from agro360.sustainability_carbon_projects where tenant_id=@TenantId and deleted_at is null and status in('ACTIVE','INTERNALLY_APPROVED','CERTIFIED')) activecarbonprojects,
        (select count(*) from agro360.sustainability_alerts where tenant_id=@TenantId and status='OPEN' and severity='CRITICAL') criticalalerts
        """, new { tenant.TenantId }, t), ct);

    public Task<IReadOnlyList<EnvironmentalComplianceListItem>> CompliancesAsync(string? status,CancellationToken ct) => db.InTenantTransactionAsync(async(c,t) =>
        (IReadOnlyList<EnvironmentalComplianceListItem>)(await c.QueryAsync<EnvironmentalComplianceListItem>("""
        select x.id,f.name farmname,x.producer_name producername,x.total_area totalarea,x.productive_area productivearea,x.status,x.risk,x.license_valid_until licensevaliduntil
        from agro360.sustainability_environmental_compliances x join agro360.geo_farms f on f.tenant_id=x.tenant_id and f.id=x.farm_id
        where x.tenant_id=@TenantId and x.deleted_at is null and (@Status is null or x.status=@Status) order by f.name
        """,new { tenant.TenantId, Status=status },t)).AsList(),ct);

    public Task<IReadOnlyList<SustainabilityFarmOption>> FarmsAsync(string? search,CancellationToken ct) => db.InTenantTransactionAsync(async(c,t) =>
        (IReadOnlyList<SustainabilityFarmOption>)(await c.QueryAsync<SustainabilityFarmOption>("select id,name from agro360.geo_farms where tenant_id=@TenantId and deleted_at is null and (@Search is null or name ilike '%'||@Search||'%') order by name limit 50",new { tenant.TenantId,Search=string.IsNullOrWhiteSpace(search)?null:search.Trim() },t)).AsList(),ct);

    public Task<Guid> SaveComplianceAsync(EnvironmentalComplianceCommand command,CancellationToken ct)
    {
        SustainabilityRules.ValidateAreas(command.TotalArea,command.ProductiveArea,command.PreservationArea,command.AppArea,command.LegalReserveArea);
        return db.InTenantTransactionAsync(async(c,t) => {
            var id=Guid.NewGuid();
            var changed=await c.ExecuteAsync("""
            insert into agro360.sustainability_environmental_compliances(id,tenant_id,farm_id,producer_name,total_area,productive_area,preservation_area,app_area,legal_reserve_area,car_number,car_status,environmental_license,license_valid_until,issuing_agency,georeferenced,status,risk,notes,created_by,updated_by)
            select @Id,@TenantId,@FarmId,@ProducerName,@TotalArea,@ProductiveArea,@PreservationArea,@AppArea,@LegalReserveArea,@CarNumber,@CarStatus,@EnvironmentalLicense,@LicenseValidUntil,@IssuingAgency,@Georeferenced,@Status,@Risk,@Notes,@UserId,@UserId
            where exists(select 1 from agro360.geo_farms where tenant_id=@TenantId and id=@FarmId and deleted_at is null)
            """,new { Id=id,tenant.TenantId,tenant.UserId,command.FarmId,command.ProducerName,command.TotalArea,command.ProductiveArea,command.PreservationArea,command.AppArea,command.LegalReserveArea,command.CarNumber,command.CarStatus,command.EnvironmentalLicense,command.LicenseValidUntil,command.IssuingAgency,command.Georeferenced,command.Status,command.Risk,command.Notes },t);
            if(changed==0) throw new KeyNotFoundException("Propriedade não encontrada neste tenant.");
            return id;
        },ct);
    }

    public async Task<byte[]> ExportCsvAsync(string report,CancellationToken ct)
    {
        var allowed = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase) {
            ["environmental-compliance"]="select producer_name,status,risk,total_area,productive_area,license_valid_until from agro360.sustainability_environmental_compliances where tenant_id=@TenantId and deleted_at is null",
            ["environmental-documents"]="select type,title,status,issued_on,valid_until,mandatory from agro360.sustainability_environmental_documents where tenant_id=@TenantId and deleted_at is null",
            ["indicators"]="select name,category,unit,target,current_value,status,data_source from agro360.sustainability_esg_indicators where tenant_id=@TenantId and deleted_at is null",
            ["measurements"]="select indicator_id,measured_on,value,source from agro360.sustainability_esg_measurements where tenant_id=@TenantId",
            ["emissions"]="select period_start,period_end,source,category,quantity,unit,emission_factor,estimated_emission,status from agro360.sustainability_emission_inventories where tenant_id=@TenantId and deleted_at is null",
            ["water"]="select period_start,period_end,quantity,unit,cost,data_origin from agro360.sustainability_resource_usages where tenant_id=@TenantId and deleted_at is null and resource_type='WATER'",
            ["energy"]="select period_start,period_end,quantity,unit,cost,data_origin from agro360.sustainability_resource_usages where tenant_id=@TenantId and deleted_at is null and resource_type='ENERGY'",
            ["waste"]="select recorded_on,type,quantity,unit,destination from agro360.sustainability_waste_records where tenant_id=@TenantId and deleted_at is null",
            ["suppliers"]="select supplier_id,category,environmental_risk,social_risk,operational_risk,status,valid_until from agro360.sustainability_supplier_assessments where tenant_id=@TenantId and deleted_at is null",
            ["lots"]="select lot_id,farm_id,status,justification from agro360.sustainability_lot_traceability where tenant_id=@TenantId and deleted_at is null",
            ["carbon-projects"]="select name,type,period_start,period_end,area,estimated_carbon,status,certified from agro360.sustainability_carbon_projects where tenant_id=@TenantId and deleted_at is null",
            ["audits"]="select type,scope,due_on,status,completed_at from agro360.sustainability_audits where tenant_id=@TenantId and deleted_at is null",
            ["action-plans"]="select origin,category,description,due_on,priority,status,result from agro360.sustainability_action_plans where tenant_id=@TenantId and deleted_at is null",
            ["alerts"]="select type,severity,message,status,due_at,created_at from agro360.sustainability_alerts where tenant_id=@TenantId"
        };
        if(!allowed.TryGetValue(report,out var sql)) throw new ArgumentException("Relatório ESG inválido.",nameof(report));
        var rows=await db.InTenantTransactionAsync(async(c,t)=>(await c.QueryAsync(sql,new { tenant.TenantId },t)).ToArray(),ct);
        var builder=new StringBuilder();
        if(rows.Length>0) { var names=((IDictionary<string,object>)rows[0]).Keys.ToArray(); builder.AppendLine(string.Join(';',names)); foreach(IDictionary<string,object> row in rows) builder.AppendLine(string.Join(';',names.Select(n=>Csv(row[n])))); }
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(builder.ToString())).ToArray();
    }
    private static string Csv(object? value) => $"\"{Convert.ToString(value,CultureInfo.InvariantCulture)?.Replace("\"","\"\"")}\"";
}
