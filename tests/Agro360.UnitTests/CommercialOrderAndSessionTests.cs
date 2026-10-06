using Agro360.Application;
using Agro360.Application.Contracts;
using Agro360.Domain.Commercial;
using Agro360.Infrastructure.Security;
using Agro360.Infrastructure.Services;
using Agro360.SharedKernel;
using Xunit;

namespace Agro360.UnitTests;

public sealed class CommercialOrderAndSessionTests
{
    [Fact]
    public async Task ReviseProposalRequiresPositiveExpectedVersion()
    {
        var service = new Commercial360Service(null!, null!);

        var nullVersionInput = new SalesProposalCommand(
            CustomerId: Guid.NewGuid(),
            OpportunityId: null,
            RepresentativeId: null,
            Currency: "BRL",
            ValidUntil: DateOnly.FromDateTime(DateTime.Today.AddDays(7)),
            Freight: 0m,
            PaymentTerms: "À vista",
            Items: [new ProposalItemCommand(Guid.NewGuid(), "SACAS", 10m, 100m, 0m)],
            ChangeReason: null,
            ExpectedVersion: null);

        var exNull = await Assert.ThrowsAsync<DomainException>(() =>
            service.ReviseProposalAsync(Guid.NewGuid(), nullVersionInput, CancellationToken.None));
        Assert.Equal("sales.proposal_expected_version_required", exNull.Code);

        var zeroVersionInput = nullVersionInput with { ExpectedVersion = 0 };
        var exZero = await Assert.ThrowsAsync<DomainException>(() =>
            service.ReviseProposalAsync(Guid.NewGuid(), zeroVersionInput, CancellationToken.None));
        Assert.Equal("sales.proposal_expected_version_required", exZero.Code);

        var negativeVersionInput = nullVersionInput with { ExpectedVersion = -2 };
        var exNeg = await Assert.ThrowsAsync<DomainException>(() =>
            service.ReviseProposalAsync(Guid.NewGuid(), negativeVersionInput, CancellationToken.None));
        Assert.Equal("sales.proposal_expected_version_required", exNeg.Code);
    }

    [Theory]
    [InlineData("RESERVED")]
    [InlineData("FULFILLMENT")]
    [InlineData("INVOICED")]
    [InlineData("DELIVERED")]
    [InlineData("RETURNED")]
    public async Task ChangeOrderStatusDisallowsOperationalStatuses(string operationalStatus)
    {
        var service = new Commercial360Service(null!, null!);
        var input = new StatusCommand(operationalStatus, "Tentativa de alteração operacional");

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            service.ChangeOrderStatusAsync(Guid.NewGuid(), input, false, CancellationToken.None));

        Assert.Equal("sales.order_operational_status_disallowed", ex.Code);
    }

    [Theory]
    [InlineData("CANCELLED", null)]
    [InlineData("CANCELLED", "")]
    [InlineData("CANCELLED", "   ")]
    public void OrderTransitionCancellingRequiresReason(string next, string? reason)
    {
        var ex = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateOrderTransition("UNDER_REVIEW", next, reason));
        Assert.Equal("sales.order_cancel_reason_required", ex.Code);

        // Does not throw when reason provided
        CommercialRules.ValidateOrderTransition("UNDER_REVIEW", next, "Cliente solicitou cancelamento");
    }

    [Fact]
    public async Task ChangeOrderStatusRejectsInvalidStatus()
    {
        var service = new Commercial360Service(null!, null!);
        var input = new StatusCommand("UNKNOWN_STATUS", "Motivo qualquer");

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            service.ChangeOrderStatusAsync(Guid.NewGuid(), input, false, CancellationToken.None));

        Assert.Equal("sales.order_status_invalid", ex.Code);
    }

    [Theory]
    [InlineData("after-sales.read")]
    [InlineData("after-sales.manage")]
    [InlineData("after-sales")]
    public void AcceptedModulesMapsAfterSalesToLogistics(string permission)
    {
        var modules = PermissionAuthorizationHandler.AcceptedModules(permission);
        Assert.Contains("logistics", modules);
        Assert.DoesNotContain("platform", modules);
    }

    [Theory]
    [InlineData("storage.view", "inventory")]
    [InlineData("dashboard.read", "reports")]
    [InlineData("export.csv", "export")]
    [InlineData("purchasing.manage", "purchasing")]
    public void ModulesForPermissionMapsGroupToCatalogModules(string permission, string expectedModule)
    {
        var modules = Permissions.ModulesForPermission(permission);
        Assert.Contains(expectedModule, modules);
    }

    [Fact]
    public void ModulesForPermissionReturnsEmptyForUnknownGroup()
    {
        Assert.Empty(Permissions.ModulesForPermission("unknown.thing"));
    }

    [Fact]
    public async Task EndActiveSupportSessionRequiresSessionId()
    {
        var service = new SaasService(null!, null!, null!, null!, null!);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.EndActiveSupportSessionAsync(Guid.NewGuid(), null, CancellationToken.None));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.EndActiveSupportSessionAsync(Guid.NewGuid(), Guid.Empty, CancellationToken.None));
    }

    [Theory]
    [InlineData("DRAFT", "UNDER_REVIEW")]
    [InlineData("UNDER_REVIEW", "APPROVED")]
    [InlineData("APPROVED", "RESERVED")]
    [InlineData("RESERVED", "FULFILLMENT")]
    [InlineData("FULFILLMENT", "DELIVERED")]
    [InlineData("INVOICED", "RETURNED")]
    public void OrderTransitionAcceptsValidPaths(string current, string next)
    {
        CommercialRules.ValidateOrderTransition(current, next, null);
    }

    [Theory]
    [InlineData("DELIVERED", "DRAFT")]
    [InlineData("RETURNED", "APPROVED")]
    [InlineData("DRAFT", "DELIVERED")]
    public void OrderTransitionRejectsInvalidJumps(string current, string next)
    {
        var ex = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateOrderTransition(current, next, "motivo"));
        Assert.Equal("sales.order_transition_invalid", ex.Code);
    }
}
