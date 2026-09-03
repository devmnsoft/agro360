using Agro360.Application.Contracts;
using Agro360.Infrastructure.Services;
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
}
