using Agro360.Domain.People;
using Xunit;

namespace Agro360.UnitTests;

public sealed class RuralHrTransitionTests
{
    [Theory]
    [InlineData("PERSON", "ACTIVE", "INACTIVE")]
    [InlineData("PERSON", "INACTIVE", "ACTIVE")]
    [InlineData("TEAM", "ACTIVE", "IN_FIELD")]
    [InlineData("TEAM", "IN_FIELD", "ACTIVE")]
    [InlineData("TRAINING", "PLANNED", "COMPLETED")]
    [InlineData("PPE", "AVAILABLE", "DELIVERED")]
    [InlineData("PPE", "DELIVERED", "RETURNED")]
    [InlineData("INCIDENT", "OPEN", "INVESTIGATING")]
    [InlineData("INCIDENT", "INVESTIGATING", "CLOSED")]
    [InlineData("CORRECTIVE_ACTION", "OPEN", "IN_PROGRESS")]
    [InlineData("CORRECTIVE_ACTION", "IN_PROGRESS", "COMPLETED")]
    public void AllowsValidStatusTransitions(string kind, string currentStatus, string targetStatus)
    {
        // Must not throw
        RuralHrRules.ValidateStatusTransition(kind, currentStatus, targetStatus);
    }

    [Theory]
    [InlineData("PERSON", "ACTIVE", "COMPLETED")]
    [InlineData("PERSON", "INACTIVE", "IN_FIELD")]
    [InlineData("TRAINING", "COMPLETED", "PLANNED")]
    [InlineData("TIME_ENTRY", "CLOSED", "OPEN")]
    public void RejectsInvalidStatusTransitions(string kind, string currentStatus, string targetStatus)
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            RuralHrRules.ValidateStatusTransition(kind, currentStatus, targetStatus));
        Assert.Contains("Transição de status inválida", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("TEAM", "ACTIVE")]
    [InlineData("PERSON", "ACTIVE")]
    [InlineData("TRAINING", "PLANNED")]
    [InlineData("PPE", "AVAILABLE")]
    [InlineData("INCIDENT", "OPEN")]
    [InlineData("CORRECTIVE_ACTION", "OPEN")]
    [InlineData("TRANSPORT", "SCHEDULED")]
    public void ReturnsCoherentInitialStatus(string kind, string expectedInitial)
    {
        Assert.Equal(expectedInitial, RuralHrRules.InitialStatus(kind));
    }

    [Theory]
    [InlineData("INCIDENT", "ACTIVE", "INVESTIGATING")]
    [InlineData("PPE", "ACTIVE", "DELIVERED")]
    [InlineData("CORRECTIVE_ACTION", "ACTIVE", "IN_PROGRESS")]
    public void AllowsLegacyActiveStatusTransitionsSafely(string kind, string currentStatus, string targetStatus)
    {
        // Must not throw due to backward compatibility for legacy ACTIVE records
        RuralHrRules.ValidateStatusTransition(kind, currentStatus, targetStatus);
    }
}
