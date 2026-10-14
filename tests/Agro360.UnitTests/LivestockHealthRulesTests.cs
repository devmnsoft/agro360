using Agro360.Domain.Livestock;
using Agro360.SharedKernel;
using Xunit;

namespace Agro360.UnitTests;

public sealed class LivestockHealthRulesTests
{
    private static readonly DateOnly Today = new(2026, 10, 13);

    [Fact]
    public void ProtocolPurposeClosedSetMatchesCatalogCheck()
    {
        var expectedProtocols = new[] { "MILK", "SLAUGHTER", "SALE", "OPERATIONAL", "DEMO" };
        var expectedRestrictions = new[] { "MILK", "SLAUGHTER", "SALE", "OPERATIONAL" };
        Assert.Equal(expectedProtocols, LivestockRules.ProtocolPurposes);
        // A restricao operacional continua sem DEMO (constrangimento do banco).
        Assert.Equal(expectedRestrictions, LivestockRules.RestrictionPurposes);
    }

    [Theory]
    [InlineData("MILK")]
    [InlineData("SLAUGHTER")]
    [InlineData("SALE")]
    [InlineData("OPERATIONAL")]
    [InlineData("DEMO")]
    [InlineData("milk")]
    public void ValidProtocolPurposePasses(string purpose)
    {
        LivestockRules.EnsureProtocolPurpose(purpose);
    }

    [Theory]
    [InlineData("VACCINATION")]
    [InlineData("OTHER")]
    [InlineData("")]
    public void InvalidProtocolPurposeThrowsCatalogCode(string purpose)
    {
        Assert.Equal(
            "livestock.protocol_purpose_invalid",
            Assert.Throws<DomainException>(() => LivestockRules.EnsureProtocolPurpose(purpose)).Code);
    }

    [Fact]
    public void ProtocolWindowAllowsOpenEndAndSameDay()
    {
        LivestockRules.EnsureProtocolWindow(new DateOnly(2026, 1, 1), null);
        LivestockRules.EnsureProtocolWindow(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1));
    }

    [Fact]
    public void ProtocolWindowRejectsEndBeforeStart()
    {
        Assert.Equal(
            "livestock.protocol_window_invalid",
            Assert.Throws<DomainException>(() => LivestockRules.EnsureProtocolWindow(new DateOnly(2026, 2, 1), new DateOnly(2026, 1, 31))).Code);
    }

    [Fact]
    public void WithdrawalDaysAcceptsZeroAndPositive()
    {
        Assert.Equal(0, LivestockRules.EnsureWithdrawalDays(0));
        Assert.Equal(21, LivestockRules.EnsureWithdrawalDays(21));
    }

    [Fact]
    public void WithdrawalDaysRejectsNegative()
    {
        Assert.Equal(
            "livestock.protocol_withdrawal_invalid",
            Assert.Throws<DomainException>(() => LivestockRules.EnsureWithdrawalDays(-1)).Code);
    }

    [Fact]
    public void ReasonLengthKeepsShortTextAndTruncatesLongProtocolNames()
    {
        const string shortReason = "Carência por protocolo BQ v2026.1";
        Assert.Equal(shortReason, LivestockRules.EnsureReasonLength(shortReason, 240));

        var longName = new string('x', 300);
        var truncated = LivestockRules.EnsureReasonLength($"Carência por protocolo {longName} v999", 240);
        Assert.True(truncated.Length <= 240);
    }

    [Fact]
    public void TerminalRestrictionStatusesAreNeverRewritten()
    {
        Assert.Equal("RELEASED", LivestockRules.EffectiveRestrictionStatus("RELEASED", new DateOnly(2020, 1, 1), Today));
        Assert.Equal("EXPIRED", LivestockRules.EffectiveRestrictionStatus("EXPIRED", null, Today));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    public void ActiveRestrictionIsEffectiveUntilItsLastDay(int? offset)
    {
        var until = offset is null ? null : (DateOnly?)(Today.AddDays(offset.Value));
        Assert.Equal("ACTIVE", LivestockRules.EffectiveRestrictionStatus("ACTIVE", until, Today));
    }

    [Fact]
    public void ActiveRestrictionPastItsDateBecomesDerivedExpired()
    {
        // Caso real do seed ...153: ACTIVE com expected_until 2026-09-25 ja deve valer como EXPIRED em 2026-10-13.
        Assert.Equal("EXPIRED", LivestockRules.EffectiveRestrictionStatus("ACTIVE", new DateOnly(2026, 9, 25), Today));
    }
}
