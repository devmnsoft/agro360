namespace Agro360.ArchitectureTests;

public sealed class Sprint27PortalTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Fact]
    public void PortalIsSeparateAndHasRealActions()
    {
        var js = File.ReadAllText(Path.Combine(Root, "src/Hosts/Agro360.Web/wwwroot/js/portal.js"));
        foreach (var route in new[]
        {
            "/api/portal/access/login",
            "/api/portal/dashboard",
            "/api/portal/marketplace",
            "/api/portal/requests",
            "/api/portal/traceability/",
            "/api/portal/documents",
            "/api/portal/support/articles",
            "/api/portal/access/change-password"
        })
        {
            Assert.Contains(route, js);
        }
    }

    [Fact]
    public void FullDatabaseContainsSprintTablesAndRls()
    {
        var sql = File.ReadAllText(Path.Combine(Root, "database/agro360-postgres-full.sql"));
        foreach (var table in new[]
        {
            "portal_external_users",
            "portal_invitations",
            "portal_requests",
            "portal_marketplace_listings",
            "portal_marketplace_quote_requests",
            "portal_transporter_delivery_updates",
            "portal_external_audit_events",
            "portal_document_permissions"
        })
        {
            Assert.Contains(table, sql);
        }

        Assert.Contains("select agro360.platform_enable_tenant_rls('agro360.portal_profiles');", sql);
        Assert.Contains("select agro360.platform_enable_tenant_rls('agro360.portal_requests');", sql);
    }

    [Fact]
    public void IncrementalMigrationContainsAllPortalTablesWithRls()
    {
        var migration = File.ReadAllText(Path.Combine(Root, "database/migrations/097_portal_external_hardening.sql"));
        Assert.Contains("create table if not exists agro360.portal_profiles", migration);
        Assert.Contains("create table if not exists agro360.portal_requests", migration);
        Assert.Contains("create table if not exists agro360.portal_document_permissions", migration);
        Assert.Contains("select agro360.platform_enable_tenant_rls('agro360.portal_profiles');", migration);
        Assert.Contains("select agro360.platform_enable_tenant_rls('agro360.portal_requests');", migration);
        Assert.Contains("values('9.7.0'", migration);
    }

    [Fact]
    public void ExternalTokenHasNoInternalPermission()
    {
        var service = File.ReadAllText(Path.Combine(Root, "src/Modules/Agro360.Infrastructure/Services/PortalService.cs"));
        Assert.Contains("Permissions.PortalAccess", service);
        Assert.DoesNotContain("Permissions.Administrator", service);
        Assert.DoesNotContain("Permissions.SuperAdmin", service);
        Assert.DoesNotContain("Permissions.SystemAdmin", service);
    }

    [Fact]
    public void RelationshipsAreSelectedWithoutIdFields()
    {
        var pages = Directory.GetFiles(Path.Combine(Root, "src/Hosts/Agro360.Web/Pages/Portal"), "*.cshtml");
        foreach (var page in pages)
        {
            var html = File.ReadAllText(page);
            Assert.DoesNotContain("name=\"tenantId\"", html, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("name=\"entityId\"", html, StringComparison.OrdinalIgnoreCase);
        }
    }
}
