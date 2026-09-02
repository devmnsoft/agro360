using Agro360.Domain.Sustainability;

namespace Agro360.UnitTests;

public sealed class SustainabilityRulesTests
{
    [Fact] public void ValidatesEnvironmentalComplianceAreas() => SustainabilityRules.ValidateAreas(100m, 60m, 20m, 10m, 10m);
    [Fact] public void RejectsProductiveAreaAboveTotal() => Assert.Throws<ArgumentException>(() => SustainabilityRules.ValidateAreas(10m, 11m, 0m, 0m, 0m));
    [Fact] public void MissingEmissionFactorIsPendingNotInvented() => Assert.Null(SustainabilityRules.EstimateEmission(10m, null));
    [Fact] public void CalculatesEmissionWithDecimalPrecision() => Assert.Equal(12.345670m, SustainabilityRules.EstimateEmission(10m, 1.234567m));
    [Fact] public void RejectsIndicatorWithoutSource() => Assert.Throws<ArgumentException>(() => SustainabilityRules.ValidateIndicator("", "NUMBER", 1m, 1m));
    [Fact] public void RejectsLotWithoutOriginAsCompliant() => Assert.Throws<InvalidOperationException>(() => SustainabilityRules.ValidateLot(null, "COMPLIANT", null, false, false));
    [Fact] public void ReservationReleaseRequiresJustification() => Assert.Throws<ArgumentException>(() => SustainabilityRules.ValidateLot(Guid.NewGuid(), "RELEASED_WITH_RESERVATION", null, false, false));
    [Fact] public void BlockedFarmRequiresPermissionAndJustification() => Assert.Throws<InvalidOperationException>(() => SustainabilityRules.ValidateLot(Guid.NewGuid(), "PENDING", "review", true, false));
    [Fact] public void CriticalActionRequiresResponsible() => Assert.Throws<ArgumentException>(() => SustainabilityRules.ValidateActionPlan("CRITICAL", null, "OPEN", null, null));
}
