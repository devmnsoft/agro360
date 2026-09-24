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
        Assert.Contains("sum(r.quantity-r.consumed_quantity-r.released_quantity) filter(where r.status='ACTIVE')", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("sum(r.consumed_quantity)", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("i.quantity-i.cancelled_quantity-coalesce((select sum(r.consumed_quantity)", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Expedição já confirmada por outra requisição", service);
    }

    [Fact]
    public void InitialUiReservationDoesNotPretendThatCheckingWasCompleted()
    {
        var service = File.ReadAllText(Path.Combine(Root, "src/Modules/Agro360.Infrastructure/Services/LogisticsService.cs"));
        var script = File.ReadAllText(Path.Combine(Root, "src/Hosts/Agro360.Web/wwwroot/js/logistics.js"));
        Assert.Contains("pickedQuantity:0,checkedQuantity:0", script);
        Assert.Contains("divergenceReason:null", script);
        Assert.Contains("x.CheckedQuantity > 0 && x.CheckedQuantity != x.PickedQuantity", service);
        Assert.Contains("check_completed", service, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PartialReleaseAndReopenUseActiveBalanceAndPreserveHistory()
    {
        var service = File.ReadAllText(Path.Combine(Root, "src/Modules/Agro360.Infrastructure/Services/LogisticsService.cs"));
        var migration = File.ReadAllText(Path.Combine(Root, "database/migrations/111_fulfillment_reopen_and_active_balance.sql"));
        Assert.Contains("var active = reservation.Quantity - reservation.Consumed - reservation.Released", service);
        Assert.Contains("reserved=reserved-@Active", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("consumed_quantity=consumed_quantity+@Quantity", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("fulfillment_preparation_reopens", migration);
        Assert.Contains("previous_picked", migration);
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
