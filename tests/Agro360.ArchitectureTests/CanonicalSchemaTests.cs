using System.Text.RegularExpressions;

namespace Agro360.ArchitectureTests;

public sealed class CanonicalSchemaTests
{
    private static string Sql => File.ReadAllText(Path.Combine(Root(), "database", "agro360-postgres-full.sql"));

    [Fact]
    public void Installer_creates_only_the_canonical_schema()
    {
        var declarations = Regex.Matches(Sql, @"create\s+schema\s+if\s+not\s+exists\s+([a-z_][a-z0-9_]*)", RegexOptions.IgnoreCase)
            .Select(match => match.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.Equal(["agro360"], declarations);
        Assert.DoesNotContain("\\i", Sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Every_static_table_declaration_is_schema_qualified()
    {
        Assert.DoesNotMatch(new Regex(@"create\s+table\s+if\s+not\s+exists\s+(?!agro360\.)", RegexOptions.IgnoreCase), Sql);
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MNSOFT.Agro360.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Raiz não encontrada.");
    }
}
