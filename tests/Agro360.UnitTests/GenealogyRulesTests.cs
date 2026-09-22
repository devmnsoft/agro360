using Agro360.Domain.Agriculture;
using Agro360.SharedKernel;
using Xunit;

namespace Agro360.UnitTests;

public sealed class GenealogyRulesTests
{
    [Fact]
    public void PersistenceGuardsConfirmedLinksAndCyclesPerTenant()
    {
        var root = FindRepositoryRoot();
        var migration = File.ReadAllText(Path.Combine(root, "database/migrations/102_genealogy_integrity.sql"));
        var service = File.ReadAllText(Path.Combine(root, "src/Modules/Agro360.Infrastructure/Services/HarvestService.cs"));

        Assert.Contains("before update or delete", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("with recursive descendants", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("tenant_id = new.tenant_id", migration, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("genealogy.cycle_detected", service);
        Assert.Contains("genealogy.relationship_immutable", service);
        Assert.DoesNotContain("do update set\n                    quantity = excluded.quantity", service);
    }

    [Fact]
    public void ManualLinkContractCarriesAuditJustificationMetadata()
    {
        var command = new Agro360.Application.Contracts.RecordGenealogyLinkCommand("MANUAL", "RECEIPT",
            Guid.NewGuid(), "STOCK_LOT", Guid.NewGuid(), null, null, "LT-1", 1m, "kg",
            "{\"justification\":\"Conferência documental\"}", "key-1");
        Assert.Contains("justification", command.Metadata, StringComparison.Ordinal);
    }

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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MNSOFT.Agro360.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }
}
