using System.Security.Claims;
using Agro360.Application;
using Agro360.Application.Abstractions;
using Dapper;
using Microsoft.AspNetCore.Authorization;

namespace Agro360.Infrastructure.Security;

public sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

public sealed class PermissionAuthorizationHandler(IDbConnectionFactory connectionFactory)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        if (!Guid.TryParse(context.User.FindFirstValue("sub"), out var userId)
            || !Guid.TryParse(context.User.FindFirstValue("tenant_id"), out var tenantId)
            || !context.User.HasClaim("permission", requirement.Permission)) return;

        await using var connection = await connectionFactory.OpenConnectionAsync().ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
        await connection.ExecuteAsync("select set_config('app.tenant_id',@TenantId,true)", new { TenantId = tenantId.ToString() }, transaction).ConfigureAwait(false);

        if (requirement.Permission == Permissions.PlatformAdmin)
        {
            var authorized = await connection.ExecuteScalarAsync<bool>(
                """
                select exists(
                    select 1 from agro360.platform_super_admins a
                    join agro360.identity_users u on u.id=a.user_id
                    join agro360.identity_user_roles ur on ur.tenant_id=u.tenant_id and ur.user_id=u.id
                    join agro360.identity_roles r on r.tenant_id=ur.tenant_id and r.id=ur.role_id
                    join agro360.identity_role_permissions rp on rp.tenant_id=r.tenant_id and rp.role_id=r.id
                    join agro360.identity_permissions p on p.id=rp.permission_id
                    where a.user_id=@UserId and a.active and a.deleted_at is null
                      and u.tenant_id=@TenantId and u.status='ACTIVE' and u.deleted_at is null
                      and r.code='SUPER_ADMIN' and p.code=@Permission)
                """, new { UserId = userId, TenantId = tenantId, requirement.Permission }, transaction).ConfigureAwait(false);
            if (authorized) context.Succeed(requirement);
            await transaction.CommitAsync().ConfigureAwait(false);
            return;
        }

        if (requirement.Permission == Permissions.PortalAccess)
        {
            var portalAuthorized = await connection.ExecuteScalarAsync<bool>(
                """
                select exists(
                    select 1 from agro360.portal_external_users u
                    join agro360.portal_profiles p on p.id=u.profile_id and p.tenant_id=u.tenant_id
                    join agro360.tenancy_tenants t on t.id=u.tenant_id
                    where u.id=@UserId and u.tenant_id=@TenantId
                      and u.status='ACTIVE' and u.deleted_at is null
                      and p.active and p.deleted_at is null
                      and t.status in (1, 2) and t.deleted_at is null
                )
                """, new { UserId = userId, TenantId = tenantId }, transaction).ConfigureAwait(false);
            if (portalAuthorized) context.Succeed(requirement);
            await transaction.CommitAsync().ConfigureAwait(false);
            return;
        }

        var isSupportSession = context.User.HasClaim("permission", "support_session") || context.User.HasClaim("role", "SUPPORT_SESSION");

        if (isSupportSession)
        {
            var sessionIdClaim = context.User.FindFirstValue("support_session_id");
            if (!Guid.TryParse(sessionIdClaim, out var sessionId))
            {
                await transaction.CommitAsync().ConfigureAwait(false);
                return;
            }

            var supportAccess = await connection.QuerySingleAsync<SupportAccessState>(
                """
                select exists(
                           select 1 from agro360.platform_super_admins a
                           join agro360.identity_users u on u.id=a.user_id
                           where a.user_id=@UserId and a.active and a.deleted_at is null
                             and u.status='ACTIVE' and u.deleted_at is null
                       ) IsSuperAdmin,
                       exists(
                           select 1 from agro360.saas_support_sessions s
                           where s.id=@SessionId and s.tenant_id=@TenantId and s.actor_id=@UserId
                             and s.started_at <= now() and s.expires_at > now() and s.ended_at is null
                       ) ActiveSupportSession,
                       exists(select 1 from agro360.tenancy_tenants where id=@TenantId and status in(1,2) and deleted_at is null) TenantAllowed,
                       exists(
                           select 1 from (
                               select 1 from agro360.saas_organizations o join agro360.saas_plans p on p.id=o.plan_id where o.tenant_id=@TenantId and o.status='ACTIVE' and p.active
                               union all
                               select 1 from agro360.platform_tenant_module_entitlements where tenant_id=@TenantId and status in('CONTRACTED','ACTIVE','TRIAL')
                               union all
                               select 1 from agro360.platform_tenant_modules where tenant_id=@TenantId and status='ACTIVE' and (trial_ends_at is null or trial_ends_at>now())
                           ) x
                       ) ContractAllowed,
                       coalesce((select s.scope from agro360.saas_support_sessions s where s.id=@SessionId and s.tenant_id=@TenantId and s.actor_id=@UserId and s.ended_at is null), '') Scope
                """, new { TenantId = tenantId, UserId = userId, SessionId = sessionId }, transaction).ConfigureAwait(false);

            if (!supportAccess.IsSuperAdmin || !supportAccess.ActiveSupportSession || !supportAccess.TenantAllowed)
            {
                await transaction.CommitAsync().ConfigureAwait(false);
                return;
            }

            // Scope check: if scope is read-only, mutations must be denied
            var isMutation = !Permissions.IsReadOnlyPermission(requirement.Permission);
            if (isMutation && !string.Equals(supportAccess.Scope, "SUPPORT_OPERATIONAL", StringComparison.OrdinalIgnoreCase))
            {
                await transaction.CommitAsync().ConfigureAwait(false);
                return;
            }

            if (requirement.Permission.StartsWith("account.", StringComparison.OrdinalIgnoreCase) || requirement.Permission == "support_session")
            {
                context.Succeed(requirement);
                await transaction.CommitAsync().ConfigureAwait(false);
                return;
            }

            var acceptedSupportModules = AcceptedModules(requirement.Permission);
            if (acceptedSupportModules.Length == 0)
            {
                context.Succeed(requirement);
                await transaction.CommitAsync().ConfigureAwait(false);
                return;
            }

            if (supportAccess.ContractAllowed)
            {
                var contracted = await connection.ExecuteScalarAsync<bool>(
                    """
                    select exists(
                        select 1 from (
                            select unnest(p.modules) code from agro360.saas_organizations o join agro360.saas_plans p on p.id=o.plan_id where o.tenant_id=@TenantId and o.status='ACTIVE' and p.active
                            union all
                            select c.code from agro360.platform_tenant_module_entitlements e join agro360.platform_module_catalog c on c.id=e.module_id where e.tenant_id=@TenantId and e.status in('CONTRACTED','ACTIVE','TRIAL')
                            union all
                            select m.code from agro360.platform_tenant_modules tm join agro360.platform_marketplace_modules m on m.id=tm.module_id where tm.tenant_id=@TenantId and tm.status='ACTIVE' and (tm.trial_ends_at is null or tm.trial_ends_at>now())
                        ) modules where lower(code)=any(@Modules))
                    """, new { TenantId = tenantId, Modules = acceptedSupportModules.Select(module => module.ToLowerInvariant()).ToArray() }, transaction).ConfigureAwait(false);
                if (contracted) context.Succeed(requirement);
            }
            await transaction.CommitAsync().ConfigureAwait(false);
            return;
        }

        var baseAccess = await connection.QuerySingleAsync<AccessState>(
            """
            select exists(
                       select 1 from agro360.identity_users u
                       join agro360.identity_user_roles ur on ur.tenant_id=u.tenant_id and ur.user_id=u.id
                       join agro360.identity_role_permissions rp on rp.tenant_id=ur.tenant_id and rp.role_id=ur.role_id
                       join agro360.identity_permissions p on p.id=rp.permission_id
                       where u.tenant_id=@TenantId and u.id=@UserId and u.status='ACTIVE' and u.deleted_at is null and p.code=@Permission
                   ) HasPermission,
                   exists(select 1 from agro360.tenancy_tenants where id=@TenantId and status in(1,2) and deleted_at is null) TenantAllowed,
                   exists(select 1 from agro360.saas_organizations o join agro360.saas_plans p on p.id=o.plan_id where o.tenant_id=@TenantId and o.status='ACTIVE' and p.active) ContractAllowed
            """, new { TenantId = tenantId, UserId = userId, requirement.Permission }, transaction).ConfigureAwait(false);
        if (!baseAccess.HasPermission || !baseAccess.TenantAllowed)
        {
            await transaction.CommitAsync().ConfigureAwait(false);
            return;
        }

        if (requirement.Permission.StartsWith("account.", StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
            await transaction.CommitAsync().ConfigureAwait(false);
            return;
        }

        var acceptedModules = AcceptedModules(requirement.Permission);
        if (baseAccess.ContractAllowed && acceptedModules.Length > 0)
        {
            var contracted = await connection.ExecuteScalarAsync<bool>(
                """
                select exists(
                    select 1 from (
                        select unnest(p.modules) code from agro360.saas_organizations o join agro360.saas_plans p on p.id=o.plan_id where o.tenant_id=@TenantId and o.status='ACTIVE' and p.active
                        union all
                        select c.code from agro360.platform_tenant_module_entitlements e join agro360.platform_module_catalog c on c.id=e.module_id where e.tenant_id=@TenantId and e.status in('CONTRACTED','ACTIVE','TRIAL')
                        union all
                        select m.code from agro360.platform_tenant_modules tm join agro360.platform_marketplace_modules m on m.id=tm.module_id where tm.tenant_id=@TenantId and tm.status='ACTIVE' and (tm.trial_ends_at is null or tm.trial_ends_at>now())
                    ) modules where lower(code)=any(@Modules))
                """, new { TenantId = tenantId, Modules = acceptedModules.Select(module => module.ToLowerInvariant()).ToArray() }, transaction).ConfigureAwait(false);
            if (contracted) context.Succeed(requirement);
        }
        await transaction.CommitAsync().ConfigureAwait(false);
    }

    public static string[] AcceptedModules(string permission)
    {
        var group = permission.Split('.', 2, StringSplitOptions.TrimEntries)[0];
        return group switch
        {
            "properties" => ["properties"], "agriculture" => ["agriculture"], "inventory" => ["inventory"], "livestock" => ["livestock"],
            "crm" or "commercial" or "commercial-saas" or "customer-success" => ["commercial"], "finance" => ["finance"], "purchasing" => ["purchasing"],
            "production" => ["agroindustry"], "fleet" or "maintenance" => ["fleet"], "dashboard" => ["reports", "analytics"],
            "storage" => ["inventory", "warehousing"], "logistics" or "regional-logistics" or "after-sales" => ["logistics"],
            "traceability" or "ledger" or "sales-network" => ["traceability"], "intelligence" => ["reports", "intelligence", "analytics", "ai", "predictive-ai"],
            "compliance" or "esg" or "sustainability" => ["environment-esg"], "maps" => ["properties", "analytics"], "cooperative" => ["cooperatives"],
            "rural-hr" or "sst" => ["verticals", "rural-hr"], "documents" or "evidences" or "dossiers" or "certificates" => ["documents"],
            "mobile" or "field-checklists" => ["mobile"], "export" or "fiscal" => [group],
            "marketplace" or "partners" or "api-keys" or "integrations" => ["platform", "marketplace"],
            "deployment" or "governance" or "lgpd" or "security" or "work" or "support" or "portal" => ["platform"],
            _ => []
        };
    }

    private sealed class AccessState { public bool HasPermission { get; init; } public bool TenantAllowed { get; init; } public bool ContractAllowed { get; init; } }
    private sealed class SupportAccessState { public bool IsSuperAdmin { get; init; } public bool ActiveSupportSession { get; init; } public bool TenantAllowed { get; init; } public bool ContractAllowed { get; init; } public string Scope { get; init; } = ""; }
}
