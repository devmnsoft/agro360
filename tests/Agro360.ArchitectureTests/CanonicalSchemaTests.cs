using System.Text.RegularExpressions;

namespace Agro360.ArchitectureTests;

public sealed class CanonicalSchemaTests
{
    private static string Sql => File.ReadAllText(Path.Combine(Root(), "database", "agro360-postgres-full.sql"));

    [Fact]
    public void InstallerCreatesOnlyTheCanonicalSchema()
    {
        var declarations = Regex.Matches(Sql, @"create\s+schema\s+if\s+not\s+exists\s+([a-z_][a-z0-9_]*)", RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.Equal(["agro360"], declarations);
        Assert.DoesNotContain("\\i", Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EveryStaticTableDeclarationIsSchemaQualified()
    {
        Assert.DoesNotMatch(new Regex(@"create\s+table\s+if\s+not\s+exists\s+(?!agro360\.)", RegexOptions.IgnoreCase), Sql);
    }

    [Fact]
    public void IncrementalFinanceBridgePreservesPublishedMigrationsAndLegacyData()
    {
        var beforeSprint8 = File.ReadAllText(Path.Combine(Root(), "database/migrations/006z_finance_receivables_legacy_bridge.sql"));
        var afterSprint8 = File.ReadAllText(Path.Combine(Root(), "database/migrations/007z_finance_receivables_legacy_data.sql"));

        Assert.Contains("rename to receivables_foundation_legacy", beforeSprint8, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("from finance.receivables_foundation_legacy", afterSprint8, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("legacy.amount > 0", afterSprint8, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("on conflict (id) do nothing", afterSprint8, StringComparison.OrdinalIgnoreCase);
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MNSOFT.Agro360.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Raiz não encontrada.");
    }
}
