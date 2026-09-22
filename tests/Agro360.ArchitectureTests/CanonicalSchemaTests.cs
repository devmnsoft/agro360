using System.Text.RegularExpressions;

namespace Agro360.ArchitectureTests;

public sealed class CanonicalSchemaTests
{
    [Fact]
    public void InternalMaterialJourneyUsesCanonicalStockAndIdempotentFacts()
    {
        var root = Root();
        var migration = File.ReadAllText(Path.Combine(root, "database", "migrations", "081_internal_material_requests.sql"));
        var service = File.ReadAllText(Path.Combine(root, "src", "Modules", "Agro360.Infrastructure", "Services", "MaterialRequestService.cs"));
        Assert.Contains("inventory_stock_balances", service);
        Assert.Contains("for update", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("unique(tenant_id,idempotency_key)", migration);
        Assert.Contains("AWAITING_INSPECTION", migration);
        Assert.DoesNotContain("material_stock_balances", migration);
    }
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

    [Fact]
    public void HarvestClosingIsImmutableTenantScopedAndActionable()
    {
        var migration = File.ReadAllText(Path.Combine(Root(), "database/migrations/077_harvest_management_closing.sql"));
        var service = File.ReadAllText(Path.Combine(Root(), "src/Modules/Agro360.Infrastructure/Services/HarvestService.cs"));
        var client = File.ReadAllText(Path.Combine(Root(), "src/Hosts/Agro360.Web/wwwroot/js/harvest.js"));

        Assert.Contains("unique(tenant_id,season_id,version)", migration);
        Assert.Contains("supersedes_id", migration);
        Assert.Contains("pg_advisory_xact_lock", service);
        Assert.Contains("operational_at::date<=@Cutoff", service);
        Assert.Contains("created_at>@GeneratedAt", service);
        Assert.Contains("closing.blocked", service);
        Assert.Contains("InTenantTransactionAsync<SeasonClosingRunDto>", service);
        Assert.Contains("closing.stale_snapshot", service);
        Assert.Contains("DeserializeIndicators", service);
        Assert.Contains("DeserializeSnapshot<SeasonClosingIssueDto>", service);
        Assert.Contains("closing.invalid_snapshot", service);
        Assert.Contains("ReopenAsync", service);
        Assert.Contains("transition\":\"reopen", service);
        Assert.Contains("data-reopen", client);
        Assert.Contains("conteúdo persistido foi omitido do log", service);
        Assert.Contains("private static List<SeasonClosingIssueDto> BuildIssues", service);
        Assert.DoesNotContain("new JsonSerializerOptions{PropertyNameCaseInsensitive=true}", service);
        Assert.Contains("percentual não calculável com base zero", client);
        Assert.Contains("Consultar dados", File.ReadAllText(Path.Combine(Root(), "src/Hosts/Agro360.Web/Pages/Harvest/Index.cshtml")));
        Assert.Contains("if(/^[=+\\-@]/.test(x))", client);
    }

    [Fact]
    public void InventoryMovementGateProtectsCountsReservationsLotsAndTenants()
    {
        var migration = File.ReadAllText(Path.Combine(Root(), "database/migrations/106_inventory_movement_integrity.sql"));
        var installer = Sql;

        Assert.Contains("create or replace function agro360.inventory_apply_stock_movement", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant_id = p_tenant", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status in ('COUNTING', 'RECONCILING', 'AWAITING_APPROVAL')", migration);
        Assert.Contains("newbalance < b.reserved", migration);
        Assert.Contains("lotbalance + p_quantity < 0", migration);
        Assert.Contains("pg_advisory_xact_lock", migration);
        Assert.Contains("inventory_stock_movement_count_guard", migration);
        Assert.Contains("new.reference_type is distinct from 'PHYSICAL_COUNT_ADJUSTMENT'", migration);
        Assert.Contains("10.6.0", installer);
        Assert.EndsWith("commit;", installer.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MNSOFT.Agro360.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Raiz não encontrada.");
    }
}
