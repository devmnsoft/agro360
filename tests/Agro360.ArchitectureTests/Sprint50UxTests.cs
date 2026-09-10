namespace Agro360.ArchitectureTests;

public sealed class Sprint50UxTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    [Fact]
    public void LayoutLoadsAccessibleRealConfirmationComponent()
    { var layout = Read("src/Hosts/Agro360.Web/Pages/Shared/_Layout.cshtml"); Assert.Contains("aria-describedby=\"confirmation-consequence\"", layout); Assert.Contains("~/js/forms.js", layout); }
    [Fact]
    public void ClientValidationHasSummaryLoadingAndLocales()
    { var js = Read("src/Hosts/Agro360.Web/wwwroot/js/forms.js"); Assert.Contains("form-validation-summary", js); Assert.Contains("aria-busy", js); foreach (var culture in new[] { "pt-BR", "en-US", "es-ES" }) Assert.Contains(culture, js); }
    [Fact]
    public void ConfirmationOnlyReplaysTheRealActionAfterConfirmation()
    { var js = Read("src/Hosts/Agro360.Web/wwwroot/js/forms.js"); Assert.Contains("data-confirm-action", js); Assert.Contains("target.click()", js); Assert.Contains("reason.value.trim().length<3", js); }
    [Fact]
    public void DatabaseHasTenantScopedUiCatalogAndAudit()
    { var sql = Read("database/migrations/050_ui_quality.sql"); foreach (var table in new[] { "contextual_help", "message_templates", "form_validation_rules", "action_confirmations", "page_events", "validation_audit", "report_exports" }) Assert.Contains(table, sql); Assert.Contains("enable row level security", sql); Assert.Contains("agro360.platform_current_tenant_id()", sql); }

    [Fact]
    public void FleetPageCoversMaintenanceJourneyWithoutGuidFields()
    {
        var page = Read("src/Hosts/Agro360.Web/Pages/Fleet/Index.cshtml");
        var js = Read("src/Hosts/Agro360.Web/wwwroot/js/fleet.js");
        var migration = Read("database/migrations/069_fleet_maintenance_operations.sql");
        var controller = Read("src/Hosts/Agro360.Api/Controllers/FleetController.cs");
        Assert.Contains("data-tab=\"plans\"", page, StringComparison.Ordinal);
        Assert.Contains("data-tab=\"orders\"", page, StringComparison.Ordinal);
        Assert.Contains("Como usar esta tela", page, StringComparison.Ordinal);
        Assert.Contains("/refuelings/operational", js, StringComparison.Ordinal);
        Assert.Contains("fleet_meter_readings", migration, StringComparison.Ordinal);
        Assert.Contains("fleet_operational_blocks", migration, StringComparison.Ordinal);
        Assert.Contains("fleet_asset_reservations", migration, StringComparison.Ordinal);
        Assert.Contains("Permissions.MaintenanceWrite", controller, StringComparison.Ordinal);
        Assert.DoesNotContain("public async Task<IActionResult> Request(", controller, StringComparison.Ordinal);
        var fleetService = Read("src/Modules/Agro360.Infrastructure/Services/FleetService.cs");
        Assert.Contains("fleet_operational_blocks", fleetService, StringComparison.Ordinal);
        Assert.Contains("blocks_asset", fleetService, StringComparison.Ordinal);
        Assert.Contains("cadastralStatus", js, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("duePolicy", js, StringComparison.OrdinalIgnoreCase);
    }
}
