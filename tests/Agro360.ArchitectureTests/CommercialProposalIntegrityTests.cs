namespace Agro360.ArchitectureTests;

public sealed class CommercialProposalIntegrityTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public void ProposalPersistenceIsVersionedTenantScopedAndIdempotent()
    {
        var sql = File.ReadAllText(Path.Combine(Root, "database/migrations/107_commercial_proposals_commissions.sql"));
        Assert.Contains("sales_proposal_versions", sql);
        Assert.Contains("force row level security", sql);
        Assert.Contains("unique(tenant_id,idempotency_key)", sql);
        Assert.Contains("sales_proposal_conversion_items", sql);
        Assert.Contains("rule_snapshot", sql);
        Assert.Contains("sales_commission_adjustments", sql);
    }

    [Fact]
    public void ProposalCommandsLockAndRejectChangedIdempotentContent()
    {
        var service = File.ReadAllText(Path.Combine(Root, "src/Modules/Agro360.Infrastructure/Services/Commercial360Service.cs"));
        Assert.Contains("for update of p", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Chave idempotente já utilizada com conteúdo diferente", service);
        Assert.Contains("current_version=@Version", service);
        Assert.Contains("pricing_snapshot", service);
    }
}
