using System.Globalization;
using System.Text;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Agro360.Infrastructure.Services;

public sealed class ExecutiveIntelligenceService(DatabaseExecutor database, ITenantContext tenant, ILogger<ExecutiveIntelligenceService> logger) : IExecutiveIntelligenceService
{
    private static readonly HashSet<string> Sources = new(StringComparer.OrdinalIgnoreCase) { "FINANCE", "PROCUREMENT", "INVENTORY", "PRODUCTION", "QUALITY", "EXPORT", "FISCAL", "LOGISTICS", "COMPLIANCE", "TRACEABILITY", "AGRICULTURE", "LIVESTOCK" };

    public Task<ExecutivePanel> GetPanelAsync(CancellationToken cancellationToken) => Guard("panel", () => database.InTenantTransactionAsync(async (c, t) =>
    {
        var indicators = (await c.QueryAsync<ExecutiveKpi>(new CommandDefinition("""select d.id,d.code,d.name,d.category,d.unit,s.calculated_value value,d.target,s.status,s.calculated_at,s.calculation_error from agro360.intelligence_kpi_definitions d left join lateral(select calculated_value,status,calculated_at,calculation_error from agro360.intelligence_kpi_snapshots where tenant_id=@TenantId and kpi_id=d.id order by reference_date desc,created_at desc limit 1)s on true where d.tenant_id=@TenantId and d.active and d.deleted_at is null order by d.category,d.name""", new { tenant.TenantId }, t, cancellationToken: cancellationToken))).ToArray();
        var totals = await c.QuerySingleAsync(new CommandDefinition("""select (select count(*) from agro360.intelligence_alerts where tenant_id=@TenantId and status not in('RESOLVED','CANCELLED')) open_alerts,(select count(*) from agro360.intelligence_alerts where tenant_id=@TenantId and severity='CRITICAL' and status not in('RESOLVED','CANCELLED')) critical_alerts,(select count(*) from agro360.intelligence_risks where tenant_id=@TenantId and status='OPEN') open_risks,(select count(*) from agro360.intelligence_recommendations where tenant_id=@TenantId and status='NEW') pending_recommendations""", new { tenant.TenantId }, t, cancellationToken: cancellationToken));
        return new ExecutivePanel(indicators, (int)totals.open_alerts, (int)totals.critical_alerts, (int)totals.open_risks, (int)totals.pending_recommendations);
    }, cancellationToken));

    public Task<Guid> CreateKpiAsync(KpiDefinitionCommand command, Guid userId, bool canManageStrategic, CancellationToken cancellationToken) => Guard("create-kpi", () => database.InTenantTransactionAsync(async (c,t) =>
    {
        if (!Sources.Contains(command.DataSource)) throw new ArgumentException("Fonte de dados não suportada.");
        if (command.Strategic && !canManageStrategic) throw new UnauthorizedAccessException("Permissão para indicador estratégico ausente.");
        if (command.Unit == "PERCENT" && new[] { command.Target, command.AttentionLimit, command.CriticalLimit }.Any(v => v is < 0 or > 100)) throw new ArgumentException("Percentuais devem estar entre 0 e 100.");
        var id=Guid.NewGuid();
        await c.ExecuteAsync(new CommandDefinition("insert into agro360.intelligence_kpi_definitions(id,tenant_id,name,code,category,formula,data_source,periodicity,unit,target,attention_limit,critical_limit,active,canManageStrategic,created_by,updated_by) values(@Id,@TenantId,@Name,@Code,@Category,@Formula,@DataSource,@Periodicity,@Unit,@Target,@AttentionLimit,@CriticalLimit,@Active,@Strategic,@UserId,@UserId)",new{Id=id,tenant.TenantId,Name=command.Name.Trim(),Code=command.Code.Trim(),command.Category,command.Formula,command.DataSource,command.Periodicity,command.Unit,command.Target,command.AttentionLimit,command.CriticalLimit,command.Active,command.Strategic,UserId=userId},t,cancellationToken:cancellationToken)); return id;
    },cancellationToken));

    public Task RecalculateAsync(Guid id,Guid userId,CancellationToken cancellationToken)=>Guard("recalculate",()=>database.InTenantTransactionAsync(async(c,t)=>
    {
        var source=await c.ExecuteScalarAsync<string?>(new CommandDefinition("select data_source from agro360.intelligence_kpi_definitions where id=@Id and tenant_id=@TenantId and active and deleted_at is null",new{Id=id,tenant.TenantId},t,cancellationToken:cancellationToken));
        if(string.IsNullOrEmpty(source))throw new KeyNotFoundException("Indicador não encontrado.");
        // Formula identifiers are allow-listed; client SQL is never executed. Connectors populate source_value.
        await c.ExecuteAsync(new CommandDefinition("insert into agro360.intelligence_kpi_snapshots(id,tenant_id,kpi_id,reference_date,status,source,calculation_error,calculated_by,created_by,updated_by) values(gen_random_uuid(),@TenantId,@Id,current_date,'UNAVAILABLE',@Source,'Conector sem valor disponível para o período.',@UserId,@UserId,@UserId)",new{tenant.TenantId,Id=id,Source=source,UserId=userId},t,cancellationToken:cancellationToken));
    },cancellationToken));

    public Task DecideAlertAsync(Guid id,AlertDecisionCommand command,Guid userId,CancellationToken cancellationToken)=>Guard("alert-decision",()=>database.InTenantTransactionAsync(async(c,t)=>
    {
        var severity=await c.ExecuteScalarAsync<string?>(new CommandDefinition("select severity from agro360.intelligence_alerts where id=@Id and tenant_id=@TenantId",new{Id=id,tenant.TenantId},t,cancellationToken:cancellationToken))??throw new KeyNotFoundException("Alerta não encontrado.");
        if(command.Status=="RESOLVED"&&string.IsNullOrWhiteSpace(command.Comment))throw new ArgumentException("Resolver alerta exige comentário.");
        if(command.Status=="IGNORED"&&severity=="CRITICAL"&&string.IsNullOrWhiteSpace(command.Comment))throw new ArgumentException("Alerta crítico exige justificativa para ser ignorado.");
        if(command.Status=="ASSIGNED"&&!command.ResponsibleId.HasValue)throw new ArgumentException("Alerta atribuído exige responsável.");
        await c.ExecuteAsync(new CommandDefinition("update agro360.intelligence_alerts set status=@Status,responsible_id=@ResponsibleId,resolved_at=case when @Status='RESOLVED' then now() else resolved_at end,resolved_by=case when @Status='RESOLVED' then @UserId else resolved_by end,updated_at=now(),updated_by=@UserId where id=@Id and tenant_id=@TenantId;insert into agro360.intelligence_alert_events(id,tenant_id,alert_id,event_type,comment,created_by,updated_by) values(gen_random_uuid(),@TenantId,@Id,@Status,@Comment,@UserId,@UserId)",new{Id=id,tenant.TenantId,command.Status,command.ResponsibleId,command.Comment,UserId=userId},t,cancellationToken:cancellationToken));
    },cancellationToken));

    public Task DecideRecommendationAsync(Guid id,RecommendationStatusCommand command,Guid userId,CancellationToken cancellationToken)=>Guard("recommendation-decision",()=>database.InTenantTransactionAsync(async(c,t)=>
    { var severity=await c.ExecuteScalarAsync<string?>(new CommandDefinition("select severity from agro360.intelligence_recommendations where id=@Id and tenant_id=@TenantId",new{Id=id,tenant.TenantId},t,cancellationToken:cancellationToken))??throw new KeyNotFoundException("Recomendação não encontrada.");if(command.Status=="REJECTED"&&(severity=="HIGH"||severity=="CRITICAL")&&string.IsNullOrWhiteSpace(command.Reason))throw new ArgumentException("Rejeição de recomendação alta ou crítica exige motivo.");await c.ExecuteAsync(new CommandDefinition("update agro360.intelligence_recommendations set status=@Status,decision_reason=@Reason,updated_at=now(),updated_by=@UserId where id=@Id and tenant_id=@TenantId;insert into agro360.intelligence_recommendation_events(id,tenant_id,recommendation_id,event_type,reason,created_by,updated_by) values(gen_random_uuid(),@TenantId,@Id,@Status,@Reason,@UserId,@UserId)",new{Id=id,tenant.TenantId,command.Status,command.Reason,UserId=userId},t,cancellationToken:cancellationToken));},cancellationToken));

    public Task<byte[]> ExportAsync(string report,IntelligencePageFilter filter,Guid userId,CancellationToken cancellationToken)=>Guard("export",()=>database.InTenantTransactionAsync(async(c,t)=>{var allowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"indicators","snapshots","alerts","risks","recommendations","audit"};if(!allowed.Contains(report))throw new ArgumentException("Relatório não suportado.");var rows=await c.QueryAsync<(string Type,string Status,string Description,DateTimeOffset CreatedAt)>(new CommandDefinition("select 'ALERT' type,status,description,created_at from agro360.intelligence_alerts where tenant_id=@TenantId and (@Status is null or status=@Status) order by created_at desc limit @PageSize",new{tenant.TenantId,Status=string.IsNullOrWhiteSpace(filter.Status)?null:filter.Status,PageSize=Math.Clamp(filter.PageSize,1,100)},t,cancellationToken:cancellationToken));var csv=new StringBuilder("Tipo;Status;Descrição;Data\r\n");foreach(var row in rows)csv.AppendLine(CultureInfo.InvariantCulture,$"{Csv(row.Type)};{Csv(row.Status)};{Csv(row.Description)};{row.CreatedAt:O}");await c.ExecuteAsync("insert into agro360.intelligence_report_exports(id,tenant_id,report,filters,row_count,created_by) values(gen_random_uuid(),@TenantId,@Report,'{}',@Count,@UserId)",new{tenant.TenantId,Report=report,Count=rows.Count(),UserId=userId},t);return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();},cancellationToken));
    private static string Csv(string value)=>$"\"{value.Replace("\"","\"\"")}\"";
    private async Task<T> Guard<T>(string operation,Func<Task<T>> work){try{return await work();}catch(Exception ex){InfrastructureLogMessages.ExecutiveIntelligenceFailed(logger,operation,tenant.TenantId,ex);throw;}}
    private async Task Guard(string operation,Func<Task> work){try{await work();}catch(Exception ex){InfrastructureLogMessages.ExecutiveIntelligenceFailed(logger,operation,tenant.TenantId,ex);throw;}}
}
