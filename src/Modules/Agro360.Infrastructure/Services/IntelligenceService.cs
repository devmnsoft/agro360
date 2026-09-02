using System.Globalization;
using System.Text;
using Agro360.Application.Abstractions;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Agro360.Infrastructure.Services;

public sealed class IntelligenceService : IIntelligenceService
{
    private readonly DatabaseExecutor _database;
    private readonly ITenantContext _tenant;
    private readonly IClock _clock;
    private readonly ILogger<IntelligenceService> _logger;

    public IntelligenceService(
        DatabaseExecutor database,
        ITenantContext tenant,
        IClock clock,
        ILogger<IntelligenceService> logger)
    {
        _database = database;
        _tenant = tenant;
        _clock = clock;
        _logger = logger;
    }

    private static readonly ReportDefinition[] Reports =
    [
        new("financial-by-season","Resultado financeiro por safra","Financeiro",true,true), new("financial-by-property","Resultado financeiro por propriedade","Financeiro",false,true),
        new("field-result","Resultado por talhão","Agricultura",true,true), new("herd-result","Resultado por rebanho/lote","Pecuária",false,true),
        new("current-stock","Estoque atual","Estoque",false,true), new("stock-movements","Movimentações de estoque","Estoque",false,true),
        new("purchases-by-supplier","Compras por fornecedor","Compras",false,true), new("payables","Contas a pagar","Financeiro",false,true),
        new("receivables","Contas a receber","Financeiro",false,true), new("cash-flow","Fluxo de caixa","Financeiro",false,false),
        new("agricultural-activities","Atividades agrícolas","Agricultura",true,true), new("livestock-handling","Manejos pecuários","Pecuária",false,true),
        new("animal-health","Sanidade animal","Pecuária",false,true), new("milk-production","Produção de leite","Pecuária",false,false),
        new("weight-gain","Ganho de peso","Pecuária",false,false), new("machine-maintenance","Manutenção de máquinas","Máquinas",false,true),
        new("fueling","Abastecimentos","Máquinas",false,true), new("receipts","Romaneios","Armazenagem",false,true),
        new("product-quality","Qualidade de produto","Armazenagem",false,true), new("shipments","Expedições","Logística",false,true),
        new("regional-logistics","Logística regional","Logística",false,true), new("lot-traceability","Rastreabilidade de lote","Conformidade",false,true),
        new("commissions-splits","Comissões e splits","Vendas",false,true)
    ];

    public Task<IReadOnlyList<IndicatorResult>> GetIndicatorsAsync(IntelligenceFilter filter, CancellationToken ct) => Guard("indicators", () => _database.InTenantTransactionAsync(async (c,t) =>
    {
        Validate(filter);
        var p = Params(filter);
        const string sql = """
        select
          coalesce((select sum(original_amount-balance) from agro360.finance_receivables where tenant_id=@TenantId and issued_on between @From and @To),0) revenue,
          coalesce((select sum(original_amount-balance) from agro360.finance_payables where tenant_id=@TenantId and issued_on between @From and @To),0) expense,
          coalesce((select sum(ce.amount) from agro360.cost_entries ce where ce.tenant_id=@TenantId and ce.occurred_on between @From and @To and (@FarmId is null or ce.farm_id=@FarmId)),0) cost,
          coalesce((select sum(f.total_area_ha) from agro360.geo_farms f where f.tenant_id=@TenantId and f.deleted_at is null and (@FarmId is null or f.id=@FarmId)),0) hectares,
          (select count(*) from agro360.livestock_animals a where a.tenant_id=@TenantId and a.deleted_at is null and a.status=1 and (@FarmId is null or a.farm_id=@FarmId)) animals,
          (select count(*) from agro360.inventory_stock_balances b join agro360.inventory_warehouses w on w.id=b.warehouse_id where b.tenant_id=@TenantId and b.available<=b.minimum and (@FarmId is null or w.farm_id=@FarmId)) critical_stock,
          (select count(*) from agro360.agriculture_field_operations o where o.tenant_id=@TenantId and o.status not in ('COMPLETED','CANCELLED') and o.executed_at<now() and (@FarmId is null or o.farm_id=@FarmId)) late_activities,
          (select count(*) from agro360.fleet_maintenance_orders m where m.tenant_id=@TenantId and m.status not in ('COMPLETED','CANCELLED') and coalesce(m.scheduled_for,m.next_review_date)<current_date) overdue_maintenance,
          (select count(*) from agro360.storage_receipts r where r.tenant_id=@TenantId and r.status not in ('UNLOADED','CANCELLED')) pending_receipts,
          (select count(*) from agro360.storage_shipments s where s.tenant_id=@TenantId and s.status not in ('DISPATCHED','CANCELLED') and s.created_at<now()-interval '2 days') late_shipments,
          (select count(*) from agro360.traceability_certificates x where x.tenant_id=@TenantId and x.revoked_at is null) certificates,
          (select count(*) from agro360.storage_lots l where l.tenant_id=@TenantId and l.status='BLOCKED') nonconforming_lots,
          (select count(*) from agro360.regional_logistics_trips x where x.tenant_id=@TenantId and x.status not in ('COMPLETED','CANCELLED') and x.planned_start<now()+interval '24 hours') risky_trips
        """;
        var command = new CommandDefinition(sql, p, t, cancellationToken: ct);
        var x = await c.QuerySingleAsync(command);
        decimal revenue=Convert.ToDecimal(x.revenue), expense=Convert.ToDecimal(x.expense), cost=Convert.ToDecimal(x.cost), hectares=Convert.ToDecimal(x.hectares); int animals=Convert.ToInt32(x.animals);
        return (IReadOnlyList<IndicatorResult>)new IndicatorResult[] {
          I("revenue","Receita no período","Financeiro",revenue,"BRL","Recebimentos baixados no período"), I("expense","Despesa no período","Financeiro",expense,"BRL","Pagamentos baixados no período"),
          I("margin","Margem realizada","Financeiro",revenue-expense-cost,"BRL","Receita menos despesas e custos operacionais"), I("cost-per-hectare","Custo por hectare","Agricultura",hectares==0?0:cost/hectares,"BRL/ha","Custos divididos pela área filtrada"),
          I("cost-per-animal","Custo por animal","Pecuária",animals==0?0:cost/animals,"BRL/animal","Custos divididos pelos animais ativos"), I("critical-stock","Estoque crítico","Estoque",Convert.ToDecimal(x.critical_stock),"itens","Saldo disponível menor ou igual ao mínimo"),
          I("late-activities","Atividades atrasadas","Agricultura",Convert.ToDecimal(x.late_activities),"atividades","Atividades abertas com data planejada ultrapassada"), I("overdue-maintenance","Manutenções vencidas","Máquinas",Convert.ToDecimal(x.overdue_maintenance),"ordens","Ordens abertas após revisão programada"),
          I("pending-receipts","Romaneios pendentes","Armazenagem",Convert.ToDecimal(x.pending_receipts),"romaneios","Romaneios ainda não descarregados"), I("late-shipments","Expedições atrasadas","Logística",Convert.ToDecimal(x.late_shipments),"expedições","Expedições abertas há mais de 48 horas"),
          I("certificates","Certificados emitidos","Conformidade",Convert.ToDecimal(x.certificates),"certificados","Certificados vigentes"), I("nonconforming-lots","Lotes sem conformidade","Conformidade",Convert.ToDecimal(x.nonconforming_lots),"lotes","Lotes bloqueados"), I("risky-trips","Viagens em risco","Logística",Convert.ToDecimal(x.risky_trips),"viagens","Viagens abertas dentro da janela de 24 horas") };
    },ct));

    public Task<IReadOnlyList<ReportDefinition>> GetReportsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ReportDefinition>>(Reports);
    public Task<ReportResult> RunReportAsync(string id, IntelligenceFilter filter, CancellationToken ct) => Guard("report", () => _database.InTenantTransactionAsync(async (c,t) =>
    {
        Validate(filter); if (!Reports.Any(x=>x.Id==id)) throw new ArgumentException("Relatório desconhecido.",nameof(id));
        var sql = ReportSql(id); var rows=(await c.QueryAsync(new CommandDefinition(sql,Params(filter),t,cancellationToken:ct))).Cast<IDictionary<string,object?>>().Select(x=>(IReadOnlyDictionary<string,object?>)new Dictionary<string,object?>(x,StringComparer.OrdinalIgnoreCase)).ToArray();
        return new ReportResult(id,rows.FirstOrDefault()?.Keys.ToArray() ?? [],rows,rows.Length);
    },ct));
    public async Task<byte[]> ExportCsvAsync(string id, IntelligenceFilter filter, CancellationToken ct) { var r=await RunReportAsync(id,filter,ct); var b=new StringBuilder(); b.AppendLine(string.Join(';',r.Columns.Select(Csv))); foreach(var row in r.Rows)b.AppendLine(string.Join(';',r.Columns.Select(x=>Csv(Convert.ToString(row.GetValueOrDefault(x),CultureInfo.InvariantCulture)??"")))); return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(b.ToString())).ToArray(); }

    public Task<IReadOnlyList<AlertResult>> GetAlertsAsync(string? status,CancellationToken ct)=>Guard("alerts",()=>_database.InTenantTransactionAsync(async(c,t)=>(IReadOnlyList<AlertResult>)(await c.QueryAsync<AlertResult>("select id,type,severity,title,status,detected_at DetectedAt,snoozed_until SnoozedUntil from agro360.intelligence_alerts where tenant_id=@TenantId and (@Status is null or status=@Status) order by case severity when 'CRITICAL' then 0 when 'HIGH' then 1 else 2 end,detected_at desc limit 200",new{_tenant.TenantId,Status=status},t)).ToArray(),ct));
    public Task ActOnAlertAsync(Guid id,string action,AlertAction command,CancellationToken ct)=>Guard("alert-action",()=>_database.InTenantTransactionAsync(async(c,t)=>{var status=action switch{"resolve"=>"RESOLVED","ignore"=>"IGNORED","snooze"=>"SNOOZED",_=>throw new ArgumentException("Ação inválida.")}; if(command.UserId==Guid.Empty)throw new ArgumentException("Usuário é obrigatório."); if(status=="SNOOZED"&&(command.Until is null||command.Until<=_clock.UtcNow))throw new ArgumentException("Adiamento deve ser futuro."); var n=await c.ExecuteAsync("update agro360.intelligence_alerts set status=@Status,resolved_by=@UserId,resolved_at=now(),snoozed_until=@Until,resolution_reason=@Reason where id=@Id and tenant_id=@TenantId and status in ('OPEN','SNOOZED')",new{id,_tenant.TenantId,status,command.UserId,command.Until,command.Reason},t);if(n==0)throw new KeyNotFoundException("Alerta não encontrado ou já encerrado.");await c.ExecuteAsync("insert into agro360.intelligence_alert_audit(id,tenant_id,alert_id,action,acted_by,acted_at,reason) values(gen_random_uuid(),@TenantId,@Id,@Status,@UserId,now(),@Reason)",new{id,_tenant.TenantId,status,command.UserId,command.Reason},t);},ct));
    public async Task<ExecutiveDashboard> GetExecutiveDashboardAsync(IntelligenceFilter filter,CancellationToken ct)=>new(await GetIndicatorsAsync(filter,ct),(await GetAlertsAsync("OPEN",ct)).Take(8).ToArray(),await GetForecastsAsync(filter,ct),_clock.UtcNow);
    public Task<IReadOnlyList<ForecastResult>> GetForecastsAsync(IntelligenceFilter filter,CancellationToken ct)=>Guard("forecasts",()=>_database.InTenantTransactionAsync(async(c,t)=>
    {
      Validate(filter); var p=Params(filter); var stock=(await c.QueryAsync("""select p.name,b.available,coalesce(avg(case when m.type='OUT' then m.quantity end),0) consumption from agro360.inventory_stock_balances b join agro360.inventory_products p on p.id=b.product_id left join agro360.inventory_stock_movements m on m.tenant_id=b.tenant_id and m.product_id=b.product_id and m.occurred_at>=now()-interval '30 days' where b.tenant_id=@TenantId group by p.name,b.available""",p,t)).ToArray();
      var rupture=stock.Where(x=>(decimal)x.consumption>0).Select(x=>new{Days=(decimal)x.available/((decimal)x.consumption/30m),Name=(string)x.name,Available=(decimal)x.available,Consumption=(decimal)x.consumption}).OrderBy(x=>x.Days).FirstOrDefault();
      var maintenance=await c.QueryFirstOrDefaultAsync("select count(*) total from agro360.fleet_maintenance_orders where tenant_id=@TenantId and status not in ('COMPLETED','CANCELLED') and coalesce(scheduled_for,next_review_date)<=current_date+30",p,t);
      var payable=await c.ExecuteScalarAsync<decimal>("select coalesce(sum(balance),0) from agro360.finance_payables where tenant_id=@TenantId and status in ('OPEN','PARTIAL') and due_on between current_date and current_date+30",p,t);
      var receivable=await c.ExecuteScalarAsync<decimal>("select coalesce(sum(balance),0) from agro360.finance_receivables where tenant_id=@TenantId and status in ('OPEN','PARTIAL') and due_on between current_date and current_date+30",p,t);
      var operational=await c.QuerySingleAsync("""select
        (select count(*) from agro360.inventory_stock_lots where tenant_id=@TenantId and quantity>0 and expires_on<=current_date+30) expiring,
        (select count(*) from agro360.agriculture_field_operations where tenant_id=@TenantId and status not in ('COMPLETED','CANCELLED') and executed_at<now()) activities,
        (select count(*) from agro360.storage_lots where tenant_id=@TenantId and status='BLOCKED') lots,
        (select count(*) from agro360.regional_logistics_trips where tenant_id=@TenantId and status not in ('COMPLETED','CANCELLED') and planned_start<=now()+interval '24 hours') trips
        """,p,t);
      var list=new List<ForecastResult>{new("stock-rupture",rupture is null?"INSUFFICIENT_DATA":rupture.Days<=30?"RISK":"NORMAL",rupture?.Days,"days",rupture is null?"Não há saídas suficientes nos últimos 30 dias para calcular consumo.":"Saldo atual dividido pelo consumo médio diário dos últimos 30 dias.",rupture is null?new Dictionary<string,object?>():new(){["product"]=rupture.Name,["available"]=rupture.Available,["consumption30Days"]=rupture.Consumption}),new("payables-30-days","CALCULATED",payable,"BRL","Soma dos saldos de contas a pagar com vencimento nos próximos 30 dias.",new Dictionary<string,object?>{{"windowDays",30}}),new("receivables-30-days","CALCULATED",receivable,"BRL","Soma dos saldos de contas a receber com vencimento nos próximos 30 dias.",new Dictionary<string,object?>{{"windowDays",30}}),new("maintenance-30-days","CALCULATED",(decimal)maintenance.total,"orders","Ordens abertas com revisão prevista nos próximos 30 dias.",new Dictionary<string,object?>{{"windowDays",30}})};
      list.AddRange([new("product-expiration","CALCULATED",Convert.ToDecimal(operational.expiring),"lots","Lotes de estoque com saldo e vencimento nos próximos 30 dias.",new Dictionary<string,object?>{{"windowDays",30}}),new("late-activities","CALCULATED",Convert.ToDecimal(operational.activities),"activities","Atividades abertas cuja data planejada já passou.",new Dictionary<string,object?>()),new("nonconforming-lots","CALCULATED",Convert.ToDecimal(operational.lots),"lots","Lotes atualmente bloqueados por não conformidade.",new Dictionary<string,object?>()),new("trip-window-risk","CALCULATED",Convert.ToDecimal(operational.trips),"trips","Viagens abertas com início planejado dentro de 24 horas.",new Dictionary<string,object?>{{"windowHours",24}})]);
      return (IReadOnlyList<ForecastResult>)list;
    },ct));

    public Task<AssistantAnswer> AskAsync(AssistantQuery query,CancellationToken ct)=>Guard("assistant",()=>_database.InTenantTransactionAsync(async(c,t)=>
    {
      var text=query.Question.Trim().ToLowerInvariant(); string intent,sql,answer,action;
      if(text.Contains("conta")&&text.Contains("venc")){intent="DUE_ACCOUNTS";sql="select supplier_name name,balance amount,due_on date,status from agro360.finance_payables where tenant_id=@TenantId and status in ('OPEN','PARTIAL') and due_on<=current_date+7 order by due_on limit 50";answer="Contas a pagar vencidas ou com vencimento nos próximos 7 dias.";action="Priorize as contas vencidas e valide disponibilidade de caixa.";}
      else if(text.Contains("estoque")||text.Contains("mínimo")||text.Contains("minimo")){intent="LOW_STOCK";sql="select p.name,b.available,b.minimum,b.unit from agro360.inventory_stock_balances b join agro360.inventory_products p on p.id=b.product_id where b.tenant_id=@TenantId and b.available<=b.minimum order by b.available/b.minimum nulls first limit 50";answer="Itens cujo saldo disponível atingiu ou ficou abaixo do mínimo.";action="Revise consumo e abra cotação para itens críticos.";}
      else if(text.Contains("manuten")){intent="MAINTENANCE";sql="select a.name,m.description,m.status,coalesce(m.scheduled_for,m.next_review_date) due_on from agro360.fleet_maintenance_orders m join agro360.fleet_assets a on a.id=m.asset_id where m.tenant_id=@TenantId and m.status not in ('COMPLETED','CANCELLED') and coalesce(m.scheduled_for,m.next_review_date)<=current_date+30 order by due_on limit 50";answer="Máquinas com manutenção vencida ou prevista em 30 dias.";action="Programe a parada antes do limite operacional.";}
      else if(text.Contains("viage")||text.Contains("rota")){intent="TRIP_RISK";sql="select number,planned_start,status from agro360.regional_logistics_trips where tenant_id=@TenantId and status not in ('COMPLETED','CANCELLED') and planned_start<=now()+interval '24 hours' order by planned_start limit 50";answer="Viagens abertas dentro da janela crítica de 24 horas.";action="Confirme veículo, rota e janela operacional.";}
      else if(text.Contains("lote")||text.Contains("conform")){intent="NONCONFORMING_LOTS";sql="select code,current_balance,status,block_reason from agro360.storage_lots where tenant_id=@TenantId and status='BLOCKED' order by formed_at limit 50";answer="Lotes bloqueados por pendência de conformidade.";action="Revise o motivo do bloqueio e registre a tratativa.";}
      else {intent="RISK_SUMMARY";sql="select type,severity,title,status,detected_at from agro360.intelligence_alerts where tenant_id=@TenantId and status='OPEN' order by detected_at desc limit 20";answer="Resumo dos riscos abertos encontrados na operação.";action="Trate primeiro alertas críticos e de alta severidade.";}
      var data=(await c.QueryAsync(new CommandDefinition(sql,new{_tenant.TenantId},t,cancellationToken:ct))).Cast<IDictionary<string,object?>>().Select(x=>(IReadOnlyDictionary<string,object?>)new Dictionary<string,object?>(x)).ToArray();
      return new AssistantAnswer(intent,data.Length==0?$"{answer} Nenhum registro foi encontrado.":$"{answer} {data.Length} registro(s) encontrado(s).",[action],data);
    },ct));

    public Task<IReadOnlyList<CustomDashboard>> GetDashboardsAsync(CancellationToken ct) => Guard("dashboards", () => _database.InTenantTransactionAsync(async (connection, transaction) =>
    {
        const string dashboardSql = """
        select id, name, description, shared_roles SharedRoles
        from agro360.intelligence_custom_dashboards
        where tenant_id = @TenantId
        order by name
        """;
        const string widgetSql = """
        select id, dashboard_id DashboardId, indicator_code IndicatorCode,
               farm_id FarmId, season_id SeasonId, position as Order, size
        from agro360.intelligence_dashboard_widgets
        where tenant_id = @TenantId
        order by position
        """;
        var parameters = new { _tenant.TenantId };
        var dashboards = (await connection.QueryAsync<DashboardRow>(
            new CommandDefinition(dashboardSql, parameters, transaction, cancellationToken: ct))).ToArray();
        var widgets = (await connection.QueryAsync<WidgetRow>(
            new CommandDefinition(widgetSql, parameters, transaction, cancellationToken: ct))).ToArray();

        return (IReadOnlyList<CustomDashboard>)dashboards.Select(dashboard => new CustomDashboard(
            dashboard.Id,
            dashboard.Name,
            dashboard.Description,
            dashboard.SharedRoles,
            widgets.Where(widget => widget.DashboardId == dashboard.Id)
                .Select(widget => new DashboardWidget(widget.Id, widget.IndicatorCode, widget.FarmId, widget.SeasonId, widget.Order, widget.Size))
                .ToArray())).ToArray();
    }, ct));
    public Task<Guid> SaveDashboardAsync(Guid? id,DashboardCommand command,Guid userId,CancellationToken ct)=>Guard("save-dashboard",()=>_database.InTenantTransactionAsync(async(c,t)=>{if(string.IsNullOrWhiteSpace(command.Name)||userId==Guid.Empty)throw new ArgumentException("Nome e usuário são obrigatórios.");var key=id??Guid.NewGuid();await c.ExecuteAsync("insert into agro360.intelligence_custom_dashboards(id,tenant_id,name,description,shared_roles,created_by) values(@Id,@TenantId,@Name,@Description,@Roles,@UserId) on conflict(id) do update set name=excluded.name,description=excluded.description,shared_roles=excluded.shared_roles,updated_at=now() where agro360.intelligence_custom_dashboards.tenant_id=@TenantId",new{Id=key,_tenant.TenantId,Name=command.Name.Trim(),command.Description,Roles=command.SharedRoles?.ToArray()??[],UserId=userId},t);return key;},ct));
    public Task<Guid> AddWidgetAsync(Guid dashboardId,WidgetCommand command,CancellationToken ct)=>Guard("add-widget",()=>_database.InTenantTransactionAsync(async(c,t)=>{if(!AllowedIndicators.Contains(command.IndicatorCode))throw new ArgumentException("Indicador não permitido.");var id=Guid.NewGuid();var n=await c.ExecuteAsync("insert into agro360.intelligence_dashboard_widgets(id,tenant_id,dashboard_id,indicator_code,farm_id,season_id,position,size) select @Id,@TenantId,id,@IndicatorCode,@FarmId,@SeasonId,@Order,@Size from agro360.intelligence_custom_dashboards where id=@DashboardId and tenant_id=@TenantId",new{id,_tenant.TenantId,dashboardId,command.IndicatorCode,command.FarmId,command.SeasonId,command.Order,command.Size},t);if(n==0)throw new KeyNotFoundException("Painel não encontrado.");return id;},ct));
    public Task DeleteWidgetAsync(Guid dashboardId,Guid widgetId,CancellationToken ct)=>Guard("delete-widget",()=>_database.InTenantTransactionAsync(async(c,t)=>{var n=await c.ExecuteAsync("delete from agro360.intelligence_dashboard_widgets where id=@WidgetId and dashboard_id=@DashboardId and tenant_id=@TenantId",new{widgetId,dashboardId,_tenant.TenantId},t);if(n==0)throw new KeyNotFoundException("Widget não encontrado.");},ct));

    private static readonly HashSet<string> AllowedIndicators=["revenue","expense","margin","cost-per-hectare","cost-per-animal","critical-stock","late-activities","overdue-maintenance","pending-receipts","late-shipments","certificates","nonconforming-lots","risky-trips"];
    private sealed record DashboardRow(Guid Id,string Name,string? Description,string[] SharedRoles);
    private sealed record WidgetRow(Guid Id,Guid DashboardId,string IndicatorCode,Guid? FarmId,Guid? SeasonId,int Order,string Size);
    private static IndicatorResult I(string c,string n,string category,decimal value,string unit,string explanation)=>new(c,n,category,value,unit,explanation);
    private object Params(IntelligenceFilter f)=>new{_tenant.TenantId,From=f.From??_clock.Today.AddMonths(-1),To=f.To??_clock.Today,FarmId=f.FarmId,SeasonId=f.SeasonId,Status=string.IsNullOrWhiteSpace(f.Status)?null:f.Status};
    private static void Validate(IntelligenceFilter f){if(f.From.HasValue&&f.To.HasValue&&f.From>f.To)throw new ArgumentException("Data inicial deve ser anterior à final.");if(f.From.HasValue&&f.To.HasValue&&f.To.Value.DayNumber-f.From.Value.DayNumber>3660)throw new ArgumentException("Período máximo é de 10 anos.");}
    private async Task<T> Guard<T>(string operation,Func<Task<T>> work){try{return await work();}catch(Exception ex){_logger.LogError(ex,"Falha na fronteira de inteligência {Operation} para tenant {TenantId}",operation,_tenant.TenantId);throw;}}
    private async Task Guard(string operation,Func<Task> work){try{await work();}catch(Exception ex){_logger.LogError(ex,"Falha na fronteira de inteligência {Operation} para tenant {TenantId}",operation,_tenant.TenantId);throw;}}
    private static string Csv(string x)=>$"\"{x.Replace("\"","\"\"")}\"";
    private static string ReportSql(string id)=>id switch
    {
      "current-stock"=>"select p.sku,p.name,w.name warehouse,b.available,b.reserved,b.minimum,b.unit,b.average_cost from agro360.inventory_stock_balances b join agro360.inventory_products p on p.id=b.product_id join agro360.inventory_warehouses w on w.id=b.warehouse_id where b.tenant_id=@TenantId and (@FarmId is null or w.farm_id=@FarmId) order by p.name",
      "stock-movements"=>"select p.name,m.type,m.quantity,m.unit,m.occurred_at,m.reason from agro360.inventory_stock_movements m join agro360.inventory_products p on p.id=m.product_id where m.tenant_id=@TenantId and m.occurred_at::date between @From and @To order by m.occurred_at desc",
      "payables"=>"select supplier_name,document,original_amount,balance,due_on,status from agro360.finance_payables where tenant_id=@TenantId and issued_on between @From and @To and (@Status is null or status=@Status) order by due_on",
      "receivables"=>"select customer_name,document,original_amount,balance,due_on,status from agro360.finance_receivables where tenant_id=@TenantId and issued_on between @From and @To and (@Status is null or status=@Status) order by due_on",
      "agricultural-activities" or "field-result"=>"select f.name field,s.name season,o.operation_type,o.status,o.area_ha,o.quantity,o.unit,o.executed_at from agro360.agriculture_field_operations o join agro360.geo_fields f on f.id=o.field_id join agro360.agriculture_seasons s on s.id=o.season_id where o.tenant_id=@TenantId and o.executed_at::date between @From and @To and (@FarmId is null or o.farm_id=@FarmId) and (@SeasonId is null or o.season_id=@SeasonId) and (@Status is null or o.status=@Status) order by o.executed_at desc",
      "machine-maintenance"=>"select a.name asset,m.type,m.description,m.status,m.scheduled_for,m.next_review_date,m.total_cost from agro360.fleet_maintenance_orders m join agro360.fleet_assets a on a.id=m.asset_id where m.tenant_id=@TenantId and coalesce(m.scheduled_for,m.created_at::date) between @From and @To and (@Status is null or m.status=@Status) order by coalesce(m.scheduled_for,m.next_review_date)",
      "receipts"=>"select number,supplier,product_id,status,gross_weight,tare,net_weight,created_at from agro360.storage_receipts where tenant_id=@TenantId and created_at::date between @From and @To and (@Status is null or status=@Status) order by created_at desc",
      "shipments"=>"select number,customer,destination,requested_quantity,loaded_quantity,status,created_at,dispatched_at from agro360.storage_shipments where tenant_id=@TenantId and created_at::date between @From and @To and (@Status is null or status=@Status) order by created_at desc",
      "regional-logistics"=>"select t.number,r.name route,t.planned_start,t.status from agro360.regional_logistics_trips t join agro360.regional_logistics_routes r on r.id=t.route_id where t.tenant_id=@TenantId and t.planned_start::date between @From and @To and (@Status is null or t.status=@Status) order by t.planned_start",
      "lot-traceability" or "product-quality"=>"select l.code,l.current_balance,l.status,l.block_reason,l.formed_at from agro360.storage_lots l where l.tenant_id=@TenantId and l.formed_at::date between @From and @To and (@Status is null or l.status=@Status) order by l.formed_at desc",
      "commissions-splits"=>"select gross_amount,status,provider_reference,approved_at,created_at from agro360.sales_network_revenue_splits where tenant_id=@TenantId and created_at::date between @From and @To and (@Status is null or status=@Status) order by created_at desc",
      "milk-production"=>"select produced_on,quantity_liters,discarded_liters,withdrawal_alert from agro360.livestock_milk_production where tenant_id=@TenantId and produced_on between @From and @To order by produced_on",
      "livestock-handling" or "animal-health" or "weight-gain" or "herd-result"=>"select a.tag,e.event_type,e.cost_amount,e.created_at from agro360.livestock_animal_events e join agro360.livestock_animals a on a.id=e.animal_id where e.tenant_id=@TenantId and e.created_at::date between @From and @To order by e.created_at desc",
      "financial-by-season" or "financial-by-property" or "cash-flow"=>"select occurred_on,source_type,amount,category,farm_id,season_id from agro360.cost_entries where tenant_id=@TenantId and occurred_on between @From and @To and (@FarmId is null or farm_id=@FarmId) and (@SeasonId is null or season_id=@SeasonId) order by occurred_on",
      "purchases-by-supplier"=>"select supplier_name,count(*) documents,sum(original_amount) total,sum(balance) balance from agro360.finance_payables where tenant_id=@TenantId and issued_on between @From and @To group by supplier_name order by total desc",
      "fueling"=>"select asset_id,quantity,unit_price,total,hour_meter,odometer,created_at from agro360.fleet_fuel_fillups where tenant_id=@TenantId and created_at::date between @From and @To order by created_at desc",
      _=>throw new ArgumentException("Relatório não suportado.")
    };
}
