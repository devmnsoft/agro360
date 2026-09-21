using Agro360.Domain.Agriculture;
using Agro360.SharedKernel;
using Xunit;

namespace Agro360.UnitTests;

public sealed class GenealogyRulesTests
{
    [Fact]
    public void ValidateLinkAcceptsACompleteTraceabilityLink()
    {
        var exception = Record.Exception(() => GenealogyRules.ValidateLink(
            "HARVEST_TO_LOT", "HARVEST_RECORD", Guid.NewGuid(), "STOCK_LOT", Guid.NewGuid(),
            250m, "kg", "genealogy-001"));

        Assert.Null(exception);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void ValidateLinkRejectsNonPositiveQuantity(decimal quantity)
    {
        var exception = Assert.Throws<DomainException>(() => GenealogyRules.ValidateLink(
            "HARVEST_TO_LOT", "HARVEST_RECORD", Guid.NewGuid(), "STOCK_LOT", Guid.NewGuid(),
            quantity, "kg", "genealogy-001"));

        Assert.Equal("genealogy.quantity_invalid", exception.Code);
    }

    [Fact]
    public void ValidateLinkRejectsSelfReference()
    {
        var id = Guid.NewGuid();

        var exception = Assert.Throws<DomainException>(() => GenealogyRules.ValidateLink(
            "RELATED", "STOCK_LOT", id, "stock_lot", id, null, null, "genealogy-002"));

        Assert.Equal("genealogy.self_reference", exception.Code);
    }

    [Fact]
    public void CanConsolidateStockNormalizesEquivalentUnits()
    {
        var canConsolidate = GenealogyRules.CanConsolidateStock([" KG ", "kg", "Kg"], out var unit, out var reason);

        Assert.True(canConsolidate);
        Assert.Equal("kg", unit);
        Assert.Null(reason);
    }

    [Fact]
    public void CanConsolidateStockRejectsHeterogeneousUnits()
    {
        var canConsolidate = GenealogyRules.CanConsolidateStock(["kg", "t"], out var unit, out var reason);

        Assert.False(canConsolidate);
        Assert.Null(unit);
        Assert.NotNull(reason);
    }

    [Fact]
    public void CalculateNetDeliveredQuantityNeverProducesNegativeBalance()
    {
        Assert.Equal(0m, GenealogyRules.CalculateNetDeliveredQuantity(5m, 8m));
    }
}
