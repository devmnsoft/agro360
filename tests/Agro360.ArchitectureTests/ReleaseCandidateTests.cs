namespace Agro360.ArchitectureTests;

public sealed class ReleaseCandidateTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void FullPostgresInstallerMustBePortableAndCoverReleasedModules()
    {
        var sql = File.ReadAllText(Path.Combine(Root, "database", "agro360-postgres-full.sql"));
        Assert.DoesNotContain("\\i ", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Host=", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Password=", sql, StringComparison.OrdinalIgnoreCase);
        foreach (var marker in new[] { "agro360.platform_schema_versions", "agro360.agriculture_field_operations", "agro360.livestock_animals", "agro360.storage_receipts", "agro360.traceability_lots", "agro360.compliance_product_rules", "agro360.rural_hr_people", "2.0.0-rc.1" })
            Assert.Contains(marker, sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MainNavigationMustUseRealDestinationsInsteadOfFeaturePlaceholders()
    {
        var layout = File.ReadAllText(Path.Combine(Root, "src/Hosts/Agro360.Web/Pages/Shared/_Layout.cshtml"));
        Assert.DoesNotContain("data-feature=\"properties\"", layout, StringComparison.Ordinal);
        Assert.DoesNotContain("data-feature=\"finance\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/Agriculture\"", layout, StringComparison.Ordinal);
        Assert.Contains("href=\"/Compliance\"", layout, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MNSOFT.Agro360.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }
}
