using Agro360.Domain.Commercial;
using Agro360.SharedKernel;
using Xunit;

namespace Agro360.UnitTests;

public sealed class DeliveryScheduleRulesTests
{
    [Theory]
    [InlineData("APPROVED")]
    [InlineData("RESERVED")]
    [InlineData("FULFILLMENT")]
    [InlineData("approved")]
    [InlineData("reserved")]
    [InlineData("fulfillment")]
    public void EnsureOrderCanBeScheduledAllowsApprovedReservedAndFulfillment(string status)
    {
        // Should not throw
        CommercialRules.EnsureOrderCanBeScheduled(status);
    }

    [Fact]
    public void EnsureOrderCanBeScheduledRejectsCancelledOrder()
    {
        var ex = Assert.Throws<DomainException>(() => CommercialRules.EnsureOrderCanBeScheduled("CANCELLED"));
        Assert.Equal("sales.schedule_order_cancelled", ex.Code);
    }

    [Theory]
    [InlineData("DRAFT")]
    [InlineData("DELIVERED")]
    [InlineData("BLOCKED")]
    [InlineData("COMPLETED")]
    public void EnsureOrderCanBeScheduledRejectsIneligibleStatuses(string status)
    {
        var ex = Assert.Throws<DomainException>(() => CommercialRules.EnsureOrderCanBeScheduled(status));
        Assert.Equal("sales.schedule_order_ineligible", ex.Code);
    }

    [Fact]
    public void CalculateEligibleScheduleBalanceCalculatesCorrectly()
    {
        // Ordered 100, 0 cancelled, 0 active scheduled, 0 unlinked dispatches -> 100 eligible
        var bal1 = CommercialRules.CalculateEligibleScheduleBalance(100m, 0m, 0m, 0m);
        Assert.Equal(100m, bal1);

        // Ordered 100, 20 cancelled, 30 scheduled, 10 unlinked -> 100 - 20 - 30 - 10 = 40 eligible
        var bal2 = CommercialRules.CalculateEligibleScheduleBalance(100m, 20m, 30m, 10m);
        Assert.Equal(40m, bal2);

        // Over-cancelled or over-scheduled clamps to 0
        var bal3 = CommercialRules.CalculateEligibleScheduleBalance(100m, 60m, 50m, 0m);
        Assert.Equal(0m, bal3);

        // Negative or zero ordered returns 0
        var bal4 = CommercialRules.CalculateEligibleScheduleBalance(0m, 0m, 0m, 0m);
        Assert.Equal(0m, bal4);
    }

    [Fact]
    public void NormalizeScheduleStatusValidatesWhitelistedStatuses()
    {
        Assert.Equal("PLANNED", CommercialRules.NormalizeScheduleStatus("planned"));
        Assert.Equal("PREPARING", CommercialRules.NormalizeScheduleStatus("PREPARING"));
        Assert.Equal("DISPATCHED", CommercialRules.NormalizeScheduleStatus("dispatched "));

        var ex = Assert.Throws<DomainException>(() => CommercialRules.NormalizeScheduleStatus("UNKNOWN_STATUS"));
        Assert.Equal("sales.schedule_status_invalid", ex.Code);
    }

    [Fact]
    public void ValidateDeliveryScheduleRequiresValidDestination()
    {
        var items = new List<(Guid OrderItemId, decimal Quantity, decimal EligibleBalance, string Unit)>
        {
            (Guid.NewGuid(), 10m, 20m, "SACAS")
        };

        var exEmpty = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateDeliverySchedule("", items));
        Assert.Equal("sales.schedule_destination_required", exEmpty.Code);

        var exWhitespace = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateDeliverySchedule("   ", items));
        Assert.Equal("sales.schedule_destination_required", exWhitespace.Code);
    }

    [Fact]
    public void ValidateDeliveryScheduleRequiresItemsAndPositiveQuantity()
    {
        var exNoItems = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateDeliverySchedule("Fazenda Boa Vista", []));
        Assert.Equal("sales.schedule_items_required", exNoItems.Code);

        var itemsZero = new List<(Guid OrderItemId, decimal Quantity, decimal EligibleBalance, string Unit)>
        {
            (Guid.NewGuid(), 0m, 20m, "KG")
        };
        var exZero = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateDeliverySchedule("Fazenda Boa Vista", itemsZero));
        Assert.Equal("sales.schedule_quantity_invalid", exZero.Code);
    }

    [Fact]
    public void ValidateDeliveryScheduleRequiresUnit()
    {
        var itemsNoUnit = new List<(Guid OrderItemId, decimal Quantity, decimal EligibleBalance, string Unit)>
        {
            (Guid.NewGuid(), 10m, 20m, "")
        };
        var ex = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateDeliverySchedule("Fazenda Boa Vista", itemsNoUnit));
        Assert.Equal("sales.schedule_unit_required", ex.Code);
    }

    [Fact]
    public void ValidateDeliveryScheduleRejectsQuantityExceedingEligibleBalance()
    {
        var items = new List<(Guid OrderItemId, decimal Quantity, decimal EligibleBalance, string Unit)>
        {
            (Guid.NewGuid(), 25m, 20m, "SACAS") // 25 > 20 eligible
        };

        var ex = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateDeliverySchedule("Fazenda Boa Vista", items));
        Assert.Equal("sales.schedule_balance_exceeded", ex.Code);
    }

    [Fact]
    public void ValidateDeliveryScheduleAllowsValidSchedule()
    {
        var items = new List<(Guid OrderItemId, decimal Quantity, decimal EligibleBalance, string Unit)>
        {
            (Guid.NewGuid(), 20m, 20m, "SACAS"),
            (Guid.NewGuid(), 15m, 30m, "KG")
        };

        // Should not throw
        CommercialRules.ValidateDeliverySchedule("Fazenda São José, Gleba B", items);
    }

    [Theory]
    [InlineData("PLANNED", "PREPARING", null)]
    [InlineData("PLANNED", "CANCELLED", "Cliente solicitou cancelamento")]
    [InlineData("PREPARING", "DISPATCHED", null)]
    [InlineData("PREPARING", "CANCELLED", "Veículo indisponível")]
    [InlineData("DISPATCHED", "PARTIALLY_DELIVERED", null)]
    [InlineData("DISPATCHED", "DELIVERED", null)]
    [InlineData("PARTIALLY_DELIVERED", "DELIVERED", null)]
    public void ValidateScheduleTransitionAllowsValidTransitions(string current, string next, string? reason)
    {
        // Should not throw
        CommercialRules.ValidateScheduleTransition(current, next, reason);
    }

    [Fact]
    public void ValidateScheduleTransitionRequiresReasonOnCancel()
    {
        var ex = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateScheduleTransition("PLANNED", "CANCELLED", ""));
        Assert.Equal("sales.schedule_cancel_reason_required", ex.Code);
    }

    [Theory]
    [InlineData("DELIVERED", "PLANNED")]
    [InlineData("CANCELLED", "PLANNED")]
    [InlineData("DISPATCHED", "PLANNED")]
    [InlineData("DISPATCHED", "PREPARING")]
    public void ValidateScheduleTransitionRejectsInvalidTransitions(string current, string next)
    {
        var ex = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateScheduleTransition(current, next, "Motivo qualquer"));
        Assert.Equal("sales.schedule_transition_invalid", ex.Code);
    }

    [Fact]
    public void ValidateRescheduleRequiresReason()
    {
        var ex = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateReschedule("PLANNED", 0m, 0m, 10m, 10m, 20m, ""));
        Assert.Equal("sales.reschedule_reason_required", ex.Code);
    }

    [Fact]
    public void ValidateRescheduleRequiresPositiveNewQuantity()
    {
        var ex = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateReschedule("PLANNED", 0m, 0m, 10m, 0m, 20m, "Alteração de data"));
        Assert.Equal("sales.reschedule_quantity_invalid", ex.Code);
    }

    [Fact]
    public void ValidateReschedulePreventsQuantityBelowDispatched()
    {
        // Dispatched 15, trying to reduce newQuantity to 10
        var ex = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateReschedule("PREPARING", 15m, 0m, 20m, 10m, 10m, "Tentativa de redução abaixo da saída física"));
        Assert.Equal("sales.reschedule_below_dispatched", ex.Code);
    }

    [Fact]
    public void ValidateReschedulePreventsIncreaseExceedingRemainingEligibleBalance()
    {
        // Current 10, wants 25 (delta +15), but available eligible is only 10
        var ex = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateReschedule("PLANNED", 0m, 0m, 10m, 25m, 10m, "Aumento de volume"));
        Assert.Equal("sales.reschedule_balance_exceeded", ex.Code);
    }

    [Fact]
    public void ValidateRescheduleRejectsTerminalStatuses()
    {
        var exDelivered = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateReschedule("DELIVERED", 10m, 10m, 10m, 10m, 0m, "Tentativa em entregue"));
        Assert.Equal("sales.reschedule_status_invalid", exDelivered.Code);

        var exCancelled = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateReschedule("CANCELLED", 0m, 0m, 10m, 10m, 0m, "Tentativa em cancelada"));
        Assert.Equal("sales.reschedule_status_invalid", exCancelled.Code);
    }
}
