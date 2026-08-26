namespace Agro360.ArchitectureTests;
public sealed class ComplianceFormTests
{
 [Fact] public void Compliance_form_uses_lookups_and_validation()
 {
   var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
   var html=File.ReadAllText(Path.Combine(root,"src/Hosts/Agro360.Web/Pages/Compliance/Index.cshtml"));
   var js=File.ReadAllText(Path.Combine(root,"src/Hosts/Agro360.Web/wwwroot/js/compliance.js"));
   Assert.DoesNotContain("ID técnico",html+js,StringComparison.OrdinalIgnoreCase); Assert.Contains("data-resource",js); Assert.Contains("reportValidity",js); Assert.Contains("Carregando",js); Assert.Contains("Nenhum registro",js);
 }
 [Fact] public void Public_certificate_route_is_constrained_and_anonymous()
 {
   var root=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../../"));
   var controller=File.ReadAllText(Path.Combine(root,"src/Hosts/Agro360.Api/Controllers/ComplianceControllers.cs"));
   Assert.Contains("[A-Fa-f0-9]{20}",controller); Assert.Contains("AllowAnonymous",controller);
 }
}
