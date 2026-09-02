using System.Text.RegularExpressions;

namespace Agro360.ArchitectureTests;

public sealed class IntelligenceServiceStructureTests
{
    private static readonly string Root = FindRoot();
    private static readonly string Service = File.ReadAllText(Path.Combine(
        Root, "src", "Modules", "Agro360.Infrastructure", "Services", "IntelligenceService.cs"));

    [Fact]
    public void ServiceImplementsEveryIntelligenceContractOperation()
    {
        var contract = File.ReadAllText(Path.Combine(
            Root, "src", "Modules", "Agro360.Application", "Contracts", "IntelligenceContracts.cs"));
        var interfaceBody = Regex.Match(
            contract,
            @"public interface IIntelligenceService\s*\{(?<body>[\s\S]*?)\n\}",
            RegexOptions.CultureInvariant).Groups["body"].Value;
        var operations = Regex.Matches(interfaceBody, @"\b(?<name>[A-Z]\w+Async)\s*\(")
            .Select(match => match.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(operations);
        Assert.All(operations, operation =>
            Assert.Matches($@"\bpublic[^\n]*\b{operation}\s*\(", Service));
    }

    [Fact]
    public void DashboardCommandsAndQueriesAreTenantScoped()
    {
        foreach (var operation in new[]
                 {
                     "GetDashboardsAsync", "SaveDashboardAsync", "AddWidgetAsync", "DeleteWidgetAsync", "AskAsync"
                 })
        {
            Assert.Contains(operation, Service, StringComparison.Ordinal);
        }

        Assert.Contains("from agro360.ai_dashboards", Service, StringComparison.Ordinal);
        Assert.Contains("from agro360.ai_dashboard_widgets", Service, StringComparison.Ordinal);
        Assert.Contains("tenant_id = @TenantId", Service, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex(@"\b(?:identity|finance|workflow|inventory)\.[a-z_]", RegexOptions.IgnoreCase), Service);
    }

   
    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MNSOFT.Agro360.sln")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Raiz do Agro360 não encontrada.");
    }
}
