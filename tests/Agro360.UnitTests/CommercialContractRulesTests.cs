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
}
