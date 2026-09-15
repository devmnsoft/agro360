namespace Agro360.ArchitectureTests;

public sealed class Sprint37ProcurementTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    [Fact] public void FullInstallerContainsPortableProcurement() { var sql = File.ReadAllText(Path.Combine(Root, "database/agro360-postgres-full.sql")); Assert.Contains("create schema if not exists agro360", sql, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("\\i ", sql, StringComparison.OrdinalIgnoreCase); Assert.Contains("force row level security", sql, StringComparison.OrdinalIgnoreCase); }
    [Fact] public void FormNeverRequestsTechnicalIdentifiers() { var view = File.ReadAllText(Path.Combine(Root, "src/Hosts/Agro360.Web/Pages/Procurement/Index.cshtml")); Assert.DoesNotContain("type=\"text\" name=\"supplierId\"", view, StringComparison.OrdinalIgnoreCase); Assert.Contains("data-lookup=\"suppliers\"", view); Assert.Contains("data-lookup=\"catalog\"", view); }
    [Fact] public void ApiHasPoliciesAndNoControllerSql() { var controller = File.ReadAllText(Path.Combine(Root, "src/Hosts/Agro360.Api/Controllers/ProcurementController.cs")); Assert.Contains("Authorize(Policy = Permissions.PurchasingReceive)", controller); Assert.DoesNotContain("select ", controller, StringComparison.OrdinalIgnoreCase); }
    [Fact] public void QualityReleaseIsTenantScopedIdempotentAndSeparatedFromPhysicalReceipt() { var service = File.ReadAllText(Path.Combine(Root, "src/Modules/Agro360.Infrastructure/Services/ProcurementService.cs")); var migration = File.ReadAllText(Path.Combine(Root, "database/migrations/080_assisted_procurement_receipt.sql")); Assert.Contains("procurement_receipt_quarantine", service); Assert.Contains("for update", service, StringComparison.OrdinalIgnoreCase); Assert.Contains("idempotency_key", migration); Assert.Contains("released_quantity+rejected_quantity<=quantity", migration); Assert.Contains("Permissions.ComplianceApprove", File.ReadAllText(Path.Combine(Root, "src/Hosts/Agro360.Api/Controllers/ProcurementController.cs"))); }

    [Fact]
    public void InvoiceMatchingIsTenantScopedIdempotentAndDoesNotClaimPayment()
    {
        var service = File.ReadAllText(Path.Combine(Root, "src", "Modules", "Agro360.Infrastructure", "Services", "ProcurementService.cs"));
        var migration = File.ReadAllText(Path.Combine(Root, "database", "migrations", "084_procurement_invoice_matching.sql"));
        var view = File.ReadAllText(Path.Combine(Root, "src", "Hosts", "Agro360.Web", "Pages", "Procurement", "Index.cshtml"));
        Assert.Contains("procurement_invoice_matches", migration);
        Assert.Contains("unique(tenant_id,idempotency_key)", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("for update", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("accepted_balance_exceeded", service);
        Assert.Contains("separation_of_duties", migration);
        Assert.Contains("não significa pagamento", view, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("conta a pagar gerada", view, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReplenishmentPlanningIsExplainableAndIdempotent()
    {
        var service = File.ReadAllText(Path.Combine(Root, "src", "Modules", "Agro360.Infrastructure", "Services", "ReplenishmentService.cs"));
        var migration = File.ReadAllText(Path.Combine(Root, "database", "migrations", "083_replenishment_planning.sql"));
        Assert.Contains("usable=available-reserved", service);
        Assert.Contains("not exists(select 1 from agro360.procurement_purchase_orders", service);
        Assert.Contains("pg_advisory_xact_lock", service);
        Assert.Contains("suggestedStock==0?0", service);
        Assert.Contains("ux_replenishment_policy_active", migration);
        Assert.Contains("ux_material_need_confirmation", migration);
    }
}
