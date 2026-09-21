using Agro360.Application.Contracts;
using Agro360.Domain.Storage;
using Agro360.SharedKernel;
using Xunit;

namespace Agro360.UnitTests;

public sealed class ReturnDispositionRulesTests
{
    [Theory]
    [InlineData("RELEASE")]
    [InlineData("BLOCK")]
    [InlineData("DISPOSE")]
    [InlineData("release")]
    [InlineData("block")]
    [InlineData("dispose")]
    public void ValidateReturnDecisionAllowsValidDecisions(string decision)
    {
        var ex = Record.Exception(() => StorageRules.ValidateReturnDecision(
            decision: decision,
            quantity: 10m,
            reason: "Motivo válido de conferência",
            idempotencyKey: "KEY-12345",
            unit: "KG",
            cost: 50.0m));

        Assert.Null(ex);
    }

    [Theory]
    [InlineData("REPROCESS")]
    [InlineData("CANCEL")]
    [InlineData("ACCEPT")]
    [InlineData("UNKNOWN")]
    [InlineData("")]
    public void ValidateReturnDecisionRejectsInvalidDecisionsWithSemanticCode(string decision)
    {
        var ex = Assert.Throws<DomainException>(() => StorageRules.ValidateReturnDecision(
            decision: decision,
            quantity: 10m,
            reason: "Motivo de teste",
            idempotencyKey: "KEY-12345",
            unit: "KG",
            cost: null));

        Assert.Equal("return.invalid_decision", ex.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.0001)]
    public void ValidateReturnDecisionRejectsNonPositiveQuantityWithSemanticCode(decimal quantity)
    {
        var ex = Assert.Throws<DomainException>(() => StorageRules.ValidateReturnDecision(
            decision: "BLOCK",
            quantity: quantity,
            reason: "Motivo de teste",
            idempotencyKey: "KEY-12345",
            unit: "KG",
            cost: null));

        Assert.Equal("return.quantity_invalid", ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateReturnDecisionRejectsMissingReasonWithSemanticCode(string? reason)
    {
        var ex = Assert.Throws<DomainException>(() => StorageRules.ValidateReturnDecision(
            decision: "BLOCK",
            quantity: 5m,
            reason: reason!,
            idempotencyKey: "KEY-12345",
            unit: "KG",
            cost: null));

        Assert.Equal("return.reason_required", ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateReturnDecisionRejectsMissingIdempotencyKeyWithSemanticCode(string? key)
    {
        var ex = Assert.Throws<DomainException>(() => StorageRules.ValidateReturnDecision(
            decision: "BLOCK",
            quantity: 5m,
            reason: "Motivo de bloqueio",
            idempotencyKey: key!,
            unit: "KG",
            cost: null));

        Assert.Equal("return.idempotency_required", ex.Code);
    }

    [Fact]
    public void ValidateReturnDecisionRejectsNegativeCostForLossWithSemanticCode()
    {
        var ex = Assert.Throws<DomainException>(() => StorageRules.ValidateReturnDecision(
            decision: "DISPOSE",
            quantity: 5m,
            reason: "Descarte de material impróprio",
            idempotencyKey: "KEY-12345",
            unit: "KG",
            cost: -10m));

        Assert.Equal("return.cost_invalid", ex.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidateReturnDecisionRejectsMissingUnitWhenDisposingLossWithSemanticCode(string? unit)
    {
        var ex = Assert.Throws<DomainException>(() => StorageRules.ValidateReturnDecision(
            decision: "DISPOSE",
            quantity: 5m,
            reason: "Descarte por perda física",
            idempotencyKey: "KEY-12345",
            unit: unit,
            cost: 25.50m));

        Assert.Equal("return.unit_required", ex.Code);
    }

    [Fact]
    public void ValidateReturnQualityForReleaseRejectsWhenNoQualityRecordsExist()
    {
        var records = Array.Empty<(string? IntentStatus, string? RunStatus, string? RunResult)>();

        var ex = Assert.Throws<DomainException>(() => StorageRules.ValidateReturnQualityForRelease(records));
        Assert.Equal("return.quality_missing", ex.Code);
    }

    [Fact]
    public void ValidateReturnQualityForReleaseRejectsWhenPendingModel()
    {
        var records = new (string?, string?, string?)[]
        {
            ("PENDING_MODEL", null, null)
        };

        var ex = Assert.Throws<DomainException>(() => StorageRules.ValidateReturnQualityForRelease(records));
        Assert.Equal("return.quality_pending_model", ex.Code);
    }

    [Fact]
    public void ValidateReturnQualityForReleaseRejectsWhenAmbiguousModel()
    {
        var records = new (string?, string?, string?)[]
        {
            ("AMBIGUOUS", null, null)
        };

        var ex = Assert.Throws<DomainException>(() => StorageRules.ValidateReturnQualityForRelease(records));
        Assert.Equal("return.quality_ambiguous", ex.Code);
    }

    [Fact]
    public void ValidateReturnQualityForReleaseRejectsWhenNonConforming()
    {
        var records = new (string?, string?, string?)[]
        {
            ("STARTED", "COMPLETED", "NON_CONFORMING")
        };

        var ex = Assert.Throws<DomainException>(() => StorageRules.ValidateReturnQualityForRelease(records));
        Assert.Equal("return.quality_non_conforming", ex.Code);
    }

    [Fact]
    public void ValidateReturnQualityForReleaseRejectsWhenInconclusive()
    {
        var records = new (string?, string?, string?)[]
        {
            ("STARTED", "COMPLETED", "INCONCLUSIVE")
        };

        var ex = Assert.Throws<DomainException>(() => StorageRules.ValidateReturnQualityForRelease(records));
        Assert.Equal("return.quality_inconclusive", ex.Code);
    }

    [Theory]
    [InlineData("IN_PROGRESS", "CONFORMING")]
    [InlineData("PENDING_REVIEW", "CONFORMING")]
    [InlineData("COMPLETED", null)]
    public void ValidateReturnQualityForReleaseRejectsWhenRunNotCompletedWithConformingResult(string runStatus, string? runResult)
    {
        var records = new (string?, string?, string?)[]
        {
            ("STARTED", runStatus, runResult)
        };

        var ex = Assert.Throws<DomainException>(() => StorageRules.ValidateReturnQualityForRelease(records));
        Assert.Equal("return.quality_incomplete", ex.Code);
    }

    [Fact]
    public void ValidateReturnQualityForReleaseSucceedsWhenInspectionCompletedConforming()
    {
        var records = new (string?, string?, string?)[]
        {
            ("STARTED", "COMPLETED", "CONFORMING")
        };

        var ex = Record.Exception(() => StorageRules.ValidateReturnQualityForRelease(records));
        Assert.Null(ex);
    }

    [Fact]
    public void DecideReturnCommandSupportsOptionalUnitAndCost()
    {
        var cmd = new DecideReturnCommand(
            Decision: "DISPOSE",
            Quantity: 12.5m,
            Reason: "Perda comprovada",
            ExpectedVersion: 2,
            IdempotencyKey: "DISP-001",
            Unit: "SC",
            Cost: 150.75m);

        Assert.Equal("DISPOSE", cmd.Decision);
        Assert.Equal(12.5m, cmd.Quantity);
        Assert.Equal("Perda comprovada", cmd.Reason);
        Assert.Equal(2, cmd.ExpectedVersion);
        Assert.Equal("DISP-001", cmd.IdempotencyKey);
        Assert.Equal("SC", cmd.Unit);
        Assert.Equal(150.75m, cmd.Cost);
    }
}
