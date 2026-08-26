namespace Agro360.ArchitectureTests;
public sealed class Sprint16FormTests
{
 [Fact] public void Integration_ui_has_validation_and_no_manual_technical_id(){var root=FindRoot();var html=File.ReadAllText(Path.Combine(root,"src/Hosts/Agro360.Web/Pages/Integrations/Index.cshtml"));Assert.Contains("required",html);Assert.DoesNotContain("name=\"id\"",html,StringComparison.OrdinalIgnoreCase);Assert.Contains("<select",html);}
 [Fact] public void Sprint16_endpoints_are_secured_except_device_ingestion(){var root=FindRoot();var code=File.ReadAllText(Path.Combine(root,"src/Hosts/Agro360.Api/Controllers/IntegrationsController.cs"));Assert.Contains("Authorize(Policy=Permissions.IntegrationsRead)",code);Assert.Contains("api/iot/readings",code);}
 private static string FindRoot(){var p=AppContext.BaseDirectory;while(p is not null&&!File.Exists(Path.Combine(p,"MNSOFT.Agro360.sln")))p=Directory.GetParent(p)?.FullName;return p??throw new DirectoryNotFoundException();}
}
