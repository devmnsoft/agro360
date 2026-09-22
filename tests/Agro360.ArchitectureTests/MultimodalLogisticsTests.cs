namespace Agro360.ArchitectureTests;

public sealed class MultimodalLogisticsTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    [Fact]
    public void PlanningIsTenantScopedIdempotentAndDoesNotMoveStock()
    {
        var sql = Read("database/migrations/103_multimodal_logistics_planning.sql");
        var service = Read("src/Modules/Agro360.Infrastructure/Services/LogisticsService.cs");
        Assert.Contains("platform_enable_tenant_rls", sql);
        Assert.Contains("unique(tenant_id,idempotency_key)", sql);
        Assert.Contains("pg_advisory_xact_lock", service);
        Assert.Contains("logistics_trip_allocations", service);
        Assert.DoesNotContain("inventory_stock_movements", service[service.IndexOf("PlanTripAsync", StringComparison.Ordinal)..service.IndexOf("TripDetailAsync", StringComparison.Ordinal)]);
    }

    [Fact]
    public void RiverLegRequiresDocumentedManualNavigationInformation()
    {
        var sql = Read("database/migrations/103_multimodal_logistics_planning.sql");
        Assert.Contains("navigation_source", sql);
        Assert.Contains("navigation_valid_until", sql);
        Assert.Contains("mode<>'RIVER'", sql);
    }

    [Fact]
    public void ApiSeparatesReadAndWriteAuthorization()
    {
        var controller = Read("src/Hosts/Agro360.Api/Controllers/LogisticsController.cs");
        Assert.Contains("HttpGet(\"{id:guid}/plan\"), Authorize(Policy = Permissions.LogisticsRead)", controller);
        Assert.Contains("HttpPost(\"plans\"), Authorize(Policy = Permissions.LogisticsWrite)", controller);
    }
}
