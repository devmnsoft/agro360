namespace Agro360.ArchitectureTests;

public sealed class ComplianceFormTests
{
    [Fact]
    public void ComplianceFormUsesLookupsAndValidation()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var html = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/Pages/Compliance/Index.cshtml"));
        var js = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/wwwroot/js/agro360.compliance_js"));
        Assert.DoesNotContain("ID técnico", html + js, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-resource", js);
        Assert.Contains("reportValidity", js);
        Assert.Contains("Carregando", js);
        Assert.Contains("Nenhum registro", js);
    }

    [Fact]
    public void PublicCertificateRouteIsConstrainedAndAnonymous()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var controller = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Api/Controllers/ComplianceControllers.cs"));
        Assert.Contains("certificate:regex(^[[A-Fa-f0-9]]{{20}}$)", controller);
        Assert.Contains("AllowAnonymous", controller);
    }
}
