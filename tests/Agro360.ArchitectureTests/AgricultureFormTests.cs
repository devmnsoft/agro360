namespace Agro360.ArchitectureTests;

public sealed class AgricultureFormTests
{
    [Fact]
    public void Agriculture_page_never_exposes_a_typed_technical_identifier()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var html = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/Pages/Agriculture/Index.cshtml"));
        Assert.DoesNotContain("type=\"text\" name=\"propertyId\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("type=\"text\" name=\"fieldId\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-lookup=\"properties\"", html);
        Assert.Contains("data-lookup=\"fields\"", html);
    }

    [Fact]
    public void Full_sql_is_standalone()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var sql = File.ReadAllText(Path.Combine(root, "database/agro360-postgres-full.sql"));
        Assert.DoesNotContain("\\i ", sql);
        Assert.Contains("Sprint 11 - Agricultura 360", sql);
    }
}
