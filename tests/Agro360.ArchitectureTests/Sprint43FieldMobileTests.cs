namespace Agro360.ArchitectureTests;

public sealed class Sprint43FieldMobileTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
 
    [Fact]
    public void FullSqlContainsTenantSafeFieldMobileModel()
    {
        var sql = File.ReadAllText(Path.Combine(Root, "database/agro360-postgres-full.sql"));
        foreach (var table in new[] { "field_mobile_profiles", "field_mobile_shortcuts", "field_checklists", "field_checklist_versions", "field_checklist_items", "field_checklist_runs", "field_checklist_answers", "field_evidences", "field_qr_codes", "field_quick_records", "field_sync_queue", "field_sync_conflicts", "field_signatures", "field_locations", "field_report_exports" })
            Assert.Contains(table, sql, StringComparison.Ordinal);
        Assert.Contains("force row level security", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unique(tenant_id,user_id,idempotency_key)", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PwaHasRealOutboxAndNoMockData()
    {
        var script = File.ReadAllText(Path.Combine(Root, "src/Hosts/Agro360.Web/wwwroot/js/field.js"));
        var worker = File.ReadAllText(Path.Combine(Root, "src/Hosts/Agro360.Web/wwwroot/service-worker.js"));
        Assert.Contains("indexedDB.open", script, StringComparison.Ordinal);
        Assert.Contains("idempotencyKey", script, StringComparison.Ordinal);
        Assert.Contains("SYNC_REQUESTED", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("mock", script, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RequiredOperationalDocumentationExists()
    {
        var guide = File.ReadAllText(Path.Combine(Root, "docs/FIELD-MOBILE-PWA.md"));
        Assert.Contains("ICP-Brasil", guide, StringComparison.Ordinal);
        Assert.Contains("Pendências reais", guide, StringComparison.Ordinal);
        Assert.Contains("RLS", guide, StringComparison.Ordinal);
    }
}
