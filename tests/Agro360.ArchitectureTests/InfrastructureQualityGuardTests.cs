using System.Reflection;
using System.Text.RegularExpressions;
using Agro360.Infrastructure.Services;

namespace Agro360.ArchitectureTests;

public sealed class InfrastructureQualityGuardTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();
    private static readonly string ServicesRoot = Path.Combine(RepositoryRoot, "src", "Modules", "Agro360.Infrastructure", "Services");

    [Fact]
    public void TrackedSourcesMustNotContainMergeConflictMarkers()
    {
        string[] markers = [new string('<', 7), new string('=', 7), new string('>', 7)];
        foreach (var file in SourceFiles())
        {
            var source = File.ReadAllText(file);
            Assert.DoesNotContain(markers, marker => source.Contains(marker, StringComparison.Ordinal));
        }
    }

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
    public void ServicesMustPreserveInterfaceParameterNames()
    {
        var assembly = typeof(FiscalService).Assembly;
        foreach (var implementation in assembly.GetTypes().Where(type => type is { IsClass: true, IsAbstract: false } && type.Namespace == "Agro360.Infrastructure.Services"))
        foreach (var contract in implementation.GetInterfaces().Where(type => type.Namespace == "Agro360.Application.Contracts"))
        foreach (var contractMethod in contract.GetMethods())
        {
            var parameterTypes = contractMethod.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
            var implementationMethod = implementation.GetMethod(contractMethod.Name, BindingFlags.Instance | BindingFlags.Public, null, parameterTypes, null);
            Assert.NotNull(implementationMethod);
            Assert.Equal(contractMethod.GetParameters().Select(parameter => parameter.Name), implementationMethod.GetParameters().Select(parameter => parameter.Name));
        }
    }

    [Fact]
    public void ServicesMustNotNormalizeCaseWithCultureSensitiveCalls()
    {
        var pattern = new Regex(@"\.To(?:Lower|Upper)\s*\(\s*\)", RegexOptions.CultureInvariant);
        foreach (var file in Directory.EnumerateFiles(ServicesRoot, "*.cs")) Assert.DoesNotMatch(pattern, File.ReadAllText(file));
    }

    [Fact]
    public void RawStringDelimitersInSourcesAndTestsMustBeBalanced()
    {
        foreach (var file in SourceFiles().Where(path => Path.GetExtension(path) is ".cs" or ".cshtml"))
        {
            var delimiters = Regex.Matches(File.ReadAllText(file), "\"\"\"", RegexOptions.CultureInvariant).Count;
            Assert.True(delimiters % 2 == 0, $"Raw string não balanceada em {Path.GetFileName(file)}.");
        }
    }

    [Fact]
    public void JsonDocumentParsingMustHandleMalformedInput()
    {
        foreach (var file in SourceFiles().Where(path => Path.GetExtension(path) == ".cs"))
        {
            var source = File.ReadAllText(file);
            foreach (Match parse in Regex.Matches(source, @"JsonDocument\.Parse\s*\(", RegexOptions.CultureInvariant))
            {
                var methodWindowStart = Math.Max(0, parse.Index - 500);
                var methodWindowLength = Math.Min(source.Length - methodWindowStart, 1_000);
                var methodWindow = source.Substring(methodWindowStart, methodWindowLength);
                Assert.True(methodWindow.Contains("try", StringComparison.Ordinal) || file.EndsWith("GeoJsonRules.cs", StringComparison.Ordinal),
                    $"JsonDocument.Parse sem tratamento de entrada inválida em {Path.GetFileName(file)}.");
            }
        }
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

    private static IEnumerable<string> SourceFiles() =>
        new[] { "src", "tests", "database" }.SelectMany(directory => Directory.EnumerateFiles(Path.Combine(RepositoryRoot, directory), "*", SearchOption.AllDirectories))
            .Where(path => Path.GetExtension(path) is ".cs" or ".cshtml" or ".sql");

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MNSOFT.Agro360.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Raiz do repositório não encontrada.");
    }
}
