using Agro360.Application.Contracts;
using Agro360.Domain.Fleet;
using Agro360.Domain.Livestock;
using Agro360.Infrastructure.Services;
using Agro360.SharedKernel;
using Xunit;

namespace Agro360.UnitTests;

public sealed class LivestockDashboardContractTests
{
    [Fact]
    public void EmptyDashboardIsAValidNonNullableResponse()
    {
        var dashboard = LivestockDashboardDto.Empty;

        Assert.Equal("Sem dados", dashboard.Status);
        Assert.Equal(0, dashboard.ActiveAnimals);
        Assert.Equal(0, dashboard.ActiveHerds);
        Assert.Equal(0, dashboard.PendingHandlings);
        Assert.Equal(0, dashboard.HealthAlerts);
        Assert.Equal(0, dashboard.RecentWeighings);
        Assert.Empty(dashboard.BySpecies);
        Assert.Empty(dashboard.ByCategory);
    }

    [Fact]
    public void InterfaceAndImplementationExposeTheSameStrongDashboardContract()
    {
        var interfaceMethod = typeof(ILivestock360Service).GetMethod(nameof(ILivestock360Service.DashboardAsync))
            ?? throw new InvalidOperationException("DashboardAsync não foi encontrado na interface.");
        var implementationMethod = typeof(Livestock360Service).GetMethod(nameof(Livestock360Service.DashboardAsync))
            ?? throw new InvalidOperationException("DashboardAsync não foi encontrado na implementação.");

        Assert.Equal(typeof(Task<LivestockDashboardDto>), interfaceMethod.ReturnType);
        Assert.Equal(interfaceMethod.ReturnType, implementationMethod.ReturnType);
    }

    [Fact]
    public void AnimalLookupAndUpdateDoNotExposeDynamicContracts()
    {
        var lookup = typeof(ILivestock360Service).GetMethod(nameof(ILivestock360Service.GetAnimalAsync))
            ?? throw new InvalidOperationException("GetAnimalAsync não foi encontrado.");
        var update = typeof(ILivestock360Service).GetMethod(nameof(ILivestock360Service.UpdateAnimalAsync))
            ?? throw new InvalidOperationException("UpdateAnimalAsync não foi encontrado.");

        Assert.Equal(typeof(Task<AnimalDto>), lookup.ReturnType);
        Assert.Equal(typeof(Task<AnimalDto>), update.ReturnType);
    }

    [Fact]
    public void HerdServiceExposesOperationalContractsWithoutDynamicDashboard()
    {
        var reserve = typeof(ILivestockHerdService).GetMethod(nameof(ILivestockHerdService.ReserveAsync))
            ?? throw new InvalidOperationException("ReserveAsync não foi encontrado.");
        var weigh = typeof(ILivestockHerdService).GetMethod(nameof(ILivestockHerdService.RecordWeighingAsync))
            ?? throw new InvalidOperationException("RecordWeighingAsync não foi encontrado.");
        var export = typeof(ILivestockHerdService).GetMethod(nameof(ILivestockHerdService.ExportCsvAsync))
            ?? throw new InvalidOperationException("ExportCsvAsync não foi encontrado.");
        var implementation = typeof(LivestockHerdService);

        Assert.Equal(typeof(Task<Guid>), reserve.ReturnType);
        Assert.Equal(typeof(Task<Guid>), weigh.ReturnType);
        Assert.Equal(typeof(Task<byte[]>), export.ReturnType);
        Assert.NotNull(implementation.GetMethod(nameof(LivestockHerdService.ReserveAsync)));
        Assert.True(typeof(ILivestockHerdService).IsAssignableFrom(implementation));
        Assert.NotNull(LivestockDashboardDto.Empty.ReferenceDate);
    }

    [Fact]
    public void LivestockRulesRejectDoubleCountMissingConversionAndPartialFullAttendance()
    {
        var conflict = Assert.Throws<ConflictException>(() => LivestockRules.PreventDoubleCount("QUANTITY", true));
        Assert.Equal("livestock.double_count", conflict.Code);
        var conversion = Assert.Throws<DomainException>(() => LivestockRules.ConvertWeight(10, "arroba", "kg", null));
        Assert.Equal("livestock.conversion_required", conversion.Code);
        Assert.Equal(150m, LivestockRules.ConvertWeight(10, "arroba", "kg", 15));
        LivestockRules.EnsureCompletionCounts(4, 2, 1, 1, 0);
        Assert.Throws<ConflictException>(() => LivestockRules.RejectSilentFullAttendance(4, 2));
        var csv = "=CMD";
        LivestockRules.EnsureCsvSafe(ref csv);
        Assert.StartsWith("'", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void FleetRulesCoverTransitionsAvailabilityAndCsvSafety()
    {
        FleetRules.EnsureWorkOrderTransition("OPEN", "PLANNED");
        Assert.Throws<ConflictException>(() => FleetRules.EnsureWorkOrderTransition("COMPLETED", "OPEN"));
        Assert.Equal("OVERDUE", FleetRules.PlanDueState(DateTimeOffset.UtcNow.AddDays(-1), null, null, true));
        Assert.Equal("INSUFFICIENT_DATA", FleetRules.PlanDueState(null, 100, null, false));
        Assert.Equal(90m, FleetRules.Availability(100, 10));
        var fuel = "=SUM(1)";
        FleetRules.EnsureCsvSafe(ref fuel);
        Assert.StartsWith("'", fuel, StringComparison.Ordinal);
        Assert.True(typeof(IFleetOperationsService).IsAssignableFrom(typeof(FleetOperationsService)));
    }
}
