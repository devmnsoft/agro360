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

        // Sobrecomprometimento permanece visível: 100 - 60 cancelados - 50 programados = -10.
        var bal3 = CommercialRules.CalculateEligibleScheduleBalance(100m, 60m, 50m, 0m);
        Assert.Equal(-10m, bal3);

        var bal4 = CommercialRules.CalculateEligibleScheduleBalance(0m, 0m, 0m, 0m);
        Assert.Equal(0m, bal4);
    }

    [Fact]
    public void ScheduleCapacityAllowsFiftyAndRejectsSixtyWhenOrderIs100CurrentIs30AndOthersAre50()
    {
        var capacity = CommercialRules.CalculateEligibleScheduleBalance(100m, 0m, 50m, 0m);
        Assert.Equal(50m, capacity);
        Assert.Equal(20m, CommercialRules.AdditionalScheduleQuantity(capacity, 30m));

        CommercialRules.ValidateReschedule("PLANNED", 0m, 0m, 30m, 50m, capacity, "Ajuste dentro da capacidade");
        var ex = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateReschedule("PLANNED", 0m, 0m, 30m, 60m, capacity, "Ajuste acima da capacidade"));
        Assert.Equal("sales.reschedule_balance_exceeded", ex.Code);
    }

    [Fact]
    public void EnsureExpectedVersionRejectsNonPositive()
    {
        var ex = Assert.Throws<DomainException>(() => CommercialRules.EnsureExpectedVersion(0));
        Assert.Equal("sales.schedule_version_invalid", ex.Code);
    }

    [Fact]
    public void ValidateDeliveryScheduleRejectsDuplicateOrderItems()
    {
        var itemId = Guid.NewGuid();
        var items = new List<(Guid OrderItemId, decimal Quantity, decimal EligibleBalance, string Unit)>
        {
            (itemId, 10m, 40m, "KG"),
            (itemId, 5m, 40m, "KG")
        };
        var ex = Assert.Throws<DomainException>(() => CommercialRules.ValidateDeliverySchedule("Fazenda Boa Vista", items));
        Assert.Equal("sales.schedule_item_duplicated", ex.Code);
    }

    [Fact]
    public void ValidateRescheduleRejectsReductionBelowOpenPreparation()
    {
        var ex = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateReschedule("PREPARING", 0m, 0m, 20m, 10m, 50m, "Reduzir abaixo da separação", 15m));
        Assert.Equal("sales.reschedule_below_dispatched", ex.Code);
    }

    [Fact]
    public void ValidateRescheduleRejectsReductionBelowDispatchedPlusOpenPreparationScenarioFiftyTwentyFifteen()
    {
        // Cenário obrigatório: programação 50; expedido 20; preparação aberta 15.
        // Mínimo permitido = 20 + 15 = 35.
        // Redução para 25 deve ser negada com sales.reschedule_below_dispatched.
        var ex25 = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateReschedule("PREPARING", 20m, 0m, 50m, 25m, 50m, "Tentativa de redução para 25", 15m));
        Assert.Equal("sales.reschedule_below_dispatched", ex25.Code);

        // Redução para 30 também é negada (30 < 35)
        var ex30 = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateReschedule("PREPARING", 20m, 0m, 50m, 30m, 50m, "Tentativa de redução para 30", 15m));
        Assert.Equal("sales.reschedule_below_dispatched", ex30.Code);

        // Redução para 35 é permitida (35 >= 35)
        CommercialRules.ValidateReschedule("PREPARING", 20m, 0m, 50m, 35m, 50m, "Redução válida para 35", 15m);

        // Redução para 40 é permitida (40 >= 35)
        CommercialRules.ValidateReschedule("PREPARING", 20m, 0m, 50m, 40m, 50m, "Redução válida para 40", 15m);

        // Reserva aberta de 15 sem separação física ainda iniciada (dispatched 0, openPreparation 15, current 50):
        // Redução para 10 é negada (10 < 15)
        var exOpenRes = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateReschedule("PREPARING", 0m, 0m, 50m, 10m, 50m, "Tentativa abaixo de reserva aberta", 15m));
        Assert.Equal("sales.reschedule_below_dispatched", exOpenRes.Code);
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
        // Capacidade desta programação = 20 (quantidade atual 10 + incremento 10). 25 excede a capacidade.
        var ex = Assert.Throws<DomainException>(() =>
            CommercialRules.ValidateReschedule("PLANNED", 0m, 0m, 10m, 25m, 20m, "Aumento de volume"));
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
