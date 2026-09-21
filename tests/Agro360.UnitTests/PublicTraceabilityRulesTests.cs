using System.Text.Json;
using Agro360.Application.Contracts;
using Agro360.Domain.Agriculture;
using Agro360.SharedKernel;
using Xunit;

namespace Agro360.UnitTests;

public sealed class PublicTraceabilityRulesTests
{
    [Fact]
    public void ApprovedLotWithOriginCanBePublished() =>
        PublicTraceabilityRules.EnsurePublishable("APPROVED", hasOrigin: true);

    [Theory]
    [InlineData("AWAITING_INSPECTION")]
    [InlineData("BLOCKED")]
    [InlineData("QUARANTINE")]
    public void QualityPendingOrBlockedLotCannotBePublished(string status)
    {
        var exception = Assert.Throws<ConflictException>(() =>
            PublicTraceabilityRules.EnsurePublishable(status, hasOrigin: true));
        Assert.Equal("traceability.quality_not_approved", exception.Code);
    }

    [Fact]
    public void LotWithoutSafeOriginCannotBePublished()
    {
        var exception = Assert.Throws<ConflictException>(() =>
            PublicTraceabilityRules.EnsurePublishable("APPROVED", hasOrigin: false));
        Assert.Equal("traceability.origin_missing", exception.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("x")]
    public void RevocationRequiresMeaningfulReason(string? reason)
    {
        var exception = Assert.Throws<DomainException>(() => PublicTraceabilityRules.RequireRevocationReason(reason));
        Assert.Equal("traceability.revocation_reason_required", exception.Code);
    }

    [Fact]
    public void PublicContractDoesNotExposeInternalIdentifiersOrFinancialData()
    {
        var payload = new PublicTraceDto("opaque", "Café", "LT-001", "Origem rastreada", "Fazenda Boa",
            "Safra 2026", "Café", ["Conforme"], [new("Qualidade", "Aprovado", DateTimeOffset.UtcNow)],
            "LIBERADO", DateTimeOffset.UtcNow);
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.DoesNotContain("tenantId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("userId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("guid", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("price", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cost", json, StringComparison.OrdinalIgnoreCase);
    }
}
