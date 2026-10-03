using System.Security.Claims;
using Agro360.Application;
using Agro360.Multitenancy;
using Microsoft.AspNetCore.Authorization;

namespace Agro360.Api.Middleware;

public sealed class TenantContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, IMutableTenantContext tenantContext)
    {
        var endpoint = context.GetEndpoint();
        if (endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is not null || context.User.Identity?.IsAuthenticated != true)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        if (!Guid.TryParse(context.User.FindFirstValue("tenant_id"), out var tenantId)
            || !Guid.TryParse(context.User.FindFirstValue("sub"), out var userId))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                type = "invalid_token_context",
                title = "Token sem contexto de tenant",
                status = 401,
                traceId = context.TraceIdentifier
            }).ConfigureAwait(false);
            return;
        }

        var organizationId = ReadOptionalGuid(context, "X-Organization-ID");
        var farmId = ReadOptionalGuid(context, "X-Farm-ID");

        var connectionFactory = context.RequestServices.GetRequiredService<Agro360.Application.Abstractions.IDbConnectionFactory>();
        await using (var conn = await connectionFactory.OpenConnectionAsync(context.RequestAborted).ConfigureAwait(false))
        await using (var tx = await conn.BeginTransactionAsync(context.RequestAborted).ConfigureAwait(false))
        {
            // Define o contexto PostgreSQL do tenant estritamente local à transação (is_local = true)
            await Dapper.SqlMapper.ExecuteAsync(conn, new Dapper.CommandDefinition(
                "select set_config('app.tenant_id', @TenantId, true);",
                new { TenantId = tenantId.ToString() },
                transaction: tx,
                cancellationToken: context.RequestAborted)).ConfigureAwait(false);

            var isSupportSession = context.User.HasClaim("permission", "support_session") || context.User.HasClaim("role", "SUPPORT_SESSION");
            var isPortalUser = context.User.Claims.Any(c => c.Value.StartsWith("agro360.portal_profile.", StringComparison.OrdinalIgnoreCase));

            if (isSupportSession)
            {
                var sessionIdClaim = context.User.FindFirstValue("support_session_id");
                if (!Guid.TryParse(sessionIdClaim, out var sessionId))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        type = "invalid_support_session",
                        title = "Sessão de suporte inválida ou ausente no token",
                        status = 403,
                        traceId = context.TraceIdentifier
                    }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                    return;
                }

                var supportState = await Dapper.SqlMapper.QuerySingleOrDefaultAsync<(bool IsSuperAdmin, bool SessionValid, string Scope, bool TenantActive)>(
                    conn, new Dapper.CommandDefinition(
                        """
                        select
                            exists(
                                select 1 from agro360.platform_super_admins a
                                join agro360.identity_users u on u.id = a.user_id
                                where a.user_id = @UserId and a.active and a.deleted_at is null
                                  and u.status = 'ACTIVE' and u.deleted_at is null
                            ) as IsSuperAdmin,
                            exists(
                                select 1 from agro360.saas_support_sessions s
                                where s.id = @SessionId and s.tenant_id = @TenantId and s.actor_id = @UserId
                                  and s.started_at <= now() and s.expires_at > now() and s.ended_at is null
                            ) as SessionValid,
                            coalesce((
                                select s.scope from agro360.saas_support_sessions s
                                where s.id = @SessionId and s.tenant_id = @TenantId and s.actor_id = @UserId
                                  and s.started_at <= now() and s.expires_at > now() and s.ended_at is null
                            ), '') as Scope,
                            exists(
                                select 1 from agro360.tenancy_tenants t
                                where t.id = @TenantId and t.status in (1, 2) and t.deleted_at is null
                            ) as TenantActive
                        """,
                        new { UserId = userId, TenantId = tenantId, SessionId = sessionId },
                        transaction: tx,
                        cancellationToken: context.RequestAborted)).ConfigureAwait(false);

                if (!supportState.IsSuperAdmin || !supportState.SessionValid || !supportState.TenantActive)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        type = "forbidden_support_session",
                        title = "Sessão de suporte inválida, expirada, revogada ou operador sem permissão",
                        status = 403,
                        traceId = context.TraceIdentifier
                    }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                    return;
                }

                var isEndingSupport = context.Request.Path.StartsWithSegments("/api/platform/support-session/end", StringComparison.OrdinalIgnoreCase);
                var isMutation = HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method) || HttpMethods.IsPatch(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method);

                if (!isEndingSupport && isMutation && !string.Equals(supportState.Scope, "SUPPORT_OPERATIONAL", StringComparison.OrdinalIgnoreCase))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        type = "read_only_support_session",
                        title = "Sessão de suporte em modo somente leitura não permite mutações",
                        status = 403,
                        traceId = context.TraceIdentifier
                    }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                    return;
                }
            }
            else if (isPortalUser)
            {
                var portalUserActive = await Dapper.SqlMapper.ExecuteScalarAsync<bool>(conn, new Dapper.CommandDefinition(
                    """
                    select exists(
                        select 1 from agro360.portal_external_users u
                        join agro360.portal_profiles p on p.id = u.profile_id and p.tenant_id = u.tenant_id and p.active and p.deleted_at is null
                        join agro360.tenancy_tenants t on t.id = u.tenant_id and t.status in (1, 2) and t.deleted_at is null
                        where u.id = @UserId and u.tenant_id = @TenantId and u.status = 'ACTIVE' and u.deleted_at is null
                    )
                    """,
                    new { UserId = userId, TenantId = tenantId },
                    transaction: tx,
                    cancellationToken: context.RequestAborted)).ConfigureAwait(false);

                if (!portalUserActive)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        type = "forbidden_user_context",
                        title = "Usuário do portal inativo, revogado ou tenant suspenso",
                        status = 403,
                        traceId = context.TraceIdentifier
                    }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                    return;
                }
            }
            else
            {
                var userActive = await Dapper.SqlMapper.ExecuteScalarAsync<bool>(conn, new Dapper.CommandDefinition(
                    """
                    select exists(
                        select 1 from agro360.identity_users u
                        join agro360.tenancy_tenants t on t.id = u.tenant_id and t.status in (1, 2) and t.deleted_at is null
                        where u.id = @UserId and u.tenant_id = @TenantId and u.status = 'ACTIVE' and u.deleted_at is null
                    )
                    """,
                    new { UserId = userId, TenantId = tenantId },
                    transaction: tx,
                    cancellationToken: context.RequestAborted)).ConfigureAwait(false);

                if (!userActive)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        type = "forbidden_user_context",
                        title = "Usuário inativo, desativado ou tenant indisponível",
                        status = 403,
                        traceId = context.TraceIdentifier
                    }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                    return;
                }
            }

            if (organizationId.HasValue || farmId.HasValue)
            {
                if (organizationId.HasValue)
                {
                    var orgValid = await Dapper.SqlMapper.ExecuteScalarAsync<bool>(conn, new Dapper.CommandDefinition(
                        "select exists(select 1 from agro360.organization_organizations where tenant_id = @TenantId and id = @OrgId and deleted_at is null)",
                        new { TenantId = tenantId, OrgId = organizationId.Value },
                        transaction: tx,
                        cancellationToken: context.RequestAborted)).ConfigureAwait(false);

                    if (!orgValid)
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            type = "invalid_organization_scope",
                            title = "Organização informada não pertence ao tenant autenticado",
                            status = 403,
                            traceId = context.TraceIdentifier
                        }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                        return;
                    }
                }

                if (farmId.HasValue)
                {
                    var farmOrgId = await Dapper.SqlMapper.ExecuteScalarAsync<Guid?>(conn, new Dapper.CommandDefinition(
                        "select organization_id from agro360.geo_farms where tenant_id = @TenantId and id = @FarmId and deleted_at is null",
                        new { TenantId = tenantId, FarmId = farmId.Value },
                        transaction: tx,
                        cancellationToken: context.RequestAborted)).ConfigureAwait(false);

                    if (!farmOrgId.HasValue)
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            type = "invalid_farm_scope",
                            title = "Fazenda informada não pertence ao tenant autenticado",
                            status = 403,
                            traceId = context.TraceIdentifier
                        }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                        return;
                    }

                    if (organizationId.HasValue && farmOrgId.Value != organizationId.Value)
                    {
                        context.Response.StatusCode = StatusCodes.Status403Forbidden;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            type = "incompatible_organization_farm_scope",
                            title = "A fazenda informada não pertence à organização especificada",
                            status = 403,
                            traceId = context.TraceIdentifier
                        }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                        return;
                    }

                    if (!organizationId.HasValue)
                    {
                        organizationId = farmOrgId.Value;
                    }
                }

                if (isPortalUser)
                {
                    var hasEntityRestrictions = await Dapper.SqlMapper.ExecuteScalarAsync<bool>(conn, new Dapper.CommandDefinition(
                        "select exists(select 1 from agro360.portal_external_user_links where tenant_id = @TenantId and external_user_id = @UserId and deleted_at is null)",
                        new { TenantId = tenantId, UserId = userId },
                        transaction: tx,
                        cancellationToken: context.RequestAborted)).ConfigureAwait(false);

                    if (hasEntityRestrictions)
                    {
                        var isLinked = await Dapper.SqlMapper.ExecuteScalarAsync<bool>(conn, new Dapper.CommandDefinition(
                            """
                            select exists(
                                select 1 from agro360.portal_external_user_links
                                where tenant_id = @TenantId
                                  and external_user_id = @UserId
                                  and deleted_at is null
                                  and (
                                      (@FarmId is not null and entity_type in ('FARM', 'PROPERTY') and entity_id = @FarmId)
                                      or (@OrgId is not null and entity_type in ('ORGANIZATION', 'COMPANY', 'UNIT') and entity_id = @OrgId)
                                  )
                            )
                            """,
                            new { TenantId = tenantId, UserId = userId, FarmId = farmId, OrgId = organizationId },
                            transaction: tx,
                            cancellationToken: context.RequestAborted)).ConfigureAwait(false);

                        if (!isLinked)
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            await context.Response.WriteAsJsonAsync(new
                            {
                                type = "forbidden_entity_scope",
                                title = "Usuário do portal não tem autorização para a unidade/fazenda solicitada",
                                status = 403,
                                traceId = context.TraceIdentifier
                            }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                            return;
                        }
                    }
                }
            }

            await tx.CommitAsync(context.RequestAborted).ConfigureAwait(false);
        }

        var timeZone = context.Request.Headers["X-Timezone"].FirstOrDefault() ?? "America/Belem";
        tenantContext.SetScope(new TenantScope(tenantId, userId, organizationId, farmId, timeZone));

        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            tenantContext.Clear();
        }

    }

    private static Guid? ReadOptionalGuid(HttpContext context, string headerName)
    {
        var value = context.Request.Headers[headerName].FirstOrDefault();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Guid.TryParse(value, out var parsed))
        {
            throw new BadHttpRequestException($"O header {headerName} não possui UUID válido.");
        }

        return parsed;
    }
}
