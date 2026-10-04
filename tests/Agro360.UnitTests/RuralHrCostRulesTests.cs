using Agro360.Domain.People;
using Xunit;

namespace Agro360.UnitTests;

public sealed class RuralHrCostRulesTests
{
    private static readonly Guid Role = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly DateOnly Day = new(2026, 10, 3);

    [Fact]
    public void WorkedHoursSubtractsBreakAndRejectsInvalidExit()
    {
        var start = new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);
        var end = new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
        Assert.Equal(3.00m, RuralHrRules.WorkedHours(start, end, 60));
        Assert.Throws<ArgumentException>(() => RuralHrRules.WorkedHours(end, start, 0));
    }

    [Fact]
    public void QuoteDistinguishesMissingPieceworkFromZeroRate()
    {
        Assert.Null(RuralHrRules.QuoteLabor("PIECEWORK", 3.5m, 3m, null));
        Assert.Equal(0.0000m, RuralHrRules.QuoteLabor("HOURLY", 0m, 3m, null));
        Assert.Equal(35.0000m, RuralHrRules.QuoteLabor("PIECEWORK", 3.5m, 3m, 10m));
        Assert.Equal(76.5000m, RuralHrRules.QuoteLabor("HOURLY", 25.5m, 3m, null));
        Assert.Equal(120.0000m, RuralHrRules.QuoteLabor("DAILY", 120m, 3m, null));
    }

    [Fact]
    public void SpecificTariffBeatsGenericAndTieWithDifferentValuesIsAmbiguous()
    {
        var generic = new RuralHrTariffCandidate(Guid.Parse("00000000-0000-0000-0000-000000000001"), null, null, "HOURLY", 10m, Day);
        var role = new RuralHrTariffCandidate(Guid.Parse("00000000-0000-0000-0000-000000000002"), Role, null, "HOURLY", 20m, Day);
        var both = new RuralHrTariffCandidate(Guid.Parse("00000000-0000-0000-0000-000000000003"), Role, "COLHEITA", "HOURLY", 30m, Day);
        Assert.True(RuralHrRules.TrySelectTariff([generic, role, both], Role, "colheita", out var selected));
        Assert.Equal(both.Id, selected.Id);

        var other = both with { Id = Guid.Parse("00000000-0000-0000-0000-000000000004"), RateValue = 31m };
        Assert.Throws<InvalidOperationException>(() => RuralHrRules.TrySelectTariff([both, other], Role, "COLHEITA", out _));
    }

    [Fact]
    public void SameScoreAndSameValueUsesLatestValidityThenSmallestId()
    {
        var older = new RuralHrTariffCandidate(Guid.Parse("00000000-0000-0000-0000-00000000000b"), Role, null, "HOURLY", 20m, Day.AddDays(-2));
        var newerHighId = new RuralHrTariffCandidate(Guid.Parse("00000000-0000-0000-0000-00000000000c"), Role, null, "HOURLY", 20m, Day);
        var newerLowId = new RuralHrTariffCandidate(Guid.Parse("00000000-0000-0000-0000-00000000000a"), Role, null, "HOURLY", 20m, Day);
        Assert.True(RuralHrRules.TrySelectTariff([older, newerHighId, newerLowId], Role, null, out var selected));
        Assert.Equal(newerLowId.Id, selected.Id);
    }

    [Fact]
    public void MissingTariffIsNotAZeroSelection()
    {
        Assert.False(RuralHrRules.TrySelectTariff([], Role, "COLHEITA", out _));
    }

    [Theory]
    [InlineData("TIME_ENTRY", "OPEN")]
    [InlineData("PERSON", "ACTIVE")]
    [InlineData("ALLOCATION", "ACTIVE")]
    public void InitialStatusIsDefinedByTheBackend(string kind, string expected)
        => Assert.Equal(expected, RuralHrRules.InitialStatus(kind));

    [Fact]
    public void ConferenceIsNotAClientStatusTransition()
        => Assert.Throws<InvalidOperationException>(() => RuralHrRules.ValidateStatusTransition("TIME_ENTRY", "CLOSED", "CONFIRMED"));
}
