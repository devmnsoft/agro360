namespace Agro360.ArchitectureTests;

public sealed class AgricultureFormTests
{
    [Fact]
    public void AgriculturePageNeverExposesATypedTechnicalIdentifier()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var html = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/Pages/Agriculture/Index.cshtml"));
        Assert.DoesNotContain("type=\"text\" name=\"propertyId\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("type=\"text\" name=\"fieldId\"", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("data-lookup=\"properties\"", html);
        Assert.Contains("data-lookup=\"fields\"", html);
    }

    [Fact]
    public void FullSqlIsStandalone()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var sql = File.ReadAllText(Path.Combine(root, "database/agro360-postgres-full.sql"));
        Assert.DoesNotContain("\\i ", sql);
        Assert.Contains("Sprint 11 - Agricultura 360", sql);
        Assert.Contains("field_work_order_resources", sql);
        Assert.Contains("field_work_order_reviews", sql);
    }

    [Fact]
    public void FieldOrdersKeepReservationsExecutionMaterialsAndReviewDistinct()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var migration = File.ReadAllText(Path.Combine(root, "database/migrations/085_field_service_orders.sql"));
        var service = File.ReadAllText(Path.Combine(root, "src/Modules/Agro360.Infrastructure/Services/FieldOperationsService.cs"));
        var controller = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Api/Controllers/Agriculture360Controller.cs"));
        var page = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/Pages/Agriculture/Index.cshtml"));

        Assert.Contains("starts_at<@EndsAt and ends_at>@StartsAt", service);
        Assert.Contains("unique(tenant_id,idempotency_key)", migration);
        Assert.Contains("consumed_quantity+returned_quantity+lost_quantity<=delivered_quantity", migration);
        Assert.Contains("inventory_apply_stock_movement", service);
        Assert.Contains("version=@Version and status='AWAITING_REVIEW'", service);
        Assert.Contains("HttpPost(\"work-orders/{id:guid}/review\")", controller);
        Assert.Contains("Como usar", page);
        Assert.Contains("data-lookup=\"machines\"", page);
        Assert.DoesNotContain("type=\"text\" name=\"responsibleId\"", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SeasonTrackingConnectsPlanOrdersDependenciesAndRealSources()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var migration = File.ReadAllText(Path.Combine(root, "database/migrations/086_season_operational_tracking.sql"));
        var service = File.ReadAllText(Path.Combine(root, "src/Modules/Agro360.Infrastructure/Services/SeasonTrackingService.cs"));
        var controller = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Api/Controllers/Agriculture360Controller.cs"));
        var page = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/Pages/Agriculture/Index.cshtml"));

        Assert.Contains("plan_record_id", migration);
        Assert.Contains("unique(tenant_id,idempotency_key)", migration);
        Assert.Contains("check(operation_id<>predecessor_id)", migration);
        Assert.Contains("with recursive path", service);
        Assert.Contains("cost_allocations", service);
        Assert.Contains("harvest_records", service);
        Assert.Contains("HttpPost(\"operations/{operationId:guid}/orders\")", controller);
        Assert.Contains("Acompanhamento operacional", page);
        Assert.Contains("Data de referência", page);
        Assert.DoesNotContain("name=\"seasonId\"", page, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FieldMaterialConsumptionHasHistoryIdempotencyAndTraceableReversal()
    {
        var service = File.ReadAllText(Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")), "src/Modules/Agro360.Infrastructure/Services/FieldOperationsService.cs"));
        var migration = File.ReadAllText(Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")), "database/migrations/087_field_material_reversals.sql"));
        var client = File.ReadAllText(Path.Combine(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../")), "src/Hosts/Agro360.Web/wwwroot/js/agriculture360.js"));

        Assert.Contains("idempotency_payload_conflict", service);
        Assert.Contains("source_event_id=@Id and event_type='REVERSAL'", service);
        Assert.Contains("material_order_closed", service);
        Assert.Contains("for update", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ux_field_material_single_reversal", migration);
        Assert.Contains("data-consume", client);
        Assert.Contains("data-reverse", client);
        Assert.Contains("materialHistory", client);
    }

    [Fact]
    public void FieldOrderTransitionsProtectConcurrentChangesAndServerTimestamps()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var agriculture = File.ReadAllText(Path.Combine(root, "src/Modules/Agro360.Infrastructure/Services/Agriculture360Service.cs"));
        var fieldOperations = File.ReadAllText(Path.Combine(root, "src/Modules/Agro360.Infrastructure/Services/FieldOperationsService.cs"));
        var client = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/wwwroot/js/agriculture360.js"));

        Assert.Contains("agriculture.version_required", agriculture);
        Assert.Contains("and (@Version is null or version=@Version)", agriculture);
        Assert.Contains("actualStartedAt", agriculture);
        Assert.Contains("actualFinishedAt", fieldOperations);
        Assert.Contains("let payload={version}", client);
        Assert.Contains("farm_id=@PropertyId", agriculture);
        Assert.Contains("w.farm_id=(o.data->>'propertyId')::uuid", fieldOperations);
        Assert.Contains("Nenhuma opção compatível com a propriedade selecionada.", client);
    }

    [Fact]
    public void PropertiesFlowConnectsPagePoliciesServiceAndDatabaseGuards()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var page = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/Pages/Properties/Index.cshtml"));
        var layout = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/Pages/Shared/_Layout.cshtml"));
        var controller = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Api/Controllers/PropertiesController.cs"));
        var service = File.ReadAllText(Path.Combine(root, "src/Modules/Agro360.Infrastructure/Services/PropertyService.cs"));
        var sql = File.ReadAllText(Path.Combine(root, "database/agro360-postgres-full.sql"));

        Assert.Contains("href=\"/Properties\" data-permissions=\"properties.read\"", layout);
        Assert.Contains("@page \"/properties\"", page);
        Assert.Contains("Como funciona esta tela", page);
        Assert.Contains("<select name=\"organizationId\"", page);
        Assert.Contains("farm-pagination", page);
        Assert.DoesNotContain("type=\"text\" name=\"organizationId\"", page, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("type=\"text\" name=\"farmId\"", page, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Authorize(Policy = Permissions.PropertiesWrite)", controller);
        Assert.Contains("HttpPut(\"properties/{id:guid}\")", controller);
        Assert.Contains("GetFarmAsync", controller);
        Assert.Contains("GetFieldAsync", controller);
        Assert.Contains("HttpDelete(\"fields/{id:guid}\")", controller);
        Assert.Contains("tenant_id=@TenantId", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("version=@Version", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("where id=@Id and tenant_id=@TenantId and deleted_at is null", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("order by name, id", service, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("select *", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ck_geo_farms_state_format", sql);
        Assert.Contains("ck_geo_fields_boundary_type", sql);
        Assert.Contains("'5.1.1'", sql);
    }
}
