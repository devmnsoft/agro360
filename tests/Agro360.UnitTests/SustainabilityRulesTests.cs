using Agro360.Domain.Sustainability;

namespace Agro360.UnitTests;

public sealed class SustainabilityRulesTests
{
    [Fact] public void Validates_environmental_compliance_areas() => SustainabilityRules.ValidateAreas(100m, 60m, 20m, 10m, 10m);
    [Fact] public void Rejects_productive_area_above_total() => Assert.Throws<ArgumentException>(() => SustainabilityRules.ValidateAreas(10m, 11m, 0m, 0m, 0m));
    [Fact] public void Missing_emission_factor_is_pending_not_invented() => Assert.Null(SustainabilityRules.EstimateEmission(10m, null));
    [Fact] public void Calculates_emission_with_decimal_precision() => Assert.Equal(12.345670m, SustainabilityRules.EstimateEmission(10m, 1.234567m));
    [Fact] public void Rejects_indicator_without_source() => Assert.Throws<ArgumentException>(() => SustainabilityRules.ValidateIndicator("", "NUMBER", 1m, 1m));
    [Fact] public void Rejects_lot_without_origin_as_compliant() => Assert.Throws<InvalidOperationException>(() => SustainabilityRules.ValidateLot(null, "COMPLIANT", null, false, false));
    [Fact] public void Reservation_release_requires_justification() => Assert.Throws<ArgumentException>(() => SustainabilityRules.ValidateLot(Guid.NewGuid(), "RELEASED_WITH_RESERVATION", null, false, false));
    [Fact] public void Blocked_farm_requires_permission_and_justification() => Assert.Throws<InvalidOperationException>(() => SustainabilityRules.ValidateLot(Guid.NewGuid(), "PENDING", "review", true, false));
    [Fact] public void Critical_action_requires_responsible() => Assert.Throws<ArgumentException>(() => SustainabilityRules.ValidateActionPlan("CRITICAL", null, "OPEN", null, null));
}
