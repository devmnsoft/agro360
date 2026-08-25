using Agro360.Domain.Agriculture;
using Agro360.Domain.Inventory;
using Agro360.Domain.Livestock;
using Agro360.SharedKernel;

namespace Agro360.UnitTests;

public sealed class DomainRulesTests
{
    [Fact]
    public void StockBalanceMustNeverBecomeNegative()
    {
        var balance = StockBalance.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "kg");
        balance.Receive(10);

        var exception = Assert.Throws<ConflictException>(() => balance.Consume(10.001m));

        Assert.Equal("inventory.insufficient_stock", exception.Code);
    }

    [Fact]
    public void ReservationIsNotAStockExit()
    {
        var balance = StockBalance.Create(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7(), "kg");
        balance.Receive(20);

        balance.Reserve(7);

        Assert.Equal(20, balance.Available);
        Assert.Equal(7, balance.Reserved);
    }

    [Fact]
    public void AnimalSaleIsBlockedDuringWithdrawalPeriod()
    {
        var animal = Animal.Register(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "BOV-001",
            "Bovino",
            "Fêmea",
            new DateOnly(2024, 1, 1));
        animal.ApplyTreatment(new DateOnly(2026, 8, 20), 14);

        var exception = Assert.Throws<ConflictException>(() => animal.Sell(new DateOnly(2026, 8, 25)));

        Assert.Equal("livestock.withdrawal_period_active", exception.Code);
    }

    [Fact]
    public void WeighingCalculatesAverageDailyGain()
    {
        var animal = Animal.Register(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "BOV-002",
            "Bovino",
            "Macho",
            new DateOnly(2024, 1, 1));
        animal.Weigh(300, new DateOnly(2026, 8, 1));

        var dailyGain = animal.Weigh(324, new DateOnly(2026, 8, 25));

        Assert.True(dailyGain.HasValue);
        Assert.Equal(1m, dailyGain.GetValueOrDefault());
    }

    [Fact]
    public void SeasonRequiresAValidPeriod()
    {
        Assert.Throws<DomainException>(() => Season.Create(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            "Soja 2027",
            "Soja",
            new DateOnly(2027, 5, 1),
            new DateOnly(2027, 4, 1)));
    }

    [Fact]
    public void MoneyRejectsOperationsBetweenCurrencies()
    {
        var brl = new Money(10, "BRL");
        var usd = new Money(5, "USD");

        var exception = Assert.Throws<DomainException>(() => _ = brl + usd);

        Assert.Equal("money.currency_mismatch", exception.Code);
    }
}
