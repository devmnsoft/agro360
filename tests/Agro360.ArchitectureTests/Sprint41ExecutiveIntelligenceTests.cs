namespace Agro360.ArchitectureTests;

public sealed class Sprint41ExecutiveIntelligenceTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact] public void FullSqlHasGovernedTenantSafeIntelligenceStructures()
    {
        var sql=File.ReadAllText(Path.Combine(Root,"database/agro360-postgres-full.sql"));
        foreach(var table in new[]{"intelligence_kpi_definitions","intelligence_kpi_snapshots","intelligence_kpi_targets","intelligence_alert_rules","intelligence_alerts","intelligence_alert_events","intelligence_risks","intelligence_recommendations","intelligence_recommendation_events","intelligence_audit_events","intelligence_report_exports","intelligence_user_preferences"}) Assert.Contains($"create table if not exists agro360.{table}",sql,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("force row level security",sql,StringComparison.OrdinalIgnoreCase);
        Assert.Contains("target between 0 and 100",sql,StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void ServiceEnforcesCriticalDecisionsSourcesAndTenant()
    {
        var code=File.ReadAllText(Path.Combine(Root,"src/Modules/Agro360.Infrastructure/Services/ExecutiveIntelligenceService.cs"));
        Assert.Contains("Resolver alerta exige comentário",code); Assert.Contains("Alerta crítico exige justificativa",code); Assert.Contains("Rejeição de recomendação alta ou crítica exige motivo",code); Assert.Contains("Sources.Contains",code); Assert.Contains("tenant_id=@TenantId",code); Assert.DoesNotContain("select *",code,StringComparison.OrdinalIgnoreCase);
    }

    [Fact] public void UiUsesBusinessChoicesAndDisclosesRuleBasedRecommendations()
    {
        var view=File.ReadAllText(Path.Combine(Root,"src/Hosts/Agro360.Web/Pages/Intelligence360/Index.cshtml"));
        Assert.Contains("Recomendações são geradas por regras transparentes",view); Assert.Contains("<select name=\"dataSource\"",view); Assert.DoesNotContain("name=\"tenantId\"",view,StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("name=\"entityId\"",view,StringComparison.OrdinalIgnoreCase);
    }
}
