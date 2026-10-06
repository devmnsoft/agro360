using System.Security.Claims;
using Agro360.Application;
using Agro360.Application.Abstractions;
using Agro360.SharedKernel;
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
        // Sem claims de identidade o requerimento falha silenciosamente (desafio 401).
        // Com identidade válida, toda negativa definitiva vira ForbiddenException (403 canônico).
        if (!Guid.TryParse(context.User.FindFirstValue("sub"), out var userId)
            || !Guid.TryParse(context.User.FindFirstValue("tenant_id"), out var tenantId)) return;
        if (!context.User.HasClaim("permission", requirement.Permission))
            throw new ForbiddenException(requirement.Permission == Permissions.PlatformAdmin
                ? "Esta operação exige super administração da plataforma MNSOFT."
                : "Seu perfil não possui permissão para executar esta operação.");

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
            if (!authorized)
            {
                await transaction.CommitAsync().ConfigureAwait(false);
                throw new ForbiddenException("Esta operação exige super administração da plataforma MNSOFT.");
            }
            context.Succeed(requirement);
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
            if (!portalAuthorized)
            {
                await transaction.CommitAsync().ConfigureAwait(false);
                throw new ForbiddenException("Seu acesso de portal está inativo ou incompatível com esta operação.");
            }
            context.Succeed(requirement);
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
                throw new ForbiddenException("Sua sessão de suporte não está mais ativa.");
            }

            await connection.ExecuteAsync("select set_config('app.platform_context', 'true', true);", transaction: transaction).ConfigureAwait(false);

            var supportAccess = await connection.QuerySingleAsync<SupportAccessState>(
                $"""
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
                       exists(select 1 from ({EntitlementQueries.ModuleCodeSelect}) effective_modules) ContractAllowed,
                       coalesce((select s.scope from agro360.saas_support_sessions s where s.id=@SessionId and s.tenant_id=@TenantId and s.actor_id=@UserId and s.ended_at is null), '') Scope
                """, new { TenantId = tenantId, UserId = userId, SessionId = sessionId }, transaction).ConfigureAwait(false);

            if (!supportAccess.IsSuperAdmin || !supportAccess.ActiveSupportSession)
            {
                await transaction.CommitAsync().ConfigureAwait(false);
                throw new ForbiddenException("Sua sessão de suporte não está mais ativa.");
            }

            if (!supportAccess.TenantAllowed)
            {
                await transaction.CommitAsync().ConfigureAwait(false);
                throw new ForbiddenException("Esta organização está em estado incompatível para a operação.");
            }

            // Scope check: if scope is read-only, mutations must be denied
            var isMutation = !Permissions.IsReadOnlyPermission(requirement.Permission);
            if (isMutation && !string.Equals(supportAccess.Scope, "SUPPORT_OPERATIONAL", StringComparison.OrdinalIgnoreCase))
            {
                await transaction.CommitAsync().ConfigureAwait(false);
                throw new ForbiddenException("Esta sessão de suporte permite apenas leitura.");
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

            var supportContracted = false;
            if (supportAccess.ContractAllowed)
            {
                supportContracted = await connection.ExecuteScalarAsync<bool>(
                    $"""
                    select exists(
                        select 1 from ({EntitlementQueries.ModuleCodeSelect}) effective_modules where module_code=any(@Modules))
                    """, new { TenantId = tenantId, Modules = acceptedSupportModules.Select(module => module.ToLowerInvariant()).ToArray() }, transaction).ConfigureAwait(false);
            }
            if (!supportContracted)
            {
                await transaction.CommitAsync().ConfigureAwait(false);
                throw new ForbiddenException("O módulo necessário para esta operação não está contratado pela sua organização.");
            }
            context.Succeed(requirement);
            await transaction.CommitAsync().ConfigureAwait(false);
            return;
        }

        var baseAccess = await connection.QuerySingleAsync<AccessState>(
            $"""
            select exists(
                       select 1 from agro360.identity_users u
                       join agro360.identity_user_roles ur on ur.tenant_id=u.tenant_id and ur.user_id=u.id
                       join agro360.identity_role_permissions rp on rp.tenant_id=ur.tenant_id and rp.role_id=ur.role_id
                       join agro360.identity_permissions p on p.id=rp.permission_id
                       where u.tenant_id=@TenantId and u.id=@UserId and u.status='ACTIVE' and u.deleted_at is null and p.code=@Permission
                   ) HasPermission,
                   exists(select 1 from agro360.tenancy_tenants where id=@TenantId and status in(1,2) and deleted_at is null) TenantAllowed,
                   exists(select 1 from ({EntitlementQueries.ModuleCodeSelect}) effective_modules) ContractAllowed
            """, new { TenantId = tenantId, UserId = userId, requirement.Permission }, transaction).ConfigureAwait(false);
        if (!baseAccess.HasPermission)
        {
            await transaction.CommitAsync().ConfigureAwait(false);
            throw new ForbiddenException("Seu perfil não possui permissão para executar esta operação.");
        }

        if (!baseAccess.TenantAllowed)
        {
            await transaction.CommitAsync().ConfigureAwait(false);
            throw new ForbiddenException("Esta organização está em estado incompatível para a operação.");
        }

        if (requirement.Permission.StartsWith("account.", StringComparison.OrdinalIgnoreCase))
        {
            context.Succeed(requirement);
            await transaction.CommitAsync().ConfigureAwait(false);
            return;
        }

        var acceptedModules = AcceptedModules(requirement.Permission);
        if (!baseAccess.ContractAllowed || acceptedModules.Length == 0)
        {
            await transaction.CommitAsync().ConfigureAwait(false);
            throw new ForbiddenException("O módulo necessário para esta operação não está contratado pela sua organização.");
        }

        var contracted = await connection.ExecuteScalarAsync<bool>(
            $"""
            select exists(
                select 1 from ({EntitlementQueries.ModuleCodeSelect}) effective_modules where module_code=any(@Modules))
            """, new { TenantId = tenantId, Modules = acceptedModules.Select(module => module.ToLowerInvariant()).ToArray() }, transaction).ConfigureAwait(false);
        if (!contracted)
        {
            await transaction.CommitAsync().ConfigureAwait(false);
            throw new ForbiddenException("O módulo necessário para esta operação não está contratado pela sua organização.");
        }
        context.Succeed(requirement);
        await transaction.CommitAsync().ConfigureAwait(false);
    }

    public static string[] AcceptedModules(string permission) => Permissions.ModulesForPermission(permission);

    private sealed class AccessState { public bool HasPermission { get; init; } public bool TenantAllowed { get; init; } public bool ContractAllowed { get; init; } }
    private sealed class SupportAccessState { public bool IsSuperAdmin { get; init; } public bool ActiveSupportSession { get; init; } public bool TenantAllowed { get; init; } public bool ContractAllowed { get; init; } public string Scope { get; init; } = ""; }
}
