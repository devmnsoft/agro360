namespace Agro360.ArchitectureTests;

public sealed class Sprint50UxTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    [Fact] public void Layout_loads_accessible_real_confirmation_component()
    { var layout=Read("src/Hosts/Agro360.Web/Pages/Shared/_Layout.cshtml"); Assert.Contains("aria-describedby=\"confirmation-consequence\"",layout); Assert.Contains("~/js/forms.js",layout); }
    [Fact] public void Client_validation_has_summary_loading_and_locales()
    { var js=Read("src/Hosts/Agro360.Web/wwwroot/js/forms.js"); Assert.Contains("form-validation-summary",js); Assert.Contains("aria-busy",js); foreach(var culture in new[]{"pt-BR","en-US","es-ES"}) Assert.Contains(culture,js); }
    [Fact] public void Confirmation_only_replays_the_real_action_after_confirmation()
    { var js=Read("src/Hosts/Agro360.Web/wwwroot/js/forms.js"); Assert.Contains("data-confirm-action",js); Assert.Contains("target.click()",js); Assert.Contains("reason.value.trim().length<3",js); }
    [Fact] public void Database_has_tenant_scoped_ui_catalog_and_audit()
    { var sql=Read("database/migrations/050_ui_quality.sql"); foreach(var table in new[]{"contextual_help","message_templates","form_validation_rules","action_confirmations","page_events","validation_audit","report_exports"}) Assert.Contains(table,sql); Assert.Contains("enable row level security",sql); Assert.Contains("agro360.platform_current_tenant_id()",sql); }
}
