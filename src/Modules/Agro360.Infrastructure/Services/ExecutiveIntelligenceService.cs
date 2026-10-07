using System.Globalization;
using System.Text;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
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

    public Task<Guid> CreateKpiAsync(KpiDefinitionCommand command, Guid userId, bool canManageStrategic, CancellationToken cancellationToken) => Guard("create-kpi", () => database.InTenantTransactionAsync(async (c, t) =>
    {
        if (!Sources.Contains(command.DataSource)) throw new ArgumentException("Fonte de dados não suportada.");
        if (command.Strategic && !canManageStrategic) throw new UnauthorizedAccessException("Permissão para indicador estratégico ausente.");
        if (command.Unit == "PERCENT" && new[] { command.Target, command.AttentionLimit, command.CriticalLimit }.Any(v => v is < 0 or > 100)) throw new ArgumentException("Percentuais devem estar entre 0 e 100.");
        var id = Guid.NewGuid();
        await c.ExecuteAsync(new CommandDefinition("insert into agro360.intelligence_kpi_definitions(id,tenant_id,name,code,category,formula,data_source,periodicity,unit,target,attention_limit,critical_limit,active,canManageStrategic,created_by,updated_by) values(@Id,@TenantId,@Name,@Code,@Category,@Formula,@DataSource,@Periodicity,@Unit,@Target,@AttentionLimit,@CriticalLimit,@Active,@Strategic,@UserId,@UserId)", new { Id = id, tenant.TenantId, Name = command.Name.Trim(), Code = command.Code.Trim(), command.Category, command.Formula, command.DataSource, command.Periodicity, command.Unit, command.Target, command.AttentionLimit, command.CriticalLimit, command.Active, command.Strategic, UserId = userId }, t, cancellationToken: cancellationToken)); return id;
    }, cancellationToken));

    public Task RecalculateAsync(Guid id, Guid userId, CancellationToken cancellationToken) => Guard("recalculate", () => database.InTenantTransactionAsync(async (c, t) =>
    {
        var source = await c.ExecuteScalarAsync<string?>(new CommandDefinition("select data_source from agro360.intelligence_kpi_definitions where id=@Id and tenant_id=@TenantId and active and deleted_at is null", new { Id = id, tenant.TenantId }, t, cancellationToken: cancellationToken));
        if (string.IsNullOrEmpty(source)) throw new KeyNotFoundException("Indicador não encontrado.");
        // Formula identifiers are allow-listed; client SQL is never executed. Connectors populate source_value.
        await c.ExecuteAsync(new CommandDefinition("insert into agro360.intelligence_kpi_snapshots(id,tenant_id,kpi_id,reference_date,status,source,calculation_error,calculated_by,created_by,updated_by) values(gen_random_uuid(),@TenantId,@Id,current_date,'UNAVAILABLE',@Source,'Conector sem valor disponível para o período.',@UserId,@UserId,@UserId)", new { tenant.TenantId, Id = id, Source = source, UserId = userId }, t, cancellationToken: cancellationToken));
    }, cancellationToken));

    public Task DecideAlertAsync(Guid id, AlertDecisionCommand command, Guid userId, CancellationToken cancellationToken) => Guard("alert-decision", () => database.InTenantTransactionAsync(async (c, t) =>
    {
        var severity = await c.ExecuteScalarAsync<string?>(new CommandDefinition("select severity from agro360.intelligence_alerts where id=@Id and tenant_id=@TenantId", new { Id = id, tenant.TenantId }, t, cancellationToken: cancellationToken)) ?? throw new KeyNotFoundException("Alerta não encontrado.");
        if (command.Status == "RESOLVED" && string.IsNullOrWhiteSpace(command.Comment)) throw new ArgumentException("Resolver alerta exige comentário.");
        if (command.Status == "IGNORED" && severity == "CRITICAL" && string.IsNullOrWhiteSpace(command.Comment)) throw new ArgumentException("Alerta crítico exige justificativa para ser ignorado.");
        if (command.Status == "ASSIGNED" && !command.ResponsibleId.HasValue) throw new ArgumentException("Alerta atribuído exige responsável.");
        await c.ExecuteAsync(new CommandDefinition("update agro360.intelligence_alerts set status=@Status,responsible_id=@ResponsibleId,resolved_at=case when @Status='RESOLVED' then now() else resolved_at end,resolved_by=case when @Status='RESOLVED' then @UserId else resolved_by end,updated_at=now(),updated_by=@UserId where id=@Id and tenant_id=@TenantId;insert into agro360.intelligence_alert_events(id,tenant_id,alert_id,event_type,comment,created_by,updated_by) values(gen_random_uuid(),@TenantId,@Id,@Status,@Comment,@UserId,@UserId)", new { Id = id, tenant.TenantId, command.Status, command.ResponsibleId, command.Comment, UserId = userId }, t, cancellationToken: cancellationToken));
    }, cancellationToken));

    public Task DecideRecommendationAsync(Guid id, RecommendationStatusCommand command, Guid userId, CancellationToken cancellationToken) => Guard("recommendation-decision", () => database.InTenantTransactionAsync(async (c, t) =>
    { var severity = await c.ExecuteScalarAsync<string?>(new CommandDefinition("select severity from agro360.intelligence_recommendations where id=@Id and tenant_id=@TenantId", new { Id = id, tenant.TenantId }, t, cancellationToken: cancellationToken)) ?? throw new KeyNotFoundException("Recomendação não encontrada."); if (command.Status == "REJECTED" && (severity == "HIGH" || severity == "CRITICAL") && string.IsNullOrWhiteSpace(command.Reason)) throw new ArgumentException("Rejeição de recomendação alta ou crítica exige motivo."); await c.ExecuteAsync(new CommandDefinition("update agro360.intelligence_recommendations set status=@Status,decision_reason=@Reason,updated_at=now(),updated_by=@UserId where id=@Id and tenant_id=@TenantId;insert into agro360.intelligence_recommendation_events(id,tenant_id,recommendation_id,event_type,reason,created_by,updated_by) values(gen_random_uuid(),@TenantId,@Id,@Status,@Reason,@UserId,@UserId)", new { Id = id, tenant.TenantId, command.Status, command.Reason, UserId = userId }, t, cancellationToken: cancellationToken)); }, cancellationToken));

    public Task<byte[]> ExportAsync(string report, IntelligencePageFilter filter, Guid userId, CancellationToken cancellationToken) =>
        Guard("export", () => database.InTenantTransactionAsync(async (c, t) =>
        {
            ArgumentNullException.ThrowIfNull(filter);
            if (userId == Guid.Empty)
                throw new ArgumentException("Usuário inválido.", nameof(userId));

            var reportId = report?.Trim().ToLowerInvariant();
            var spec = reportId switch
            {
                "indicators" => new ExportSpec("Indicadores", ["id", "code", "name", "category", "unit", "target", "active"], """
                    select id,code,name,category,unit,target,active
                    from agro360.intelligence_kpi_definitions
                    where tenant_id=@TenantId and deleted_at is null
                      and (@Status is null or case when active then 'ACTIVE' else 'INACTIVE' end=@Status)
                      and (@Module is null or category ilike @Module)
                      and (@Search is null or code ilike @SearchPattern or name ilike @SearchPattern)
                    order by category,name,id
                    limit @PageSize offset @Offset
                    """),
                "snapshots" => new ExportSpec("Medições de indicadores", ["code", "name", "reference_date", "status", "calculated_value", "target", "source", "calculation_error", "calculated_at"], """
                    select d.code,d.name,s.reference_date,s.status,s.calculated_value,d.target,s.source,s.calculation_error,s.calculated_at
                    from agro360.intelligence_kpi_snapshots s
                    join agro360.intelligence_kpi_definitions d on d.id=s.kpi_id and d.tenant_id=s.tenant_id
                    where s.tenant_id=@TenantId and d.deleted_at is null
                      and (@Status is null or s.status=@Status)
                      and (@Module is null or d.category ilike @Module)
                      and (@Search is null or d.code ilike @SearchPattern or d.name ilike @SearchPattern)
                    order by s.reference_date desc,s.created_at desc
                    limit @PageSize offset @Offset
                    """),
                "alerts" => new ExportSpec("Alertas executivos", ["id", "type", "category", "severity", "source_module", "origin", "description", "recommendation", "status", "due_at", "created_at"], """
                    select id,type,category,severity,source_module,origin,description,recommendation,status,due_at,created_at
                    from agro360.intelligence_alerts
                    where tenant_id=@TenantId
                      and (@Status is null or status=@Status)
                      and (@Severity is null or severity=@Severity)
                      and (@Module is null or source_module ilike @Module)
                      and (@Search is null or type ilike @SearchPattern or origin ilike @SearchPattern or description ilike @SearchPattern)
                    order by created_at desc,id
                    limit @PageSize offset @Offset
                    """),
                "risks" => new ExportSpec("Riscos executivos", ["id", "type", "source_module", "cause", "severity", "impact", "recommendation", "detected_at", "status", "created_at"], """
                    select id,type,source_module,cause,severity,impact,recommendation,detected_at,status,created_at
                    from agro360.intelligence_risks
                    where tenant_id=@TenantId and deleted_at is null
                      and (@Status is null or status=@Status)
                      and (@Severity is null or severity=@Severity)
                      and (@Module is null or source_module ilike @Module)
                      and (@Search is null or type ilike @SearchPattern or cause ilike @SearchPattern or impact ilike @SearchPattern)
                    order by detected_at desc,id
                    limit @PageSize offset @Offset
                    """),
                "recommendations" => new ExportSpec("Recomendações executivas", ["id", "title", "description", "severity", "source_module", "related_entity_type", "related_entity_id", "status", "decision_reason", "created_at"], """
                    select id,title,description,severity,source_module,related_entity_type,related_entity_id,status,decision_reason,created_at
                    from agro360.intelligence_recommendations
                    where tenant_id=@TenantId and deleted_at is null
                      and (@Status is null or status=@Status)
                      and (@Severity is null or severity=@Severity)
                      and (@Module is null or source_module ilike @Module)
                      and (@Search is null or title ilike @SearchPattern or description ilike @SearchPattern)
                    order by created_at desc,id
                    limit @PageSize offset @Offset
                    """),
                "audit" => new ExportSpec("Auditoria de inteligência", ["id", "module", "entity_type", "entity_id", "action", "user_id", "correlation_id", "created_at"], """
                    select id,module,entity_type,entity_id,action,user_id,correlation_id,created_at
                    from agro360.intelligence_audit_events
                    where tenant_id=@TenantId
                      and (@Module is null or module ilike @Module)
                      and (@Search is null or entity_type ilike @SearchPattern or action ilike @SearchPattern)
                    order by created_at desc,id
                    limit @PageSize offset @Offset
                    """),
                _ => throw new ArgumentException("Relatório não suportado.", nameof(report))
            };

            var page = Math.Clamp(filter.Page, 1, 500);
            var pageSize = Math.Clamp(filter.PageSize, 1, 100);
            var search = NormalizeFilter(filter.Search, 160);
            var status = NormalizeFilter(filter.Status, 40);
            var severity = NormalizeFilter(filter.Severity, 40);
            var module = NormalizeFilter(filter.Module, 40);
            var parameters = new
            {
                tenant.TenantId,
                Status = status,
                Severity = severity,
                Module = module,
                Search = search,
                SearchPattern = search is null ? null : $"%{search}%",
                PageSize = pageSize,
                Offset = (page - 1) * pageSize
            };
            var rows = (await c.QueryAsync(new CommandDefinition(spec.Sql, parameters, t, cancellationToken: cancellationToken)))
                .Cast<IDictionary<string, object?>>()
                .ToArray();

            var csv = new StringBuilder();
            csv.AppendLine(Csv($"Relatório: {spec.Title}"));
            csv.AppendLine(string.Join(';', spec.Columns.Select(Csv)));
            foreach (var row in rows)
            {
                csv.AppendLine(string.Join(';', spec.Columns.Select(column =>
                    Csv(row.TryGetValue(column, out var value) ? value : null))));
            }

            var filtersJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                page,
                pageSize,
                search,
                status,
                severity,
                module
            });
            await c.ExecuteAsync(new CommandDefinition(
                "insert into agro360.intelligence_report_exports(id,tenant_id,report,filters,row_count,created_by) values(gen_random_uuid(),@TenantId,@Report,cast(@Filters as jsonb),@Count,@UserId)",
                new { tenant.TenantId, Report = reportId, Filters = filtersJson, Count = rows.Length, UserId = userId },
                t,
                cancellationToken: cancellationToken));

            return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray();
        }, cancellationToken));

    private sealed record ExportSpec(string Title, string[] Columns, string Sql);

    private static string? NormalizeFilter(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
            throw new ArgumentException($"O filtro deve ter no máximo {maximumLength} caracteres.");
        return normalized;
    }
    private static string Csv(object? value) => value switch
    {
        null or DBNull => string.Empty,
        string text => CsvSanitizer.Sanitize(text),
        DateTime dateTime => dateTime.ToString("O", CultureInfo.InvariantCulture),
        DateTimeOffset dateTimeOffset => dateTimeOffset.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture) ?? string.Empty,
        _ => CsvSanitizer.Sanitize(value.ToString())
    };
    private async Task<T> Guard<T>(string operation, Func<Task<T>> work) { try { return await work(); } catch (Exception ex) { InfrastructureLogMessages.ExecutiveIntelligenceFailed(logger, operation, tenant.TenantId, ex); throw; } }
    private async Task Guard(string operation, Func<Task> work) { try { await work(); } catch (Exception ex) { InfrastructureLogMessages.ExecutiveIntelligenceFailed(logger, operation, tenant.TenantId, ex); throw; } }
}
