using Agro360.Domain.Commercial;
using Agro360.SharedKernel;

namespace Agro360.UnitTests;

public sealed class CommercialProposalRulesTests
{
    [Fact]
    public void ProposalUsesExplicitHalfAwayFromZeroRounding()
        => Assert.Equal(0.02m, CommercialRules.ProposalLineTotal(1m, 0.015m, 0m));

    [Theory]
    [InlineData(0, 10, 0)] [InlineData(1, 0, 0)] [InlineData(1, 10, -1)] [InlineData(1, 10, 101)]
    public void ProposalRejectsInvalidCommercialValues(decimal quantity, decimal price, decimal discount)
        => Assert.Equal("sales.proposal_item_invalid", Assert.Throws<DomainException>(() => CommercialRules.ProposalLineTotal(quantity, price, discount)).Code);

    [Fact]
    public void RejectionAndCancellationRequireReason()
    {
        Assert.Equal("sales.proposal_reason_required", Assert.Throws<DomainException>(() => CommercialRules.ValidateProposalTransition("SUBMITTED", "REJECTED", null)).Code);
        Assert.Equal("sales.proposal_reason_required", Assert.Throws<DomainException>(() => CommercialRules.ValidateProposalTransition("DRAFT", "CANCELLED", " ")).Code);
    }

    [Fact]
    public void ExpiredProposalCannotBeAccepted()
        => Assert.Equal("sales.proposal_expired", Assert.Throws<DomainException>(() => CommercialRules.EnsureProposalAcceptable("APPROVED", new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 2))).Code);

    [Fact]
    public void OnlyApprovedProposalCanBeAccepted()
        => Assert.Equal("sales.proposal_not_approved", Assert.Throws<DomainException>(() => CommercialRules.EnsureProposalAcceptable("SUBMITTED", new DateOnly(2026, 1, 2), new DateOnly(2026, 1, 1))).Code);
}
