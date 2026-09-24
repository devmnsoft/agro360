namespace Agro360.ArchitectureTests;

public sealed class FulfillmentOperationalIntegrityTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public void DispatchPersistsTenantScopedIdempotencyIdentity()
    {
        var sql = File.ReadAllText(Path.Combine(Root, "database/migrations/109_fulfillment_operational_view.sql"));
        Assert.Contains("tenant_id, dispatch_idempotency_key", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dispatch_request_hash", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("check", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void OperationalViewDerivesQuantitiesFromCanonicalReservations()
    {
        var service = File.ReadAllText(Path.Combine(Root, "src/Modules/Agro360.Infrastructure/Services/LogisticsService.cs"));
        Assert.Contains("sum(r.quantity) filter(where r.status='ACTIVE')", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sum(r.consumed_quantity)", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("i.quantity-i.cancelled_quantity-coalesce(sum(r.consumed_quantity),0) pending_quantity", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Expedição já confirmada por outra requisição", service);
    }

    [Fact]
    public void OperationalPageExplainsSubsetInvariantAndDoesNotAskForIdentifiers()
    {
        var page = File.ReadAllText(Path.Combine(Root, "src/Hosts/Agro360.Web/Pages/Logistics/Index.cshtml"));
        var script = File.ReadAllText(Path.Combine(Root, "src/Hosts/Agro360.Web/wwwroot/js/logistics.js"));
        Assert.Contains("Como funciona esta tela", page);
        Assert.DoesNotContain("type=\"text\" name=\"warehouseId\"", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Reserva ativa e separação são subconjuntos do pendente", script);
        Assert.Contains("data-order-detail", script);
    }

    [Fact]
    public void OperationalClosureUsesCommercialItemLockAndAuditableQuantities()
    {
        var service = File.ReadAllText(Path.Combine(Root, "src/Modules/Agro360.Infrastructure/Services/LogisticsService.cs"));
        var migration = File.ReadAllText(Path.Combine(Root, "database/migrations/110_fulfillment_operational_closure.sql"));
        Assert.Contains("fulfillment:item:", service);
        Assert.Contains("quantity-i.cancelled_quantity", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fulfillment_order_item_cancellations", migration);
        Assert.Contains("fulfillment_reservation_releases", migration);
        Assert.Contains("fulfillment_operation_requests", migration);
    }

    [Fact]
    public void DispatchReplaySurvivesLaterStateEvolution()
    {
        var service = File.ReadAllText(Path.Combine(Root, "src/Modules/Agro360.Infrastructure/Services/LogisticsService.cs"));
        Assert.Contains("if (shipment.DispatchIdempotencyKey is not null)", service);
        Assert.DoesNotContain("shipment.Status == \"DISPATCHED\" || shipment.Status == \"IN_DELIVERY\"", service);
    }
}
