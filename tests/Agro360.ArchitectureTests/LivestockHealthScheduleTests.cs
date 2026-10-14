namespace Agro360.ArchitectureTests;

public sealed class LivestockHealthScheduleTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));
    private static int Count(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    [Fact]
    public void Migration143ExtendsHealthLedgerWithProtocolAndIdempotency()
    {
        var migration = Read("database/migrations/143_livestock_health_schedule.sql");
        var full = Read("database/agro360-postgres-full.sql");

        Assert.Contains("add column if not exists protocol_id uuid", migration, StringComparison.Ordinal);
        Assert.Contains("protocol_version_label varchar(40)", migration, StringComparison.Ordinal);
        Assert.Contains("idempotency_key varchar(160)", migration, StringComparison.Ordinal);
        Assert.Contains("ux_livestock_health_events_idempotency", migration, StringComparison.Ordinal);
        Assert.Contains("'143.0.0'", migration, StringComparison.Ordinal);
        // Versao embutida no dump completo (fonte dos templates).
        Assert.Contains("values('143.0.0'", full, StringComparison.Ordinal);
    }

    [Fact]
    public void SantaClaraSeedsTwoDemoProtocolsForTheNewJourney()
    {
        var full = Read("database/agro360-postgres-full.sql");

        Assert.Contains("insert into agro360.livestock_health_protocols", full, StringComparison.Ordinal);
        Assert.Contains("'30000000-0000-4000-8000-000000000250'", full, StringComparison.Ordinal);
        Assert.Contains("'30000000-0000-4000-8000-000000000251'", full, StringComparison.Ordinal);
    }

    [Fact]
    public void HealthExecutionValidatesSubjectAndReplaysByIdempotencyKey()
    {
        var service = Read("src/Modules/Agro360.Infrastructure/Services/Livestock360Service.cs");

        Assert.Contains("agro360.livestock_health_subject_required", service, StringComparison.Ordinal);
        Assert.Contains(
            "select id from agro360.livestock_health_events where tenant_id=@TenantId and idempotency_key=@Key", service, StringComparison.Ordinal);
        Assert.Contains("protocol_id,protocol_version_label,idempotency_key", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ProtocolApplicationDerivesRestrictionInSameTransaction()
    {
        var service = Read("src/Modules/Agro360.Infrastructure/Services/Livestock360Service.cs");

        Assert.Contains("for update", service, StringComparison.Ordinal);
        Assert.Contains("livestock.protocol_not_active", service, StringComparison.Ordinal);
        Assert.Contains("livestock.protocol_window_invalid", service, StringComparison.Ordinal);
        Assert.Contains("livestock.restriction_overlap", service, StringComparison.Ordinal);
        Assert.Contains("source_event_id", service, StringComparison.Ordinal);
        Assert.Contains("insert into agro360.livestock_restrictions", service, StringComparison.Ordinal);
    }

    [Fact]
    public void ExpiryIsDerivedInEveryActiveRestrictionCheck()
    {
        var herd = Read("src/Modules/Agro360.Infrastructure/Services/LivestockHerdService.cs");
        var service = Read("src/Modules/Agro360.Infrastructure/Services/Livestock360Service.cs");

        // Sem prefixo (mesma tabela): bloqueio operacional (HasActive) + liberacao.
        Assert.True(
            Count(herd, "(expected_until is null or expected_until >= current_date)") >= 2,
            "bloqueio e liberacao de restricao devem considerar a data de vencimento.");
        // Com prefixo r.: previa de manejo + elegibilidade para venda + filtro da lista.
        Assert.True(
            Count(herd, "(r.expected_until is null or r.expected_until >= current_date)") >= 3,
            "previa, elegibilidade e lista de restricoes devem considerar a data de vencimento.");
        Assert.True(
            Count(service, "(expected_until is null or expected_until >= current_date)") >= 1,
            "KPI de restritos deve ignorar restricao vencida em data.");
    }

    [Fact]
    public void DerivedEffectiveStatusIsExposedOnRestrictionLists()
    {
        var herd = Read("src/Modules/Agro360.Infrastructure/Services/LivestockHerdService.cs");

        Assert.Equal(2, Count(herd, "end as \"effectiveStatus\""));
    }

    [Fact]
    public void AgendaAndProtocolRoutesLiveInTheLivestockController()
    {
        var controller = Read("src/Hosts/Agro360.Api/Controllers/Livestock360Controller.cs");

        Assert.Contains("api/livestock/agenda", controller, StringComparison.Ordinal);
        Assert.Contains("api/livestock/protocols", controller, StringComparison.Ordinal);
        Assert.Contains("/deactivate", controller, StringComparison.Ordinal);
        Assert.Contains("AgendaAsync(farmId, from, to, overdueOnly, ct)", controller, StringComparison.Ordinal);
    }

    [Fact]
    public void GenericCatalogSaveForProtocolsPointsToTheDedicatedRoute()
    {
        var herd = Read("src/Modules/Agro360.Infrastructure/Services/LivestockHerdService.cs");

        Assert.Contains("livestock.protocols_dedicated_route", herd, StringComparison.Ordinal);
    }
}
