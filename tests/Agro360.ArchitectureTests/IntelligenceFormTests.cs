namespace Agro360.ArchitectureTests;

public sealed class IntelligenceFormTests
{
    [Fact]
    public void IntelligenceUiDoesNotRequestTechnicalIds()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var html = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/Pages/Intelligence/Index.cshtml"));
        Assert.DoesNotContain("name=\"farmId\" type=\"text\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("type=\"hidden\" name=\"farmId\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<select name=\"farmId\"", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AssistantAcceptsFreeQuestionsAndUsesSemanticThemeTokens()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var html = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/Pages/Intelligence/Index.cshtml"));
        var css = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/wwwroot/css/intelligence.css"));
        Assert.Contains("<textarea id=\"assistant-question\"", html, StringComparison.Ordinal);
        Assert.Contains("maxlength=\"300\"", html, StringComparison.Ordinal);
        Assert.Contains("aria-live=\"polite\"", html, StringComparison.Ordinal);
        Assert.Contains("var(--surface", css, StringComparison.Ordinal);
        Assert.Contains("var(--text", css, StringComparison.Ordinal);
        Assert.DoesNotContain("background:#071914", css, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("color:inherit", css, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FullSqlContainsSprint13AndNoIncludes()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var sql = File.ReadAllText(Path.Combine(root, "database/agro360-postgres-full.sql"));
        Assert.Contains("Sprint 13", sql); Assert.Contains("create schema if not exists agro360", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\\i ", sql, StringComparison.OrdinalIgnoreCase);
    }
}
