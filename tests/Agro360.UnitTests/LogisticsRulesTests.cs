using Agro360.Domain.Logistics;
using Agro360.SharedKernel;

namespace Agro360.UnitTests;

public sealed class LogisticsRulesTests
{
    [Theory]
    [InlineData("ROAD")]
    [InlineData("RIVER")]
    [InlineData("MIXED")]
    public void TripAcceptsSupportedRouteTypes(string routeType)
        => LogisticsRules.ValidateTrip("Fazenda Norte", "Armazém Central", routeType, 42, 750, 12, "PLANNED");

    [Fact]
    public void TripRejectsSameOriginAndDestination()
    {
        var error = Assert.Throws<DomainException>(() =>
            LogisticsRules.ValidateTrip("Porto", " porto ", "RIVER", 10, 100, 2, "PLANNED"));

        Assert.Equal("logistics.route_same_endpoint", error.Code);
    }

    [Theory]
    [InlineData("PLANNED")]
    [InlineData("AWAITING_PICKUP")]
    [InlineData("WITH_OCCURRENCE")]
    [InlineData("DELIVERED")]
    public void DeliveryRequiresTripInTransit(string status)
    {
        var error = Assert.Throws<DomainException>(() => LogisticsRules.EnsureCanDeliver(status));

        Assert.Equal("logistics.delivery_requires_dispatch", error.Code);
    }

    [Fact]
    public void InTransitTripCanBeDelivered()
        => LogisticsRules.EnsureCanDeliver("in_transit");
}
