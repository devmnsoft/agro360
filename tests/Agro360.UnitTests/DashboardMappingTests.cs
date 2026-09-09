using Agro360.Domain.Properties;
using Agro360.Infrastructure.Services;
using Agro360.SharedKernel;
using Xunit;

namespace Agro360.UnitTests;

public sealed class DashboardMappingTests
{
    [Fact]
    public void PropertyRulesValidateStateAreaAndGeoJson()
    {
        Assert.Equal("PA", PropertyRules.NormalizeState("pa"));
        Assert.Equal(
            "agro360.properties_state_invalid",
            Assert.Throws<DomainException>(() => PropertyRules.NormalizeState("Pará")).Code);

        PropertyRules.EnsureFieldsFit(100m, 60m, 40m);
        Assert.Equal(
            "agro360.properties_field_area_exceeded",
            Assert.Throws<DomainException>(() => PropertyRules.EnsureFieldsFit(100m, 60m, 40.0001m)).Code);

        Assert.Contains("Polygon", PropertyRules.NormalizeBoundary("""{"type":"Polygon","coordinates":[[[-48,-1],[-47,-1],[-48,-1]]]}"""));
        Assert.Equal(
            "agro360.properties_boundary_invalid",
            Assert.Throws<DomainException>(() => PropertyRules.NormalizeBoundary("""{"type":"Point","coordinates":[-48,-1]}""")).Code);
    }

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void RecentOperationRowMapsEveryValueAndNormalizesTimestamp(DateTimeKind kind)
    {
        var id = Guid.NewGuid();
        var occurredAt = DateTime.SpecifyKind(new DateTime(2026, 9, 3, 14, 25, 30), kind);
        var row = new DashboardService.RecentOperationRow
        {
            Id = id,
            ModuleName = "AGRICULTURE",
            OperationType = "HARVEST",
            Description = "HARVEST · Talhão Norte",
            Amount = 1234.56m,
            OccurredAt = occurredAt,
            Status = "COMPLETED"
        };

        var result = DashboardService.MapRecentOperation(row);

        Assert.Equal(id, result.Id);
        Assert.Equal("AGRICULTURE", result.Module);
        Assert.Equal("HARVEST", result.Type);
        Assert.Equal("HARVEST · Talhão Norte", result.Description);
        Assert.Equal(1234.56m, result.Amount);
        Assert.Equal(TimeSpan.Zero, result.OccurredAt.Offset);
        Assert.Equal(new DateTime(2026, 9, 3, 14, 25, 30, DateTimeKind.Utc), result.OccurredAt.UtcDateTime);
        Assert.Equal("COMPLETED", result.Status);
    }

    [Fact]
    public void RecentOperationRowSupportsNullAmount()
    {
        var result = DashboardService.MapRecentOperation(new DashboardService.RecentOperationRow
        {
            ModuleName = "AGRICULTURE",
            OperationType = "MONITORING",
            Description = "Monitoramento",
            OccurredAt = DateTime.UtcNow,
            Status = "PLANNED"
        });

        Assert.Null(result.Amount);
    }
}
