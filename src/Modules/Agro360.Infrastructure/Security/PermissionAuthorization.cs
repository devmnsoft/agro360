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

        var baseAccess = await connection.QuerySingleAsync<AccessState>(
            """
            select exists(
                       select 1 from agro360.identity_users u
                       join agro360.identity_user_roles ur on ur.tenant_id=u.tenant_id and ur.user_id=u.id
                       join agro360.identity_role_permissions rp on rp.tenant_id=ur.tenant_id and rp.role_id=ur.role_id
                       join agro360.identity_permissions p on p.id=rp.permission_id
                       where u.tenant_id=@TenantId and u.id=@UserId and u.status='ACTIVE' and u.deleted_at is null and p.code=@Permission
                   ) HasPermission,
                   exists(select 1 from agro360.tenancy_tenants where id=@TenantId and status in(1,2) and deleted_at is null) TenantAllowed
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

        var acceptedModules = AccessDecision.AcceptedModules(requirement.Permission);
        if (acceptedModules.Length > 0)
        {
            var effectiveModules = await AccessDecision.EffectiveModulesAsync(
                connection,
                transaction,
                tenantId,
                CancellationToken.None).ConfigureAwait(false);
            if (acceptedModules.Any(effectiveModules.Contains)) context.Succeed(requirement);
        }
        await transaction.CommitAsync().ConfigureAwait(false);
    }

    private sealed class AccessState { public bool HasPermission { get; init; } public bool TenantAllowed { get; init; } }
}
