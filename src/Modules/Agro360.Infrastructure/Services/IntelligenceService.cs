using System.Globalization;
using System.Text;
using System.Text.Json;
using Agro360.Application;
using Agro360.Application.Abstractions;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Infrastructure.Security;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
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

    public Task<IReadOnlyList<IndicatorResult>> GetIndicatorsAsync(IntelligenceFilter filter, CancellationToken ct) => Guard("indicators", () => _database.InTenantTransactionAsync(async (c, t) =>
    {
        Validate(filter);
        var p = Params(filter);
        const string sql = """
        select
          coalesce((select sum(original_amount-balance) from agro360.finance_receivables where tenant_id=@TenantId and issued_on between @From and @To),0) revenue,
          coalesce((select sum(original_amount-balance) from agro360.finance_payables where tenant_id=@TenantId and issued_on between @From and @To),0) expense,
          coalesce((select sum(recognized_amount) from agro360.cost_management_entries where tenant_id=@TenantId and competence_date between @From and @To and (@FarmId is null or farm_id=@FarmId)),0) cost,
          coalesce((select sum(ce.allocated_amount) from agro360.cost_management_entries ce where ce.tenant_id=@TenantId and ce.competence_date between @From and @To and (@FarmId is null or ce.farm_id=@FarmId)),0) allocated_cost,
          coalesce((select sum(planned_amount) from agro360.cost_management_entries where tenant_id=@TenantId and competence_date between @From and @To and (@FarmId is null or farm_id=@FarmId)),0) planned_cost,
          (select count(*) from agro360.livestock_animals a where a.tenant_id=@TenantId and a.deleted_at is null and a.status=1 and (@FarmId is null or a.farm_id=@FarmId)) animals,
          (select count(*) from agro360.inventory_stock_balances b join agro360.inventory_warehouses w on w.id=b.warehouse_id where b.tenant_id=@TenantId and b.available<=b.minimum and (@FarmId is null or w.farm_id=@FarmId)) critical_stock,
          (select count(*) from agro360.agriculture_field_operations o where o.tenant_id=@TenantId and o.status not in ('COMPLETED','CANCELLED') and o.executed_at<now() and (@FarmId is null or o.farm_id=@FarmId)) late_activities,
          (select count(*) from agro360.fleet_maintenance_orders m where m.tenant_id=@TenantId and m.status not in ('COMPLETED','CANCELLED') and coalesce(m.scheduled_for,m.next_review_date)<current_date) overdue_maintenance,
          (select count(*) from agro360.storage_receipts r where r.tenant_id=@TenantId and r.status not in ('UNLOADED','CANCELLED')) pending_receipts,
          (select count(*) from agro360.storage_shipments s where s.tenant_id=@TenantId and s.status not in ('DISPATCHED','CANCELLED') and s.created_at<now()-interval '2 days') late_shipments,
          (select count(*) from agro360.traceability_certificates x where x.tenant_id=@TenantId and x.revoked_at is null) certificates,
          (select count(*) from agro360.storage_lots l where l.tenant_id=@TenantId and l.status='BLOCKED') nonconforming_lots,
          (select count(*) from agro360.regional_logistics_trips x where x.tenant_id=@TenantId and x.status not in ('COMPLETED','CANCELLED') and x.planned_start<now()+interval '24 hours') risky_trips,
          (select count(*) from agro360.cost_management_entries where tenant_id=@TenantId and status='OPEN' and (@FarmId is null or farm_id=@FarmId)) pending_appropriations
        """;
        var command = new CommandDefinition(sql, p, t, cancellationToken: ct);
        var x = await c.QuerySingleAsync(command);
        decimal revenue = Convert.ToDecimal(x.revenue), expense = Convert.ToDecimal(x.expense), cost = Convert.ToDecimal(x.cost), allocatedCost = Convert.ToDecimal(x.allocated_cost), plannedCost = Convert.ToDecimal(x.planned_cost), hectares = (await c.ExecuteScalarAsync<decimal>(new CommandDefinition("select coalesce(sum(total_area_ha),0) from agro360.geo_farms where tenant_id=@TenantId and deleted_at is null and (@FarmId is null or id=@FarmId)", new { _tenant.TenantId, FarmId = filter.FarmId }, t, cancellationToken: ct))); int animals = Convert.ToInt32(x.animals);

        // Calculate efficiency criticality
        string efficiencyStatus = "NORMAL";
        decimal efficiency = plannedCost == 0 ? 0 : (cost / plannedCost) * 100;
        if (efficiency > 110) efficiencyStatus = "CRITICAL";
        else if (efficiency > 105) efficiencyStatus = "WARNING";

        return (IReadOnlyList<IndicatorResult>)new IndicatorResult[] {
          I("revenue","Receita no período","Financeiro",revenue,"BRL","Recebimentos baixados no período"), I("expense","Despesa no período","Financeiro",expense,"BRL","Pagamentos baixados no período"),
          I("margin","Margem realizada","Financeiro",revenue-expense-cost,"BRL","Receita menos despesas e custos operacionais"), I("cost-per-hectare","Custo por hectare","Agricultura",hectares==0?0:cost/hectares,"BRL/ha","Custos divididos pela área filtrada"),
          I("cost-efficiency","Eficiência de custo","Financeiro",efficiency,"%", $"Status: {efficiencyStatus}. Percentual do custo realizado vs planejado"), I("pending-appropriations","Pendências de apropriação","Financeiro",Convert.ToDecimal(x.pending_appropriations),"lançamentos","Custos reconhecidos ainda não apropriados"),
          I("cost-per-animal","Custo por animal","Pecuária",animals==0?0:cost/animals,"BRL/animal","Custos divididos pelos animais ativos"), I("critical-stock","Estoque crítico","Estoque",Convert.ToDecimal(x.critical_stock),"itens","Saldo disponível menor ou igual ao mínimo"),
          I("late-activities","Atividades atrasadas","Agricultura",Convert.ToDecimal(x.late_activities),"atividades","Atividades abertas com data planejada ultrapassada"), I("overdue-maintenance","Manutenções vencidas","Máquinas",Convert.ToDecimal(x.overdue_maintenance),"ordens","Ordens abertas após revisão programada"),
          I("pending-receipts","Romaneios pendentes","Armazenagem",Convert.ToDecimal(x.pending_receipts),"romaneios","Romaneios ainda não descarregados"), I("late-shipments","Expedições atrasadas","Logística",Convert.ToDecimal(x.late_shipments),"expedições","Expedições abertas há mais de 48 horas"),
          I("certificates","Certificados emitidos","Conformidade",Convert.ToDecimal(x.certificates),"certificados","Certificados vigentes"), I("nonconforming-lots","Lotes sem conformidade","Conformidade",Convert.ToDecimal(x.nonconforming_lots),"lotes","Lotes bloqueados"), I("risky-trips","Viagens em risco","Logística",Convert.ToDecimal(x.risky_trips),"viagens","Viagens abertas dentro da janela de 24 horas") };
    }, ct));

    public Task<IReadOnlyList<ReportDefinition>> GetReportsAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<ReportDefinition>>(Reports);
    public Task<ReportResult> RunReportAsync(string id, IntelligenceFilter filter, CancellationToken ct) => Guard("report", () => _database.InTenantTransactionAsync(async (c, t) =>
    {
        Validate(filter); if (!Reports.Any(x => x.Id == id)) throw new ArgumentException("Relatório desconhecido.", nameof(id));
        var sql = ReportSql(id); var rows = (await c.QueryAsync(new CommandDefinition(sql, Params(filter), t, cancellationToken: ct))).Cast<IDictionary<string, object?>>().Select(x => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(x, StringComparer.OrdinalIgnoreCase)).ToArray();
        return new ReportResult(id, rows.FirstOrDefault()?.Keys.ToArray() ?? [], rows, rows.Length);
    }, ct));
    public async Task<byte[]> ExportCsvAsync(string id, IntelligenceFilter filter, CancellationToken ct)
    {
        var report = await RunReportAsync(id, filter, ct);
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(",", report.Columns.Select(EscapeCsvField)));

        foreach (var row in report.Rows)
        {
            csv.AppendLine(string.Join(",", report.Columns.Select(column =>
                EscapeCsvField(Convert.ToString(row.GetValueOrDefault(column), CultureInfo.InvariantCulture) ?? string.Empty))));
        }

        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
    }

    public Task<IReadOnlyList<AlertResult>> GetAlertsAsync(string? status, CancellationToken ct) => Guard("alerts", () => _database.InTenantTransactionAsync(async (c, t) => (IReadOnlyList<AlertResult>)(await c.QueryAsync<AlertResult>("select id,type,severity,title,status,detected_at DetectedAt,snoozed_until SnoozedUntil from agro360.intelligence_legacy_alerts where tenant_id=@TenantId and (@Status is null or status=@Status) order by case severity when 'CRITICAL' then 0 when 'HIGH' then 1 else 2 end,detected_at desc limit 200", new { _tenant.TenantId, Status = status }, t)).ToArray(), ct));
    public Task ActOnAlertAsync(Guid id, string action, AlertAction command, CancellationToken ct) => Guard("alert-action", () => _database.InTenantTransactionAsync(async (c, t) => { var status = action switch { "resolve" => "RESOLVED", "ignore" => "IGNORED", "snooze" => "SNOOZED", _ => throw new ArgumentException("Ação inválida.") }; if (command.UserId == Guid.Empty) throw new ArgumentException("Usuário é obrigatório."); if (status == "SNOOZED" && (command.Until is null || command.Until <= _clock.UtcNow)) throw new ArgumentException("Adiamento deve ser futuro."); var n = await c.ExecuteAsync("update agro360.intelligence_legacy_alerts set status=@Status,resolved_by=@UserId,resolved_at=now(),snoozed_until=@Until,resolution_reason=@Reason where id=@Id and tenant_id=@TenantId and status in ('OPEN','SNOOZED')", new { id, _tenant.TenantId, status, command.UserId, command.Until, command.Reason }, t); if (n == 0) throw new KeyNotFoundException("Alerta não encontrado ou já encerrado."); await c.ExecuteAsync("insert into agro360.intelligence_legacy_alert_audit(id,tenant_id,alert_id,action,acted_by,acted_at,reason) values(gen_random_uuid(),@TenantId,@Id,@Status,@UserId,now(),@Reason)", new { id, _tenant.TenantId, status, command.UserId, command.Reason }, t); }, ct));
    public async Task<ExecutiveDashboard> GetExecutiveDashboardAsync(IntelligenceFilter filter, CancellationToken ct) => new(await GetIndicatorsAsync(filter, ct), (await GetAlertsAsync("OPEN", ct)).Take(8).ToArray(), await GetForecastsAsync(filter, ct), _clock.UtcNow);
    public Task<IReadOnlyList<ForecastResult>> GetForecastsAsync(IntelligenceFilter filter, CancellationToken ct) => Guard("forecasts", () => _database.InTenantTransactionAsync(async (c, t) =>
    {
        Validate(filter);
        var p = Params(filter);
        const string stockSql = """
        select p.name, b.available,
               coalesce(avg(case when m.type = 'OUT' then m.quantity end), 0) consumption
        from agro360.inventory_stock_balances b
        join agro360.inventory_products p on p.id = b.product_id
        left join agro360.inventory_stock_movements m
          on m.tenant_id = b.tenant_id
         and m.product_id = b.product_id
         and m.occurred_at >= now() - interval '30 days'
        where b.tenant_id = @TenantId
        group by p.name, b.available
        """;
        var stock = (await c.QueryAsync(new CommandDefinition(stockSql, p, t, cancellationToken: ct))).ToArray();
        var rupture = stock
            .Select(x => new { Name = (string)x.name, Available = ToDecimal(x.available), Consumption = ToDecimal(x.consumption) })
            .Where(x => x.Consumption > 0)
            .Select(x => new { Days = x.Available / (x.Consumption / 30m), x.Name, x.Available, x.Consumption })
            .OrderBy(x => x.Days)
            .FirstOrDefault();
        var maintenance = await c.QuerySingleAsync<CountRow>(new CommandDefinition(
            "select count(*) Total from agro360.fleet_maintenance_orders where tenant_id=@TenantId and status not in ('COMPLETED','CANCELLED') and coalesce(scheduled_for,next_review_date)<=current_date+30",
            p, t, cancellationToken: ct));
        var payable = await c.ExecuteScalarAsync<decimal>("select coalesce(sum(balance),0) from agro360.finance_payables where tenant_id=@TenantId and status in ('OPEN','PARTIAL') and due_on between current_date and current_date+30", p, t);
        var receivable = await c.ExecuteScalarAsync<decimal>("select coalesce(sum(balance),0) from agro360.finance_receivables where tenant_id=@TenantId and status in ('OPEN','PARTIAL') and due_on between current_date and current_date+30", p, t);
        const string operationalSql = """
        select
          (select count(*) from agro360.inventory_stock_lots where tenant_id = @TenantId and quantity > 0 and expires_on <= current_date + 30) expiring,
          (select count(*) from agro360.agriculture_field_operations where tenant_id = @TenantId and status not in ('COMPLETED','CANCELLED') and executed_at < now()) activities,
          (select count(*) from agro360.storage_lots where tenant_id = @TenantId and status = 'BLOCKED') lots,
          (select count(*) from agro360.regional_logistics_trips where tenant_id = @TenantId and status not in ('COMPLETED','CANCELLED') and planned_start <= now() + interval '24 hours') trips
        """;
        var operational = await c.QuerySingleAsync(new CommandDefinition(operationalSql, p, t, cancellationToken: ct));
        var forecasts = new List<ForecastResult> { new("stock-rupture", rupture is null ? "INSUFFICIENT_DATA" : rupture.Days <= 30 ? "RISK" : "NORMAL", rupture?.Days, "days", rupture is null ? "Não há saídas suficientes nos últimos 30 dias para calcular consumo." : "Saldo atual dividido pelo consumo médio diário dos últimos 30 dias.", rupture is null ? new Dictionary<string, object?>() : new() { ["product"] = rupture.Name, ["available"] = rupture.Available, ["consumption30Days"] = rupture.Consumption }), new("payables-30-days", "CALCULATED", payable, "BRL", "Soma dos saldos de contas a pagar com vencimento nos próximos 30 dias.", new Dictionary<string, object?> { { "windowDays", 30 } }), new("receivables-30-days", "CALCULATED", receivable, "BRL", "Soma dos saldos de contas a receber com vencimento nos próximos 30 dias.", new Dictionary<string, object?> { { "windowDays", 30 } }), new("maintenance-30-days", "CALCULATED", maintenance.Total, "orders", "Ordens abertas com revisão prevista nos próximos 30 dias.", new Dictionary<string, object?> { { "windowDays", 30 } }) };
        forecasts.AddRange([new("product-expiration", "CALCULATED", ToDecimal(operational.expiring), "lots", "Lotes de estoque com saldo e vencimento nos próximos 30 dias.", new Dictionary<string, object?> { { "windowDays", 30 } }), new("late-activities", "CALCULATED", ToDecimal(operational.activities), "activities", "Atividades abertas cuja data planejada já passou.", new Dictionary<string, object?>()), new("nonconforming-lots", "CALCULATED", ToDecimal(operational.lots), "lots", "Lotes atualmente bloqueados por não conformidade.", new Dictionary<string, object?>()), new("trip-window-risk", "CALCULATED", ToDecimal(operational.trips), "trips", "Viagens abertas com início planejado dentro de 24 horas.", new Dictionary<string, object?> { { "windowHours", 24 } })]);
        return (IReadOnlyList<ForecastResult>)forecasts;
    }, ct));

    public Task<AssistantAnswer> AskAsync(AssistantQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var text = AssistantQueryRules.NormalizeQuestion(query.Question);

        return Guard("assistant", () => _database.InTenantTransactionAsync(async (c, t) =>
        {
            string intent, permission, sql, answer, action, source;
            var tenantWideData = false;
            if (text.Contains("conta", StringComparison.OrdinalIgnoreCase) && text.Contains("venc", StringComparison.OrdinalIgnoreCase))
            {
                intent = "DUE_ACCOUNTS";
                permission = Permissions.FinanceRead;
                tenantWideData = true;
                sql = """
                    select supplier_name name,balance amount,due_on date,status,
                           case when due_on<current_date then 'OVERDUE' else 'DUE_WITHIN_7_DAYS' end due_category
                    from agro360.finance_payables
                    where tenant_id=@TenantId and status in ('OPEN','PARTIAL') and due_on<=current_date+7
                    order by due_on limit 50
                    """;
                answer = "Contas vencidas e contas com vencimento nos próximos 7 dias corridos.";
                action = "Priorize as contas vencidas e valide disponibilidade de caixa.";
                source = "Financeiro · contas a pagar";
            }
            else if (text.Contains("estoque", StringComparison.OrdinalIgnoreCase) || text.Contains("mínimo", StringComparison.OrdinalIgnoreCase) || text.Contains("minimo", StringComparison.OrdinalIgnoreCase))
            {
                intent = "LOW_STOCK";
                permission = Permissions.InventoryRead;
                sql = """
                    select p.name,b.available,b.minimum,b.unit
                    from agro360.inventory_stock_balances b
                    join agro360.inventory_products p on p.id=b.product_id and p.tenant_id=b.tenant_id
                    join agro360.inventory_warehouses w on w.id=b.warehouse_id and w.tenant_id=b.tenant_id
                    where b.tenant_id=@TenantId and b.available<=b.minimum
                      and (@FarmId is null or w.farm_id=@FarmId)
                    order by b.available/nullif(b.minimum,0) nulls first limit 50
                    """;
                answer = "Itens cujo saldo disponível atingiu ou ficou abaixo do mínimo na fazenda ativa.";
                action = "Revise consumo e prepare uma cotação para os itens críticos.";
                source = "Estoque · saldo disponível e mínimo dos armazéns";
            }
            else if (text.Contains("manuten", StringComparison.OrdinalIgnoreCase))
            {
                intent = "MAINTENANCE";
                permission = Permissions.MaintenanceRead;
                sql = """
                    select a.name,m.description,m.status,coalesce(m.scheduled_for,m.next_review_date) due_on
                    from agro360.fleet_maintenance_orders m
                    join agro360.fleet_assets a on a.id=m.asset_id and a.tenant_id=m.tenant_id
                    where m.tenant_id=@TenantId and m.status not in ('COMPLETED','CANCELLED')
                      and coalesce(m.scheduled_for,m.next_review_date)<=current_date+30
                      and (@FarmId is null or a.property_id=@FarmId)
                    order by due_on limit 50
                    """;
                answer = "Máquinas com manutenção vencida ou prevista nos próximos 30 dias na fazenda ativa.";
                action = "Programe a parada antes do limite operacional.";
                source = "Frota · ordens de manutenção";
            }
            else if (text.Contains("viage", StringComparison.OrdinalIgnoreCase) || text.Contains("rota", StringComparison.OrdinalIgnoreCase))
            {
                intent = "TRIP_RISK";
                permission = Permissions.RegionalLogisticsRead;
                tenantWideData = true;
                sql = """
                    select number,planned_start,status
                    from agro360.regional_logistics_trips
                    where tenant_id=@TenantId and status not in ('COMPLETED','CANCELLED')
                      and planned_start<=now()+interval '24 hours'
                    order by planned_start limit 50
                    """;
                answer = "Viagens abertas atrasadas ou com início planejado nas próximas 24 horas.";
                action = "Confirme veículo, rota e janela operacional.";
                source = "Logística regional · viagens abertas";
            }
            else if (text.Contains("lote", StringComparison.OrdinalIgnoreCase) || text.Contains("conform", StringComparison.OrdinalIgnoreCase))
            {
                intent = "NONCONFORMING_LOTS";
                permission = Permissions.StorageRead;
                sql = """
                    select l.code,l.current_balance,l.status,l.block_reason
                    from agro360.storage_lots l
                    join agro360.storage_structures s on s.id=l.structure_id and s.tenant_id=l.tenant_id
                    where l.tenant_id=@TenantId and l.status='BLOCKED'
                      and (@FarmId is null or s.property_id=@FarmId)
                    order by l.formed_at limit 50
                    """;
                answer = "Lotes bloqueados por pendência de conformidade na fazenda ativa.";
                action = "Revise o motivo do bloqueio e registre a tratativa.";
                source = "Armazenagem · lotes bloqueados e estruturas";
            }
            else
            {
                return new AssistantAnswer(
                    "UNSUPPORTED",
                    "Não reconheci uma consulta disponível. Pergunte sobre contas, estoque, manutenção, viagens ou lotes bloqueados.",
                    ["Use um dos exemplos disponíveis na tela."],
                    [],
                    "Nenhuma consulta executada");
            }

            var canReadAcrossTenant = await AuthorizeAssistantQueryAsync(c, t, permission, ct);
            if (tenantWideData && !canReadAcrossTenant)
                throw new ForbiddenException("Esta consulta não possui vínculo confiável com uma fazenda. Ela exige escopo de tenant completo.");
            if (!_tenant.FarmId.HasValue && !canReadAcrossTenant)
                throw new ForbiddenException("Selecione uma fazenda autorizada no contexto operacional antes de consultar estes dados.");

            var parameters = new { _tenant.TenantId, FarmId = _tenant.FarmId };
            var data = (await c.QueryAsync(new CommandDefinition(sql, parameters, t, cancellationToken: ct)))
                .Cast<IDictionary<string, object?>>()
                .Select(x => (IReadOnlyDictionary<string, object?>)new Dictionary<string, object?>(x))
                .ToArray();

            var response = intent == "DUE_ACCOUNTS"
                ? $"{data.Count(x => string.Equals(x.GetValueOrDefault("due_category")?.ToString(), "OVERDUE", StringComparison.Ordinal))} vencida(s); {data.Count(x => string.Equals(x.GetValueOrDefault("due_category")?.ToString(), "DUE_WITHIN_7_DAYS", StringComparison.Ordinal))} vence(m) nos próximos 7 dias corridos."
                : data.Length == 0 ? "Nenhum registro foi encontrado." : $"{data.Length} registro(s) encontrado(s).";
            return new AssistantAnswer(intent, $"{answer} {response}", [action], data, source);
        }, ct));
    }

    private async Task<bool> AuthorizeAssistantQueryAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        string permission,
        CancellationToken cancellationToken)
    {
        var modules = Permissions.ModulesForPermission(permission);
        if (modules.Length == 0)
            throw new InvalidOperationException($"Nenhum módulo foi associado à permissão '{permission}'.");

        var access = await connection.QuerySingleAsync<AssistantAccess>(new CommandDefinition(
            $"""
            select exists(
                       select 1 from agro360.identity_users u
                       join agro360.identity_user_roles ur on ur.tenant_id=u.tenant_id and ur.user_id=u.id
                       join agro360.identity_role_permissions rp on rp.tenant_id=ur.tenant_id and rp.role_id=ur.role_id
                       join agro360.identity_permissions p on p.id=rp.permission_id
                       where u.tenant_id=@TenantId and u.id=@UserId
                         and u.status='ACTIVE' and u.deleted_at is null and p.code=@Permission
                   ) HasPermission,
                   exists(
                       select 1 from ({EntitlementQueries.ModuleCodeSelect}) effective_modules
                       where module_code=any(@Modules)
                   ) HasContract,
                   exists(
                       select 1 from agro360.identity_user_unit_scopes s
                       where s.tenant_id=@TenantId and s.user_id=@UserId and s.scope_type='ALL'
                   ) HasAllUnitScope,
                   @FarmId is null or exists(
                       select 1 from agro360.geo_farms f
                       where f.tenant_id=@TenantId and f.id=@FarmId and f.deleted_at is null
                   ) FarmActive,
                   @FarmId is null or exists(
                       select 1 from agro360.geo_farms f
                       where f.tenant_id=@TenantId and f.id=@FarmId and f.deleted_at is null
                         and (
                             exists(select 1 from agro360.identity_user_unit_scopes s
                                    where s.tenant_id=@TenantId and s.user_id=@UserId
                                      and s.scope_type='FARM' and s.farm_id=f.id)
                             or exists(select 1 from agro360.identity_user_unit_scopes s
                                       where s.tenant_id=@TenantId and s.user_id=@UserId
                                         and s.scope_type='ORGANIZATION' and s.organization_id=f.organization_id)
                         )
                   ) FarmInScope
            """,
            new { _tenant.TenantId, UserId = _tenant.UserId, FarmId = _tenant.FarmId, Permission = permission, Modules = modules },
            transaction,
            cancellationToken: cancellationToken));

        if (!access.HasPermission)
            throw new ForbiddenException("Seu perfil não possui permissão efetiva para consultar este módulo.");
        if (!access.HasContract)
            throw new ForbiddenException("O módulo consultado não está contratado ou vigente para esta organização.");
        if (!access.FarmActive)
            throw new ForbiddenException("A fazenda ativa não pertence ao tenant ou está inativa.");
        if (!access.HasAllUnitScope && !access.FarmInScope)
            throw new ForbiddenException("A fazenda ativa não está no escopo concedido ao usuário.");

        return access.HasAllUnitScope;
    }

    private sealed class AssistantAccess
    {
        public bool HasPermission { get; init; }
        public bool HasContract { get; init; }
        public bool HasAllUnitScope { get; init; }
        public bool FarmActive { get; init; }
        public bool FarmInScope { get; init; }
    }

    public Task<IReadOnlyList<CustomDashboard>> GetDashboardsAsync(CancellationToken ct) => Guard("dashboards", () => _database.InTenantTransactionAsync(async (connection, transaction) =>
    {
        const string dashboardSql = """
        select id, name, description, shared_roles SharedRoles
        from agro360.ai_dashboards
        where tenant_id = @TenantId
        order by name
        """;
        const string widgetSql = """
        select id, dashboard_id DashboardId, indicator_code IndicatorCode,
               farm_id FarmId, season_id SeasonId, position as Order, size
        from agro360.ai_dashboard_widgets
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
    public Task<Guid> SaveDashboardAsync(Guid? id, DashboardCommand command, Guid userId, CancellationToken ct) =>
        Guard("save-dashboard", () => _database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            if (id == Guid.Empty)
                throw new ArgumentException("Identificador do painel inválido.", nameof(id));
            if (string.IsNullOrWhiteSpace(command.Name) || userId == Guid.Empty)
                throw new ArgumentException("Nome e usuário são obrigatórios.");

            var dashboardId = id ?? Guid.NewGuid();
            const string sql = """
            insert into agro360.ai_dashboards
                (id, tenant_id, name, description, shared_roles, created_by)
            values
                (@Id, @TenantId, @Name, @Description, @Roles, @UserId)
            on conflict (id) do update
            set name = excluded.name,
                description = excluded.description,
                shared_roles = excluded.shared_roles,
                updated_at = now()
            where agro360.ai_dashboards.tenant_id = @TenantId
            """;
            var parameters = new
            {
                Id = dashboardId,
                _tenant.TenantId,
                Name = command.Name.Trim(),
                command.Description,
                Roles = command.SharedRoles?.ToArray() ?? [],
                UserId = userId
            };
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters, transaction, cancellationToken: ct));
            return dashboardId;
        }, ct));

    public Task<Guid> AddWidgetAsync(Guid dashboardId, WidgetCommand command, CancellationToken ct) =>
        Guard("add-widget", () => _database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            if (dashboardId == Guid.Empty)
                throw new ArgumentException("Identificador do painel inválido.", nameof(dashboardId));
            if (string.IsNullOrWhiteSpace(command.IndicatorCode) || !AllowedIndicators.Contains(command.IndicatorCode))
                throw new ArgumentException("Indicador não permitido.");
            if (!AllowedWidgetSizes.Contains(command.Size))
                throw new ArgumentException("Tamanho do widget inválido.", nameof(command));

            var widgetId = Guid.NewGuid();
            const string sql = """
            insert into agro360.ai_dashboard_widgets
                (id, tenant_id, dashboard_id, indicator_code, farm_id, season_id, position, size)
            select @WidgetId, @TenantId, id, @IndicatorCode, @FarmId, @SeasonId, @Order, @Size
            from agro360.ai_dashboards
            where id = @DashboardId
              and tenant_id = @TenantId
            """;
            var parameters = new
            {
                WidgetId = widgetId,
                _tenant.TenantId,
                DashboardId = dashboardId,
                command.IndicatorCode,
                command.FarmId,
                command.SeasonId,
                command.Order,
                command.Size
            };
            var affected = await connection.ExecuteAsync(
                new CommandDefinition(sql, parameters, transaction, cancellationToken: ct));
            if (affected == 0)
                throw new KeyNotFoundException("Painel não encontrado.");

            return widgetId;
        }, ct));

    public Task DeleteWidgetAsync(Guid dashboardId, Guid widgetId, CancellationToken ct) =>
        Guard("delete-widget", () => _database.InTenantTransactionAsync(async (connection, transaction) =>
        {
            if (dashboardId == Guid.Empty)
                throw new ArgumentException("Identificador do painel inválido.", nameof(dashboardId));
            if (widgetId == Guid.Empty)
                throw new ArgumentException("Identificador do widget inválido.", nameof(widgetId));

            const string sql = """
            delete from agro360.ai_dashboard_widgets
            where id = @WidgetId
              and dashboard_id = @DashboardId
              and tenant_id = @TenantId
            """;
            var affected = await connection.ExecuteAsync(new CommandDefinition(
                sql,
                new { WidgetId = widgetId, DashboardId = dashboardId, _tenant.TenantId },
                transaction,
                cancellationToken: ct));
            if (affected == 0)
                throw new KeyNotFoundException("Widget não encontrado.");
        }, ct));

    private static readonly HashSet<string> AllowedIndicators = ["revenue", "expense", "margin", "cost-per-hectare", "cost-per-animal", "critical-stock", "late-activities", "overdue-maintenance", "pending-receipts", "late-shipments", "certificates", "nonconforming-lots", "risky-trips"];
    private static readonly HashSet<string> AllowedWidgetSizes = new(StringComparer.Ordinal) { "SMALL", "MEDIUM", "LARGE" };

    public async Task<OperationVariance> GetOperationVarianceAsync(Guid orderId, CancellationToken ct)
    {
        return await _database.InTenantTransactionAsync(async (c, t) =>
        {
            var order = await c.QuerySingleOrDefaultAsync<OrderRow>(new CommandDefinition("""
                select id, data, status
                from agro360.agriculture_records
                where tenant_id=@TenantId and id=@OrderId and module='work-orders'
                """, new { _tenant.TenantId, OrderId = orderId }, t, cancellationToken: ct)) ?? throw new NotFoundException("Ordem de campo", orderId);

            var data = JsonSerializer.Deserialize<Dictionary<string, object?>>(order.Data, JsonOptions) ?? [];
            decimal plannedCost = ToDecimal(data.GetValueOrDefault("estimatedCost"));

            // Actual cost: sum of all recognized costs for this order
            decimal actualCost = await c.ExecuteScalarAsync<decimal>(new CommandDefinition("""
                select coalesce(sum(recognized_amount), 0)
                from agro360.cost_management_entries
                where tenant_id=@TenantId and source_key=@OrderId
                """, new { _tenant.TenantId, OrderId = orderId.ToString() }, t, cancellationToken: ct));

            // Physical Progress: (Executed Area / Planned Area)
            var progress = await c.ExecuteScalarAsync<decimal>(new CommandDefinition("""
                select
                  case
                    when planned_area = 0 then 0
                    else coalesce(sum(performed_quantity), 0) / planned_area
                  end
                from (
                  select
                    (data->>'plannedArea')::decimal as planned_area,
                    (select coalesce(sum(performed_quantity), 0) from agro360.field_work_logs where tenant_id=@TenantId and work_order_id=@OrderId) as performed
                  from agro360.agriculture_records
                  where tenant_id=@TenantId and id=@OrderId
                ) s
                """, new { _tenant.TenantId, OrderId = orderId }, t, cancellationToken: ct));

            decimal physicalProgress = Math.Clamp(progress, 0, 1);
            decimal costProgress = plannedCost == 0 ? 0 : (actualCost / plannedCost);

            string status = "NORMAL";
            string warning = "";

            if (plannedCost > 0)
            {
                // Variance: Cost progress is significantly ahead of physical progress
                decimal variance = costProgress - physicalProgress;
                if (variance > 0.2m) // 20% deviation
                {
                    status = "CRITICAL";
                    warning = $"Custo consumido ({costProgress:P}) está muito acima do progresso físico ({physicalProgress:P}).";
                }
                else if (variance > 0.1m)
                {
                    status = "WARNING";
                    warning = $"Custo consumido ({costProgress:P}) está acima do esperado para o progresso físico ({physicalProgress:P}).";
                }
            }

            return new OperationVariance(orderId.ToString(), plannedCost, actualCost, physicalProgress, costProgress, status, warning);
        }, ct);
    }

    private sealed record OrderRow(Guid Id, string Data, string Status);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record DashboardRow(Guid Id, string Name, string? Description, string[] SharedRoles);
    private sealed record WidgetRow(Guid Id, Guid DashboardId, string IndicatorCode, Guid? FarmId, Guid? SeasonId, int Order, string Size);
    private sealed record CountRow(long Total);
    private static IndicatorResult I(string c, string n, string category, decimal value, string unit, string explanation) => new(c, n, category, value, unit, explanation);
    private object Params(IntelligenceFilter f) => new { _tenant.TenantId, From = f.From ?? _clock.Today.AddMonths(-1), To = f.To ?? _clock.Today, FarmId = f.FarmId, SeasonId = f.SeasonId, Status = string.IsNullOrWhiteSpace(f.Status) ? null : f.Status };
    private static void Validate(IntelligenceFilter f) { if (f.From.HasValue && f.To.HasValue && f.From > f.To) throw new ArgumentException("Data inicial deve ser anterior à final."); if (f.From.HasValue && f.To.HasValue && f.To.Value.DayNumber - f.From.Value.DayNumber > 3660) throw new ArgumentException("Período máximo é de 10 anos."); }
    private async Task<T> Guard<T>(string operation, Func<Task<T>> work) { try { return await work(); } catch (Exception ex) { InfrastructureLogMessages.IntelligenceFailed(_logger, operation, _tenant.TenantId, ex); throw; } }
    private async Task Guard(string operation, Func<Task> work) { try { await work(); } catch (Exception ex) { InfrastructureLogMessages.IntelligenceFailed(_logger, operation, _tenant.TenantId, ex); throw; } }
    private static decimal ToDecimal(object? value) => value switch
    {
        null => 0m,
        decimal number => number,
        double number when double.IsFinite(number) => Convert.ToDecimal(number),
        int number => number,
        long number => number,
        string text when decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) => number,
        _ => throw new InvalidCastException($"O valor do tipo {value?.GetType().Name ?? "null"} não pode ser convertido para decimal.")
    };
    private static string EscapeCsvField(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var escaped = value.Replace("\"", "\"\"", StringComparison.Ordinal);

        return escaped.Contains(',', StringComparison.Ordinal)
            || escaped.Contains('"', StringComparison.Ordinal)
            || escaped.Contains('\n', StringComparison.Ordinal)
            || escaped.Contains('\r', StringComparison.Ordinal)
                ? $"\"{escaped}\""
                : escaped;
    }
    private static string ReportSql(string id) => id switch
    {
        "current-stock" => "select p.sku,p.name,w.name warehouse,b.available,b.reserved,b.minimum,b.unit,b.average_cost from agro360.inventory_stock_balances b join agro360.inventory_products p on p.id=b.product_id join agro360.inventory_warehouses w on w.id=b.warehouse_id where b.tenant_id=@TenantId and (@FarmId is null or w.farm_id=@FarmId) order by p.name",
        "stock-movements" => "select p.name,m.type,m.quantity,m.unit,m.occurred_at,m.reason from agro360.inventory_stock_movements m join agro360.inventory_products p on p.id=m.product_id where m.tenant_id=@TenantId and m.occurred_at::date between @From and @To order by m.occurred_at desc",
        "payables" => "select supplier_name,document,original_amount,balance,due_on,status from agro360.finance_payables where tenant_id=@TenantId and issued_on between @From and @To and (@Status is null or status=@Status) order by due_on",
        "receivables" => "select customer_name,document,original_amount,balance,due_on,status from agro360.finance_receivables where tenant_id=@TenantId and issued_on between @From and @To and (@Status is null or status=@Status) order by due_on",
        "agricultural-activities" or "field-result" => "select f.name field,s.name season,o.operation_type,o.status,o.area_ha,o.quantity,o.unit,o.executed_at from agro360.agriculture_field_operations o join agro360.geo_fields f on f.id=o.field_id join agro360.agriculture_seasons s on s.id=o.season_id where o.tenant_id=@TenantId and o.executed_at::date between @From and @To and (@FarmId is null or o.farm_id=@FarmId) and (@SeasonId is null or o.season_id=@SeasonId) and (@Status is null or o.status=@Status) order by o.executed_at desc",
        "machine-maintenance" => "select a.name asset,m.type,m.description,m.status,m.scheduled_for,m.next_review_date,m.total_cost from agro360.fleet_maintenance_orders m join agro360.fleet_assets a on a.id=m.asset_id where m.tenant_id=@TenantId and coalesce(m.scheduled_for,m.created_at::date) between @From and @To and (@Status is null or m.status=@Status) order by coalesce(m.scheduled_for,m.next_review_date)",
        "receipts" => "select number,supplier,product_id,status,gross_weight,tare,net_weight,created_at from agro360.storage_receipts where tenant_id=@TenantId and created_at::date between @From and @To and (@Status is null or status=@Status) order by created_at desc",
        "shipments" => "select number,customer,destination,requested_quantity,loaded_quantity,status,created_at,dispatched_at from agro360.storage_shipments where tenant_id=@TenantId and created_at::date between @From and @To and (@Status is null or status=@Status) order by created_at desc",
        "regional-logistics" => "select t.number,r.name route,t.planned_start,t.status from agro360.regional_logistics_trips t join agro360.regional_logistics_routes r on r.id=t.route_id where t.tenant_id=@TenantId and t.planned_start::date between @From and @To and (@Status is null or t.status=@Status) order by t.planned_start",
        "lot-traceability" or "product-quality" => "select l.code,l.current_balance,l.status,l.block_reason,l.formed_at from agro360.storage_lots l where l.tenant_id=@TenantId and l.formed_at::date between @From and @To and (@Status is null or l.status=@Status) order by l.formed_at desc",
        "commissions-splits" => "select gross_amount,status,provider_reference,approved_at,created_at from agro360.sales_network_revenue_splits where tenant_id=@TenantId and created_at::date between @From and @To and (@Status is null or status=@Status) order by created_at desc",
        "milk-production" => "select produced_on,quantity_liters,discarded_liters,withdrawal_alert from agro360.livestock_milk_production where tenant_id=@TenantId and produced_on between @From and @To order by produced_on",
        "livestock-handling" or "animal-health" or "weight-gain" or "herd-result" => "select a.tag,e.event_type,e.cost_amount,e.created_at from agro360.livestock_animal_events e join agro360.livestock_animals a on a.id=e.animal_id where e.tenant_id=@TenantId and e.created_at::date between @From and @To order by e.created_at desc",
        "financial-by-season" or "financial-by-property" or "cash-flow" => "select occurred_on,source_type,amount,category,farm_id,season_id from agro360.cost_entries where tenant_id=@TenantId and occurred_on between @From and @To and (@FarmId is null or farm_id=@FarmId) and (@SeasonId is null or season_id=@SeasonId) order by occurred_on",
        "purchases-by-supplier" => "select supplier_name,count(*) documents,sum(original_amount) total,sum(balance) balance from agro360.finance_payables where tenant_id=@TenantId and issued_on between @From and @To group by supplier_name order by total desc",
        "fueling" => "select asset_id,quantity,unit_price,total,hour_meter,odometer,created_at from agro360.fleet_fuel_fillups where tenant_id=@TenantId and created_at::date between @From and @To order by created_at desc",
        _ => throw new ArgumentException("Relatório não suportado.")
    };
}
