using Agro360.Domain.Mobile;

namespace Agro360.UnitTests;

public sealed class FieldMobileRulesTests
{
    [Fact]
    public void Active_checklist_requires_items() => Assert.ThrowsAny<Exception>(() =>
        FieldMobileRules.ValidateChecklist("Inspeção", [], true));

    [Fact]
    public void Checklist_item_cannot_be_empty() => Assert.ThrowsAny<Exception>(() =>
        FieldMobileRules.ValidateChecklist("Inspeção", [new(" ", true, false, false, "TEXT")], true));

    [Fact]
    public void Approved_checklist_requires_new_version() => Assert.ThrowsAny<Exception>(() =>
        FieldMobileRules.EnsureNewVersion(true, 3, 3));

    [Fact]
    public void Completion_blocks_missing_required_answer() => Assert.ThrowsAny<Exception>(() =>
        FieldMobileRules.ValidateCompletion([new("Freios", true, false, false, "YES_NO")], false, null, false, null, null));

    [Fact]
    public void Completion_blocks_missing_evidence() => Assert.ThrowsAny<Exception>(() =>
        FieldMobileRules.ValidateCompletion([new("Foto", false, true, false, "PHOTO", "OK")], false, null, false, null, null));

    [Fact]
    public void Completion_blocks_required_signature_and_location()
    {
        Assert.ThrowsAny<Exception>(() => FieldMobileRules.ValidateCompletion([], true, null, false, null, null));
        Assert.ThrowsAny<Exception>(() => FieldMobileRules.ValidateCompletion([], false, null, true, null, null));
    }

    [Fact]
    public void Critical_occurrence_requires_responsible() => Assert.ThrowsAny<Exception>(() =>
        FieldMobileRules.ValidateOccurrence(FieldPriority.Critical, null, null, null));

    [Theory]
    [InlineData("RESOLVED")]
    [InlineData("CANCELLED")]
    public void Critical_transitions_require_reason(string transition) => Assert.ThrowsAny<Exception>(() =>
        FieldMobileRules.ValidateOccurrence(FieldPriority.Low, null, transition, null));

    [Fact]
    public void Conflict_resolution_requires_audit_data() => Assert.ThrowsAny<Exception>(() =>
        FieldMobileRules.ValidateConflictResolution(null, Guid.Empty, default));

    [Fact]
    public void Signature_detects_content_change()
    {
        Assert.False(FieldMobileRules.SignatureWasAltered("ABC", "abc"));
        Assert.True(FieldMobileRules.SignatureWasAltered("ABC", "ABD"));
    }

    [Theory]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    public void Location_rejects_invalid_coordinates(decimal latitude, decimal longitude) =>
        Assert.ThrowsAny<Exception>(() => MobileRules.ValidateLocation(latitude, longitude, null));
}
