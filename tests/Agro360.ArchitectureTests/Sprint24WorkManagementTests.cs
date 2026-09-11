namespace Agro360.ArchitectureTests;

public sealed class Sprint24WorkManagementTests
{
    [Fact] public void FullSqlContainsSprint24Tables() { var sql = File.ReadAllText(Path.Combine(Root(), "database/agro360-postgres-full.sql")); foreach (var table in new[] { "operational_tasks", "operational_alerts", "operational_rules", "workflow_instances", "notifications", "communication_outbox", "calendar_events" }) Assert.Contains(table, sql, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("\\i ", sql); }
    [Fact] public void WebFormUsesUserLookupNotManualIdentifier() { var view = File.ReadAllText(Path.Combine(Root(), "src/Hosts/Agro360.Web/Pages/Work/Index.cshtml")); Assert.Contains("select name=\"responsibleId\"", view); Assert.DoesNotContain("type=\"text\" name=\"responsibleId\"", view); }
    [Fact] public void OperationCenterDerivesPendingItemsAndNeverAllowsManualResolution() { var service = File.ReadAllText(Path.Combine(Root(), "src/Modules/Agro360.Infrastructure/Services/WorkManagementService.cs")); Assert.Contains("OperationCenterAsync", service); Assert.Contains("user_permissions", service); Assert.Contains("count(*) over() FullCount", service); Assert.DoesNotContain("ResolveOccurrenceAsync", service); }
    [Fact] public void OperationCenterInteractionIsTenantScopedAndAudited() { var sql = File.ReadAllText(Path.Combine(Root(), "database/migrations/073_operation_center.sql")); Assert.Contains("primary key(tenant_id,occurrence_key)", sql); Assert.Contains("operation_occurrence_events", sql); Assert.Contains("platform_enable_tenant_rls", sql); }
    private static string Root() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName, "MNSOFT.Agro360.sln"))) d = d.Parent; return d?.FullName ?? throw new InvalidOperationException("Repository root not found."); }
}
