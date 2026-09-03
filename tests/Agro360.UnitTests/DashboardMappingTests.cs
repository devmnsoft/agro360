using Agro360.Infrastructure.Services;
using Xunit;

namespace Agro360.UnitTests;

public sealed class DashboardMappingTests
{
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
