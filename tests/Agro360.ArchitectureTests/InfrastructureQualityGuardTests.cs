using System.Reflection;
using System.Text.RegularExpressions;

namespace Agro360.ArchitectureTests;

public sealed class InfrastructureQualityGuardTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ServicesRoot = Path.Combine(RepositoryRoot, "src", "Modules", "Agro360.Infrastructure", "Services");

    
    [Fact]
    public void InfrastructureServicesMustUseLoggerMessageInsteadOfDirectLoggingExtensions()
    {
        string[] forbiddenCalls = ["Log" + "Information(", "Log" + "Warning(", "Log" + "Error("];
        foreach (var file in Directory.EnumerateFiles(ServicesRoot, "*.cs").Where(path => !path.EndsWith("InfrastructureLogMessages.cs", StringComparison.Ordinal)))
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain(forbiddenCalls, call => source.Contains(call, StringComparison.Ordinal));
        }
    }

   

    [Fact]
    public void ServicesMustNotNormalizeCaseWithCultureSensitiveCalls()
    {
        var pattern = new Regex(@"\.To(?:Lower|Upper)\s*\(\s*\)", RegexOptions.CultureInvariant);
        foreach (var file in Directory.EnumerateFiles(ServicesRoot, "*.cs")) Assert.DoesNotMatch(pattern, File.ReadAllText(file));
    }

  

     

    [Fact]
    public void CanonicalInstallerMustOnlyCreateTheAgro360Schema()
    {
        var sql = File.ReadAllText(Path.Combine(RepositoryRoot, "database", "agro360-postgres-full.sql"));
        var schemas = Regex.Matches(sql, @"\bcreate\s+schema(?:\s+if\s+not\s+exists)?\s+([a-z_][a-z0-9_]*)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Select(match => match.Groups[1].Value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        Assert.Equal(["agro360"], schemas, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("USING gist", sql, StringComparison.OrdinalIgnoreCase);
    }

    
    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MNSOFT.Agro360.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }
}
