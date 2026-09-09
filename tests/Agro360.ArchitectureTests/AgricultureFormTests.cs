namespace Agro360.ArchitectureTests;

public sealed class AgricultureFormTests
{
    [Fact]
    public void AgriculturePageNeverExposesATypedTechnicalIdentifier()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var html = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/Pages/Agriculture/Index.cshtml"));
        Assert.DoesNotContain("type=\"text\" name=\"propertyId\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("type=\"text\" name=\"fieldId\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-lookup=\"properties\"", html);
        Assert.Contains("data-lookup=\"fields\"", html);
    }

    [Fact]
    public void FullSqlIsStandalone()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var sql = File.ReadAllText(Path.Combine(root, "database/agro360-postgres-full.sql"));
        Assert.DoesNotContain("\\i ", sql);
        Assert.Contains("Sprint 11 - Agricultura 360", sql);
    }

    [Fact]
    public void PropertiesFlowConnectsPagePoliciesServiceAndDatabaseGuards()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var page = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/Pages/Properties/Index.cshtml"));
        var layout = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/Pages/Shared/_Layout.cshtml"));
        var controller = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Api/Controllers/PropertiesController.cs"));
        var service = File.ReadAllText(Path.Combine(root, "src/Modules/Agro360.Infrastructure/Services/PropertyService.cs"));
        var sql = File.ReadAllText(Path.Combine(root, "database/agro360-postgres-full.sql"));

        Assert.Contains("href=\"/Properties\" data-permissions=\"properties.read\"", layout);
        Assert.Contains("@page \"/properties\"", page);
        Assert.Contains("Como usar esta tela", page);
        Assert.Contains("<select name=\"organizationId\"", page);
        Assert.DoesNotContain("type=\"text\" name=\"organizationId\"", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("type=\"text\" name=\"farmId\"", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Authorize(Policy = Permissions.PropertiesWrite)", controller);
        Assert.Contains("HttpPut(\"properties/{id:guid}\")", controller);
        Assert.Contains("HttpDelete(\"fields/{id:guid}\")", controller);
        Assert.Contains("tenant_id=@TenantId", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("version=@Version", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("select *", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ck_geo_farms_state_format", sql);
        Assert.Contains("ck_geo_fields_boundary_type", sql);
        Assert.Contains("'5.1.1'", sql);
    }
}
