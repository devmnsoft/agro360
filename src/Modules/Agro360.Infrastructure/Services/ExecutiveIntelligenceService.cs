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

    public Task<ExecutivePanel> GetPanelAsync(CancellationToken ct) => Guard("panel", () => database.InTenantTransactionAsync(async (c, t) =>
    {
        var indicators = (await c.QueryAsync<ExecutiveKpi>(new CommandDefinition("""select d.id,d.code,d.name,d.category,d.unit,s.calculated_value value,d.target,s.status,s.calculated_at,s.calculation_error from intelligence_kpi_definitions d left join lateral(select calculated_value,status,calculated_at,calculation_error from intelligence_kpi_snapshots where tenant_id=@TenantId and kpi_id=d.id order by reference_date desc,created_at desc limit 1)s on true where d.tenant_id=@TenantId and d.active and d.deleted_at is null order by d.category,d.name""", new { tenant.TenantId }, t, cancellationToken: ct))).ToArray();
        var totals = await c.QuerySingleAsync(new CommandDefinition("""select (select count(*) from intelligence_alerts where tenant_id=@TenantId and status not in('RESOLVED','CANCELLED')) open_alerts,(select count(*) from intelligence_alerts where tenant_id=@TenantId and severity='CRITICAL' and status not in('RESOLVED','CANCELLED')) critical_alerts,(select count(*) from intelligence_risks where tenant_id=@TenantId and status='OPEN') open_risks,(select count(*) from intelligence_recommendations where tenant_id=@TenantId and status='NEW') pending_recommendations""", new { tenant.TenantId }, t, cancellationToken: ct));
        return new ExecutivePanel(indicators, (int)totals.open_alerts, (int)totals.critical_alerts, (int)totals.open_risks, (int)totals.pending_recommendations);
    }, ct));

    public Task<Guid> CreateKpiAsync(KpiDefinitionCommand x, Guid userId, bool strategic, CancellationToken ct) => Guard("create-kpi", () => database.InTenantTransactionAsync(async (c,t) =>
    {
        if (!Sources.Contains(x.DataSource)) throw new ArgumentException("Fonte de dados não suportada.");
        if (x.Strategic && !strategic) throw new UnauthorizedAccessException("Permissão para indicador estratégico ausente.");
        if (x.Unit == "PERCENT" && new[] { x.Target, x.AttentionLimit, x.CriticalLimit }.Any(v => v is < 0 or > 100)) throw new ArgumentException("Percentuais devem estar entre 0 e 100.");
        var id=Guid.NewGuid();
        await c.ExecuteAsync(new CommandDefinition("insert into intelligence_kpi_definitions(id,tenant_id,name,code,category,formula,data_source,periodicity,unit,target,attention_limit,critical_limit,active,strategic,created_by,updated_by) values(@Id,@TenantId,@Name,@Code,@Category,@Formula,@DataSource,@Periodicity,@Unit,@Target,@AttentionLimit,@CriticalLimit,@Active,@Strategic,@UserId,@UserId)",new{Id=id,tenant.TenantId,Name=x.Name.Trim(),Code=x.Code.Trim(),x.Category,x.Formula,x.DataSource,x.Periodicity,x.Unit,x.Target,x.AttentionLimit,x.CriticalLimit,x.Active,x.Strategic,UserId=userId},t,cancellationToken:ct)); return id;
    },ct));

    public Task RecalculateAsync(Guid id,Guid userId,CancellationToken ct)=>Guard("recalculate",()=>database.InTenantTransactionAsync(async(c,t)=>
    {
        var source=await c.ExecuteScalarAsync<string?>(new CommandDefinition("select data_source from intelligence_kpi_definitions where id=@Id and tenant_id=@TenantId and active and deleted_at is null",new{Id=id,tenant.TenantId},t,cancellationToken:ct));
        if(string.IsNullOrEmpty(source))throw new KeyNotFoundException("Indicador não encontrado.");
        // Formula identifiers are allow-listed; client SQL is never executed. Connectors populate source_value.
        await c.ExecuteAsync(new CommandDefinition("insert into intelligence_kpi_snapshots(id,tenant_id,kpi_id,reference_date,status,source,calculation_error,calculated_by,created_by,updated_by) values(gen_random_uuid(),@TenantId,@Id,current_date,'UNAVAILABLE',@Source,'Conector sem valor disponível para o período.',@UserId,@UserId,@UserId)",new{tenant.TenantId,Id=id,Source=source,UserId=userId},t,cancellationToken:ct));
    },ct));

    public Task DecideAlertAsync(Guid id,AlertDecisionCommand x,Guid userId,CancellationToken ct)=>Guard("alert-decision",()=>database.InTenantTransactionAsync(async(c,t)=>
    {
        var severity=await c.ExecuteScalarAsync<string?>(new CommandDefinition("select severity from intelligence_alerts where id=@Id and tenant_id=@TenantId",new{Id=id,tenant.TenantId},t,cancellationToken:ct))??throw new KeyNotFoundException("Alerta não encontrado.");
        if(x.Status=="RESOLVED"&&string.IsNullOrWhiteSpace(x.Comment))throw new ArgumentException("Resolver alerta exige comentário.");
        if(x.Status=="IGNORED"&&severity=="CRITICAL"&&string.IsNullOrWhiteSpace(x.Comment))throw new ArgumentException("Alerta crítico exige justificativa para ser ignorado.");
        if(x.Status=="ASSIGNED"&&!x.ResponsibleId.HasValue)throw new ArgumentException("Alerta atribuído exige responsável.");
        await c.ExecuteAsync(new CommandDefinition("update intelligence_alerts set status=@Status,responsible_id=@ResponsibleId,resolved_at=case when @Status='RESOLVED' then now() else resolved_at end,resolved_by=case when @Status='RESOLVED' then @UserId else resolved_by end,updated_at=now(),updated_by=@UserId where id=@Id and tenant_id=@TenantId;insert into intelligence_alert_events(id,tenant_id,alert_id,event_type,comment,created_by,updated_by) values(gen_random_uuid(),@TenantId,@Id,@Status,@Comment,@UserId,@UserId)",new{Id=id,tenant.TenantId,x.Status,x.ResponsibleId,x.Comment,UserId=userId},t,cancellationToken:ct));
    },ct));

    public Task DecideRecommendationAsync(Guid id,RecommendationStatusCommand x,Guid userId,CancellationToken ct)=>Guard("recommendation-decision",()=>database.InTenantTransactionAsync(async(c,t)=>
    { var severity=await c.ExecuteScalarAsync<string?>(new CommandDefinition("select severity from intelligence_recommendations where id=@Id and tenant_id=@TenantId",new{Id=id,tenant.TenantId},t,cancellationToken:ct))??throw new KeyNotFoundException("Recomendação não encontrada.");if(x.Status=="REJECTED"&&(severity=="HIGH"||severity=="CRITICAL")&&string.IsNullOrWhiteSpace(x.Reason))throw new ArgumentException("Rejeição de recomendação alta ou crítica exige motivo.");await c.ExecuteAsync(new CommandDefinition("update intelligence_recommendations set status=@Status,decision_reason=@Reason,updated_at=now(),updated_by=@UserId where id=@Id and tenant_id=@TenantId;insert into intelligence_recommendation_events(id,tenant_id,recommendation_id,event_type,reason,created_by,updated_by) values(gen_random_uuid(),@TenantId,@Id,@Status,@Reason,@UserId,@UserId)",new{Id=id,tenant.TenantId,x.Status,x.Reason,UserId=userId},t,cancellationToken:ct));},ct));

    public Task<byte[]> ExportAsync(string report,IntelligencePageFilter f,Guid userId,CancellationToken ct)=>Guard("export",()=>database.InTenantTransactionAsync(async(c,t)=>{var allowed=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"indicators","snapshots","alerts","risks","recommendations","audit"};if(!allowed.Contains(report))throw new ArgumentException("Relatório não suportado.");var rows=await c.QueryAsync<(string Type,string Status,string Description,DateTimeOffset CreatedAt)>(new CommandDefinition("select 'ALERT' type,status,description,created_at from intelligence_alerts where tenant_id=@TenantId and (@Status is null or status=@Status) order by created_at desc limit @PageSize",new{tenant.TenantId,Status=string.IsNullOrWhiteSpace(f.Status)?null:f.Status,PageSize=Math.Clamp(f.PageSize,1,100)},t,cancellationToken:ct));var csv=new StringBuilder("Tipo;Status;Descrição;Data\r\n");foreach(var row in rows)csv.AppendLine($"{Csv(row.Type)};{Csv(row.Status)};{Csv(row.Description)};{row.CreatedAt:O}");await c.ExecuteAsync("insert into intelligence_report_exports(id,tenant_id,report,filters,row_count,created_by) values(gen_random_uuid(),@TenantId,@Report,'{}',@Count,@UserId)",new{tenant.TenantId,Report=report,Count=rows.Count(),UserId=userId},t);return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();},ct));
    private static string Csv(string value)=>$"\"{value.Replace("\"","\"\"")}\"";
    private async Task<T> Guard<T>(string operation,Func<Task<T>> work){try{return await work();}catch(Exception ex){logger.LogError(ex,"Falha em inteligência executiva {Operation} para tenant {TenantId}",operation,tenant.TenantId);throw;}}
    private async Task Guard(string operation,Func<Task> work){try{await work();}catch(Exception ex){logger.LogError(ex,"Falha em inteligência executiva {Operation} para tenant {TenantId}",operation,tenant.TenantId);throw;}}
}
