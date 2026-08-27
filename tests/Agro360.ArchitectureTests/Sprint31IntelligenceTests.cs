namespace Agro360.ArchitectureTests;

public sealed class Sprint31IntelligenceTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public void Full_sql_has_all_operational_intelligence_tables_and_tenant_policies()
    {
        var sql = File.ReadAllText(Path.Combine(Root, "database/agro360-postgres-full.sql"));
        string[] tables = ["intelligence_rules", "intelligence_rule_executions", "intelligence_recommendations", "intelligence_recommendation_events", "intelligence_recommendation_sources", "intelligence_scores", "intelligence_score_factors", "intelligence_score_history", "intelligence_anomalies", "intelligence_anomaly_events", "intelligence_priority_items", "intelligence_assistant_sessions", "intelligence_assistant_messages", "intelligence_assistant_sources", "intelligence_feedback", "intelligence_provider_settings", "intelligence_query_logs"];
        foreach (var table in tables) Assert.Contains($"intelligence.{table}", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enable row level security", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("overall_score between 0 and 100", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("credential_reference", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recommendation_decision_requires_reason_for_rejection_and_tenant_filter()
    {
        var service = File.ReadAllText(Path.Combine(Root, "src/Modules/Agro360.Infrastructure/Services/OperationalIntelligenceService.cs"));
        Assert.Contains("Informe o motivo da recusa", service);
        Assert.Contains("tenant_id=@TenantId", service);
        Assert.DoesNotContain("select *", service, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Intelligence_forms_use_business_choices_not_manual_foreign_keys()
    {
        var view = File.ReadAllText(Path.Combine(Root, "src/Hosts/Agro360.Web/Pages/Intelligence/Index.cshtml"));
        Assert.Contains("Motor de Regras Inteligentes", view);
        Assert.Contains("<select name=\"module\"", view);
        Assert.DoesNotContain("name=\"entityId\"", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("type=\"text\" name=\"tenantId\"", view, StringComparison.OrdinalIgnoreCase);
    }
}
