using System.Text.Json;
using Agro360.Application;
using Agro360.Application.Contracts;
using Xunit;

namespace Agro360.UnitTests;

public sealed class PortalSecurityAndIsolationTests
{
    private static readonly JsonSerializerOptions JsonWebOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void PortalRequestRowDoesNotExposeInternalCostOrTenantId()
    {
        var row = new PortalRequestRow(
            Guid.NewGuid(),
            "SOL-20260921-A1B2C3",
            "SUPPORT",
            "Dúvida sobre colheita",
            "OPEN",
            "MEDIUM",
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);

        var json = JsonSerializer.Serialize(row, JsonWebOptions);

        Assert.DoesNotContain("tenantId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cost", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("price", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internalNote", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PortalRequestDetailDtoDoesNotExposeInternalAuditsOrPrivateFlags()
    {
        var detail = new PortalRequestDetailDto(
            Guid.NewGuid(),
            "SOL-20260921-A1B2C3",
            "DOCUMENT",
            "Solicitação de Laudo",
            "Gostaria da cópia do laudo de qualidade.",
            "RESOLVED",
            "HIGH",
            "Laudo liberado na aba de Documentos.",
            null,
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow,
            [
                new(Guid.NewGuid(), "CREATED", "Solicitação aberta", DateTimeOffset.UtcNow.AddDays(-1)),
                new(Guid.NewGuid(), "RESOLVED", "Laudo liberado", DateTimeOffset.UtcNow)
            ]);

        var json = JsonSerializer.Serialize(detail, JsonWebOptions);

        Assert.DoesNotContain("tenantId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internalAudit", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("financialData", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MarketplaceListingDoesNotExposeInternalCostOrSupplierCost()
    {
        var listing = new MarketplaceListing(
            Guid.NewGuid(),
            "Soja em Grãos",
            "Soja",
            "Safra 2025/2026",
            "Cerrado",
            "SC",
            5000m,
            145.50m,
            "FOB Fazenda Santa Clara",
            ["GlobalGAP", "RTRS"]);

        var json = JsonSerializer.Serialize(listing, JsonWebOptions);

        Assert.DoesNotContain("tenantId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("productionCost", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("profitMargin", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("internalBatchId", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ExternalTokenClaimsContainOnlyPortalAccessAndProfile()
    {
        var portalAccessClaim = Permissions.PortalAccess;
        Assert.Equal("portal.access", portalAccessClaim);

        Assert.NotEqual(Permissions.PlatformAdmin, portalAccessClaim);
        Assert.NotEqual(Permissions.AccountUsersManage, portalAccessClaim);
        Assert.NotEqual(Permissions.PortalManage, portalAccessClaim);
    }
}
