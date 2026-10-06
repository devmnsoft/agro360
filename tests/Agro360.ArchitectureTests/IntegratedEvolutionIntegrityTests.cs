namespace Agro360.ArchitectureTests;

public sealed class IntegratedEvolutionIntegrityTests
{
    private static string Root => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    private static string Read(string relativePath) => File.ReadAllText(Path.Combine(Root, relativePath));

    private static string MethodSlice(string source, string startMarker, string endMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        var end = source.IndexOf(endMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Marcador inicial nao encontrado: {startMarker}");
        Assert.True(end > start, $"Marcador final nao encontrado apos o inicio: {endMarker}");
        return source[start..end];
    }

    [Fact]
    public void SchedulingCreatesOnlyTheCommercialCommitment()
    {
        // Programacao != reserva != saida fisica: criar o compromisso pode ler reservas para validar saldo,
        // mas nunca escreve em reserva nem em estoque.
        var commercial = Read("src/Modules/Agro360.Infrastructure/Services/Commercial360Service.cs");
        var create = MethodSlice(commercial, "CreateDeliveryScheduleAsync(Guid orderId", "RescheduleDeliveryAsync");
        Assert.Contains("insert into agro360.sales_delivery_schedules", create, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("insert into agro360.fulfillment_reservations", create, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("update agro360.fulfillment_reservations", create, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("delete from agro360.fulfillment_reservations", create, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inventory_stock_balances", create, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inventory_stock_lots", create, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inventory_stock_movements", create, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void JourneyWritersNeverWriteFinancialOrFiscalTables()
    {
        // Sem simular NF/credito/pagamento: os writers da jornada comercial/logistica nao escrevem em finance_*/fiscal_*.
        var commercial = Read("src/Modules/Agro360.Infrastructure/Services/Commercial360Service.cs");
        var logistics = Read("src/Modules/Agro360.Infrastructure/Services/LogisticsService.cs");
        foreach (var source in new[] { commercial, logistics })
        {
            Assert.DoesNotContain("finance_", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("fiscal_", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("saas_billing", source, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void SettlementIsAnAdministrativeStepWithoutFinancialWrites()
    {
        var commercial = Read("src/Modules/Agro360.Infrastructure/Services/Commercial360Service.cs");
        var settle = MethodSlice(commercial, "SettleDeliveryScheduleAsync(Guid scheduleId", "ListDeliverySchedulesAsync(DeliveryScheduleQuery");
        Assert.Contains("'SETTLE'", settle);
        Assert.Contains("DELIVERY_SCHEDULE_SETTLED", settle);
        Assert.DoesNotContain("finance_", settle, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fiscal_", settle, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inventory_", settle, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettlementSealsRescheduleAndCancel()
    {
        var commercial = Read("src/Modules/Agro360.Infrastructure/Services/Commercial360Service.cs");
        var reschedule = MethodSlice(commercial, "RescheduleDeliveryAsync(Guid scheduleId", "CancelDeliveryScheduleAsync");
        var cancel = MethodSlice(commercial, "CancelDeliveryScheduleAsync(Guid scheduleId", "SettleDeliveryScheduleAsync");
        Assert.Contains("sales.schedule_settled_locked", reschedule);
        Assert.Contains("sales.schedule_settled_locked", cancel);
    }

    [Fact]
    public void FailedAttemptMovesShipmentToInDeliveryNotDelivered()
    {
        // Expedicao != prova de entrega: tentativa frustrada leva a expedicao a IN_DELIVERY;
        // apenas quantidade aceita avanca delivered_quantity.
        var logistics = Read("src/Modules/Agro360.Infrastructure/Services/LogisticsService.cs");
        Assert.Contains("@AttemptStatus='FAILED' and s.status in ('DISPATCHED','IN_DELIVERY') then 'IN_DELIVERY'", logistics, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("set delivered_quantity = delivered_quantity + @Accepted", logistics, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("check(status in('PREPARING','CHECKED','DISPATCHED','IN_DELIVERY','PARTIAL','RETURN_PENDING','RECONCILED','CANCELLED'))", Read("database/agro360-postgres-full.sql"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SettlementSchemaIsMigratedIncrementallyAndMirroredInFullScript()
    {
        var migration = Read("database/migrations/126_delivery_schedule_settlement.sql");
        Assert.Contains("settled_at timestamptz", migration);
        Assert.Contains("'RESCHEDULE', 'CANCEL', 'SETTLE'", migration);
        Assert.Contains("'11.16.0'", migration);

        var full = Read("database/agro360-postgres-full.sql");
        Assert.Contains("'RESCHEDULE', 'CANCEL', 'SETTLE'", full);
        Assert.Contains("'11.16.0'", full);
        Assert.Contains("ck_sales_delivery_schedules_settled_pair", full);
    }

    [Fact]
    public void SettlementCommandIsExposedOnTheCommercialEndpoint()
    {
        var controller = Read("src/Hosts/Agro360.Api/Controllers/Commercial360Controller.cs");
        Assert.Contains("HttpPost(\"schedules/{id:guid}/settle\")", controller);
        Assert.Contains("\"reschedule\" or \"cancel\" or \"settle\" => canWrite", controller);
    }
}
