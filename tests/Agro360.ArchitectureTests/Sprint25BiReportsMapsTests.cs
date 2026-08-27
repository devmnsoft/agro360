namespace Agro360.ArchitectureTests;

public sealed class Sprint25BiReportsMapsTests
{
    private static readonly string Root = FindRoot();

    [Fact]
    public void Database_defines_tenant_scoped_bi_and_geo_foundation()
    {
        var sql = File.ReadAllText(Path.Combine(Root, "database/migrations/023_sprint25_bi_reports_maps_design.sql"));
        foreach (var table in new[] { "saved_filters", "dashboard_widgets", "report_definitions", "report_exports", "geo_locations", "geo_areas", "geo_routes", "geo_route_points", "geo_layers", "user_dashboard_preferences", "ui_audit_events" })
            Assert.Contains(table, sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("latitude between -90 and 90", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("longitude between -180 and 180", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("enable row level security", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Reports_page_has_real_filtered_csv_flow_and_empty_state()
    {
        var view = File.ReadAllText(Path.Combine(Root, "src/Hosts/Agro360.Web/Pages/Reports/Index.cshtml"));
        var script = File.ReadAllText(Path.Combine(Root, "src/Hosts/Agro360.Web/wwwroot/js/reports.js"));
        Assert.Contains("type=\"date\"", view);
        Assert.Contains("Exportar CSV", view);
        Assert.Contains("Nenhum registro encontrado", script);
        Assert.Contains("Authorization", script);
        Assert.DoesNotContain("mock", script, StringComparison.OrdinalIgnoreCase);
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MNSOFT.Agro360.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Raiz da solução não encontrada.");
    }
}
