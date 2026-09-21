using Agro360.Domain.Commercial;
using Agro360.SharedKernel;
using Xunit;

namespace Agro360.UnitTests;

public sealed class CommercialContractRulesTests
{
    [Fact]
    public void ExportContractRequiresIncoterm()
    {
        var error = Assert.Throws<DomainException>(() => CommercialRules.ValidateContract("EXPORT", 10, 20, new(2026, 1, 1), new(2026, 12, 31), "Pagamento em 30 dias", "USD", null));
        Assert.Equal("sales.contract_export_terms_required", error.Code);
    }

    [Fact]
    public void CancellationRequiresReason()
    {
        var error = Assert.Throws<DomainException>(() => CommercialRules.ValidateContractTransition("DRAFT", "CANCELLED", null));
        Assert.Equal("sales.contract_cancel_reason_required", error.Code);
    }

    [Fact]
    public void ApprovedContractCannotReturnToDraft()
    {
        var error = Assert.Throws<DomainException>(() => CommercialRules.ValidateContractTransition("APPROVED", "DRAFT", "edição"));
        Assert.Equal("sales.contract_transition_invalid", error.Code);
    }

    [Fact]
    public void ContractValidationDoesNotCreateOrderStockOrFinanceSideEffects()
    {
        var error = Record.Exception(() => CommercialRules.ValidateContract("INTERNAL_SALE", 100, 42.50m, new(2026, 1, 1), new(2026, 12, 31), "Entrega mensal", "BRL", null));
        Assert.Null(error);
    }

    [Theory]
    [InlineData("DRAFT", "UNDER_REVIEW")]
    [InlineData("ACTIVE", "PARTIALLY_FULFILLED")]
    [InlineData("PARTIALLY_FULFILLED", "FULFILLED")]
    public void ContractAcceptsValidTransitions(string current, string next)
        => CommercialRules.ValidateContractTransition(current, next, null);

    [Theory]
    [InlineData("BLOCKED")]
    [InlineData("DELINQUENT")]
    public void CustomerWithCommercialRestrictionCannotOrder(string status)
    {
        var error = Assert.Throws<DomainException>(() => CommercialRules.CustomerCanOrder(status, false));
        Assert.Equal("sales.customer_blocked", error.Code);
    }

    [Fact]
    public void AuthorizedProfileMayOverrideCustomerBlock()
        => CommercialRules.CustomerCanOrder("BLOCKED", true);

    [Fact]
    public void SpecialDiscountRequiresReasonAndApproval()
    {
        var missingReason = Assert.Throws<DomainException>(() => CommercialRules.CalculatePrice(10, 100, 12, 0, 0, 0, 0, 10, null));
        Assert.Equal("sales.special_discount_reason_required", missingReason.Code);

        var result = CommercialRules.CalculatePrice(10, 100, 12, 0, 25, 50, 0, 10, "Negociação de safra");
        Assert.True(result.RequiresApproval);
        Assert.Equal(955, result.NetTotal);
    }

    [Fact]
    public void PriceCalculationRejectsNegativeNetTotal()
    {
        var error = Assert.Throws<DomainException>(() => CommercialRules.CalculatePrice(1, 100, 100, 1, 0, 0, 0, 100, null));
        Assert.Equal("sales.negative_net_total", error.Code);
    }

    [Fact]
    public void ContractOrderRejectsInsufficientBalance()
    {
        var error = Assert.Throws<DomainException>(() => CommercialRules.ValidateContractBalance(100, 90, 11));
        Assert.Equal("sales.contract_balance_insufficient", error.Code);
    }

    [Theory]
    [InlineData("CANCELLED")]
    [InlineData("RETURNED")]
    [InlineData("DELIVERED")]
    public void CommissionRejectsIneligibleOrderStatus(string status)
    {
        var error = Assert.Throws<DomainException>(() => CommercialRules.CommissionForOrder(status, 1_000, 5));
        Assert.Equal("sales.commission_status_ineligible", error.Code);
    }

    [Fact]
    public void RegionalAmazonProductRequiresReinforcedTraceability()
    {
        var requirements = CommercialRules.ComplianceFor("Açaí congelado");
        Assert.True(requirements.ReinforcedTraceability);
        Assert.True(requirements.RequiresTrackedOrigin);
        Assert.True(requirements.RequiresPhotoOrGpsEvidence);
    }
}
