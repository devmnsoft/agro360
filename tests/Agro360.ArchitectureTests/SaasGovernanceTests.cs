namespace Agro360.ArchitectureTests;

public sealed class SaasGovernanceTests
{
    [Fact] public void SaasUiNeverRequestsTechnicalIds() { var root = FindRoot(); var text = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/Pages/Saas/Index.cshtml")) + File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/wwwroot/js/saas.js")); Assert.DoesNotContain("name=\"tenantId\"", text, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("name=\"planId\"", text, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("name=\"roleId\"", text, StringComparison.OrdinalIgnoreCase); }
    [Fact] public void BillingFeaturesAndAuditArePersistentAndProtected() { var root = FindRoot(); var api = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Api/Controllers/SaasControllers.cs")); var service = File.ReadAllText(Path.Combine(root, "src/Modules/Agro360.Infrastructure/Services/SaasService.cs")); var authorization = File.ReadAllText(Path.Combine(root, "src/Modules/Agro360.Infrastructure/Security/PermissionAuthorization.cs")); foreach (var route in new[] { "billing", "features/override", "audit" }) Assert.Contains(route, api); Assert.Contains("saas_billing_charge_events", service); Assert.Contains("MANUAL_OVERRIDE", service); Assert.Contains("saas_admin_audit_events", service); Assert.Contains("Authorize(Policy = Permissions.PlatformAdmin)", api); Assert.Contains("platform_super_admins", authorization); }
    [Fact] public void SqlIsStandaloneAndContainsSaasModules() { var sql = File.ReadAllText(Path.Combine(FindRoot(), "database/agro360-postgres-full.sql")); Assert.DoesNotContain("\\i ", sql); Assert.Contains("create schema if not exists agro360", sql, StringComparison.OrdinalIgnoreCase); Assert.Contains("'Essencial'", sql); Assert.Contains("agro360.audit_saas_events", sql); }
    [Fact] public void ExpectedEndpointsAreDeclared() { var api = File.ReadAllText(Path.Combine(FindRoot(), "src/Hosts/Agro360.Api/Controllers/SaasControllers.cs")); foreach (var route in new[] { "api/platform", "tenants", "plans", "usage", "dashboard", "api/account", "upgrade-requests", "api/users", "api/roles", "api/invitations", "api/security", "sessions", "devices", "api/notifications", "api/settings/organization" }) Assert.Contains(route, api); }
    [Fact] public void TenantBlockEndpointUsesBlockedStatus() { var api = File.ReadAllText(Path.Combine(FindRoot(), "src/Hosts/Agro360.Api/Controllers/SaasControllers.cs")); Assert.Contains("Block(Guid id, ReasonCommand x, CancellationToken ct) => Status(id, \"BLOCKED\"", api); }
    [Fact] public void TenantBlockPersistsTimestampAndActivationClearsReason() { var service = File.ReadAllText(Path.Combine(FindRoot(), "src/Modules/Agro360.Infrastructure/Services/SaasService.cs")); Assert.Contains("@Status in ('SUSPENDED','BLOCKED')", service); Assert.Contains("block_reason=case when @Status='ACTIVE' then null", service); }
    [Fact]
    public void UserStatusChangeIsAuthorizedAuditedAndRevokesSessions()
    {
        var root = FindRoot();
        var api = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Api/Controllers/SaasControllers.cs"));
        var service = File.ReadAllText(Path.Combine(root, "src/Modules/Agro360.Infrastructure/Services/SaasService.cs"));
        var sql = File.ReadAllText(Path.Combine(root, "database/agro360-postgres-full.sql"));
        var ui = File.ReadAllText(Path.Combine(root, "src/Hosts/Agro360.Web/wwwroot/js/saas.js"));
        Assert.Contains("Permissions.AccountUsersRead", api);
        Assert.Contains("Permissions.AccountUsersManage", api);
        Assert.Contains("identity_user_status_events", service);
        Assert.Contains("identity_refresh_tokens", service);
        Assert.Contains("'USER_STATUS_CHANGED'", service);
        Assert.Contains("identity_user_status_events", sql);
        Assert.Contains("data-user-status", ui);
    }

    [Fact]
    public void UserStatusRuleProtectsSelfAndLastAdministrator()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Agro360.Domain.Tenancy.SaasGovernanceRules.EnsureUserAccessTransition("ACTIVE", false, true, false, "Revisao de acesso"));
        Assert.Throws<InvalidOperationException>(() =>
            Agro360.Domain.Tenancy.SaasGovernanceRules.EnsureUserAccessTransition("ACTIVE", false, false, true, "Revisao de acesso"));
        Assert.Equal("DISABLED", Agro360.Domain.Tenancy.SaasGovernanceRules.EnsureUserAccessTransition("ACTIVE", false, false, false, "Revisao de acesso"));
        Assert.Equal("ACTIVE", Agro360.Domain.Tenancy.SaasGovernanceRules.EnsureUserAccessTransition("DISABLED", true, false, false, "Retorno ao quadro"));
    }
    private static string FindRoot() { var d = new DirectoryInfo(AppContext.BaseDirectory); while (d is not null && !File.Exists(Path.Combine(d.FullName, "MNSOFT.Agro360.sln"))) d = d.Parent; return d?.FullName ?? throw new DirectoryNotFoundException(); }
}
