using System.Text.RegularExpressions;
using Agro360.Application.Contracts; 

namespace Agro360.ArchitectureTests;

public sealed class FiscalServiceSafetyTests
{
    [Fact]
    public void FiscalServiceImplementationMatchesContractParameterNames()
    {
        var contractMethods = typeof(IFiscalService).GetMethods().ToDictionary(MethodKey);
        var implementationMethods = typeof(Agro360.Infrastructure.Services.FiscalService)
            .GetMethods()
            .Where(method => method.DeclaringType == typeof(Agro360.Infrastructure.Services.FiscalService));

        foreach (var implementationMethod in implementationMethods)
        {
            var key = MethodKey(implementationMethod);
            Assert.True(contractMethods.TryGetValue(key, out var contractMethod), $"Método público sem correspondência no contrato: {key}");
            Assert.Equal(
                contractMethod!.GetParameters().Select(parameter => parameter.Name),
                implementationMethod.GetParameters().Select(parameter => parameter.Name));
        }
    }

    [Fact]
    public void FiscalServiceKeepsSqlInsideStringsAndDapperCommandsStructured()
    {
        var source = File.ReadAllText(Path.Combine(Root(), "src", "Modules", "Agro360.Infrastructure", "Services", "FiscalService.cs"));

        Assert.DoesNotContain(new string('<', 7), source, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('=', 7), source, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('>', 7), source, StringComparison.Ordinal);
        Assert.DoesNotMatch(new Regex("CommandDefinition\\s*\\(\\s*\"\"\"", RegexOptions.CultureInvariant), source);
        Assert.Contains("const string sql = \"\"\"", source, StringComparison.Ordinal);
        Assert.Contains("new CommandDefinition(\n                sql,", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalInstallerContainsTheFiscalBillingTablesWithoutLegacyNameCollisions()
    {
        var sql = File.ReadAllText(Path.Combine(Root(), "database", "agro360-postgres-full.sql"));
        string[] tables =
        [
            "fiscal_operations",
            "fiscal_operation_rules",
            "fiscal_invoice_drafts",
            "fiscal_invoice_items",
            "fiscal_invoice_installments",
            "fiscal_documents",
            "fiscal_purchase_checks",
            "fiscal_divergences",
            "fiscal_stock_movements",
            "fiscal_financial_integrations",
            "fiscal_document_events"
        ];

        foreach (var table in tables)
        {
            Assert.Contains($"agro360.{table}", sql, StringComparison.OrdinalIgnoreCase);
        }

        Assert.Contains("agro360.fiscal_issuance_documents", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static string MethodKey(System.Reflection.MethodInfo method) =>
        $"{method.Name}({string.Join(',', method.GetParameters().Select(parameter => parameter.ParameterType.FullName))})";

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MNSOFT.Agro360.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Raiz não encontrada.");
    }
}
