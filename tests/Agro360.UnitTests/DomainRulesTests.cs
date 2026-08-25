using Agro360.Domain.Agriculture;
using Agro360.Domain.Inventory;
using Agro360.Domain.Livestock;
using Agro360.Domain.Operations;
using Agro360.Domain.Storage;
using Agro360.Domain.Finance;
using Agro360.Domain.Traceability;
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

    [Fact]
    public void PurchaseCannotBeApprovedWithoutItems() =>
        Assert.Throws<DomainException>(() => OperationalRules.PurchaseTotal([], 0, 0, 0));

    [Fact]
    public void CancelledPurchaseCannotBeReceived() =>
        Assert.Throws<DomainException>(() => OperationalRules.Receive(PurchaseStatus.Cancelled, 10, 0, 1));

    [Fact]
    public void PartialAndFullReceiptCloseAtTheCorrectTime()
    {
        Assert.Equal(PurchaseStatus.PartiallyReceived, OperationalRules.Receive(PurchaseStatus.Approved, 10, 0, 4));
        Assert.Equal(PurchaseStatus.Received, OperationalRules.Receive(PurchaseStatus.PartiallyReceived, 10, 4, 6));
    }

    [Fact]
    public void FuelAndMaintenanceCostsAreCalculated()
    {
        Assert.Equal(525m, OperationalRules.FuelTotal(50, 10.5m));
        Assert.Equal(350m, OperationalRules.MaintenanceTotal(200, 150));
    }

    [Fact] public void LivestockRejectsNegativeProduction() => Assert.Throws<DomainException>(() => Agro360.Domain.Livestock.LivestockRules.NonNegative(-1, "Produção"));
    [Fact] public void LivestockCalculatesAverageDailyGain() => Assert.Equal(1m, Agro360.Domain.Livestock.LivestockRules.AverageDailyGain(300, new DateOnly(2026,8,1), 324, new DateOnly(2026,8,25)));
    [Fact] public void ReproductionRequiresFemale() => Assert.Throws<DomainException>(() => Agro360.Domain.Livestock.LivestockRules.RequireFemale("M"));
    [Fact] public void PregnancyDiagnosisCalculatesExpectedBirth() => Assert.Equal(new DateOnly(2027,6,4), Agro360.Domain.Livestock.LivestockRules.ExpectedBirth(new DateOnly(2026,8,25), "BOVINO"));
    [Fact] public void NutritionPlanRequiresItems() => Assert.Throws<DomainException>(() => Agro360.Domain.Livestock.LivestockRules.DietCost([]));
    [Fact] public void NutritionPlanCalculatesCost() => Assert.Equal(25m, Agro360.Domain.Livestock.LivestockRules.DietCost([(10m,2.5m)]));
    [Fact] public void FinancialTitleCalculatesDiscountAndCharges() => Assert.Equal(102m, FinanceRules.FinalAmount(100, 3, 2, 3));
    [Fact] public void FinancialTitleRejectsNegativeValues() => Assert.Throws<DomainException>(() => FinanceRules.FinalAmount(100, -1, 0, 0));
    [Fact] public void ChartOfAccountsValidatesType() => Assert.Throws<DomainException>(() => FinanceRules.Account("1", "Conta", "UNKNOWN", "DEBIT"));
    [Fact] public void ReceiptNetWeightSubtractsTare() => Assert.Equal(8_500m, StorageRules.NetWeight(10_000, 1_500));
    [Fact] public void ReceiptRejectsTareAboveGross() => Assert.Throws<DomainException>(() => StorageRules.NetWeight(1_000, 1_001));
    [Fact] public void TechnicalDiscountCalculatesFinalWeight() => Assert.Equal(9_500m, StorageRules.FinalWeight(10_000, 5));
    [Fact] public void StructureRejectsCapacityOverflow() => Assert.Throws<DomainException>(() => StorageRules.Capacity(100, 101));
    [Fact] public void BlockedLotCannotBeDispatched() => Assert.Throws<DomainException>(() => StorageRules.LotWithdrawal(100, 10, true));
    [Fact] public void LotCannotDispatchAboveBalance() => Assert.Throws<DomainException>(() => StorageRules.LotWithdrawal(100, 101, false));
    [Fact] public void FreightCostPerTonneIsCalculated() => Assert.Equal(125m, StorageRules.Freight(1_000, 200, 8));
    [Fact] public void FreightRejectsNegativeValue() => Assert.Throws<DomainException>(() => StorageRules.Freight(-1, 10, 1));
    [Fact] public void TraceableLotRequiresOrigin() => Assert.Throws<DomainException>(() => Sprint10Rules.RequireOrigin(null,""));
    [Fact] public void TucupiMinimumBoilingTimeIsEnforced() => Assert.False(Sprint10Rules.ProcessingComplies(DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddMinutes(20),100,30,95));
    [Fact] public void CompliantTucupiBoilingIsApproved() => Assert.True(Sprint10Rules.ProcessingComplies(DateTimeOffset.UtcNow,DateTimeOffset.UtcNow.AddMinutes(40),98,30,95));
    [Fact] public void LedgerHashChainDependsOnPreviousHash() => Assert.NotEqual(Sprint10Rules.LedgerHash(null,"{}","m"),Sprint10Rules.LedgerHash("abc","{}","m"));
    [Fact] public void InterdictedSegmentNeedsAuthorization() => Assert.Throws<DomainException>(() => Sprint10Rules.RequireRouteAuthorization("INTERDICTED",false));
    [Fact] public void SellerPercentageCommissionIsCalculated() => Assert.Equal(100m,Sprint10Rules.Commission(1000,10,true));
    [Fact] public void SplitMustBalanceSaleTotal() => Assert.Throws<DomainException>(() => Sprint10Rules.ValidateSplit(100,[50,40]));
}

public sealed class Agriculture360RulesTests
{
    [Fact]
    public void Plan_rejects_duplicate_fields()
    {
        var field = Guid.NewGuid();
        Assert.Throws<DomainException>(() => Agro360.Domain.Agriculture.Agriculture360Rules.UniqueFields([field, field]));
    }

    [Fact]
    public void Application_rejects_non_positive_dose() =>
        Assert.Throws<DomainException>(() => Agro360.Domain.Agriculture.Agriculture360Rules.Positive(0, "Dose"));

    [Fact]
    public void Irrigation_rejects_inverted_period() =>
        Assert.Throws<DomainException>(() => Agro360.Domain.Agriculture.Agriculture360Rules.Period(DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddHours(-1)));

    [Theory]
    [InlineData(-1, 25, 80, 5)]
    [InlineData(1, 80, 80, 5)]
    [InlineData(1, 25, 101, 5)]
    [InlineData(1, 25, 80, -1)]
    public void Weather_rejects_values_outside_ranges(decimal rain, decimal temperature, decimal humidity, decimal wind) =>
        Assert.Throws<DomainException>(() => Agro360.Domain.Agriculture.Agriculture360Rules.Weather(rain, temperature, humidity, wind));
}

public sealed class MobileRulesTests
{
    [Fact] public void Valid_location_is_accepted()=>Agro360.Domain.Mobile.MobileRules.ValidateLocation(-3.1m,-60.0m,8m);
    [Theory]
    [InlineData(-91,-60)] [InlineData(91,-60)] [InlineData(-3,-181)] [InlineData(-3,181)]
    public void Invalid_location_is_rejected(decimal latitude,decimal longitude)=>Assert.Throws<Agro360.SharedKernel.DomainException>(()=>Agro360.Domain.Mobile.MobileRules.ValidateLocation(latitude,longitude,1));
    [Fact] public void Negative_quick_record_is_rejected()=>Assert.Throws<Agro360.SharedKernel.DomainException>(()=>Agro360.Domain.Mobile.MobileRules.ValidateQuickRecord("WEIGHING",-1,DateTimeOffset.UtcNow));
    [Fact] public void Empty_entity_is_rejected()=>Assert.Throws<Agro360.SharedKernel.DomainException>(()=>Agro360.Domain.Mobile.MobileRules.ValidateEntity("animal",Guid.Empty));
}
