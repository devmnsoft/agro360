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
        string culture;

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

                await Dapper.SqlMapper.ExecuteAsync(conn, new Dapper.CommandDefinition(
                    "select set_config('app.platform_context', 'true', true);",
                    transaction: tx,
                    cancellationToken: context.RequestAborted)).ConfigureAwait(false);

                var supportState = await Dapper.SqlMapper.QuerySingleOrDefaultAsync<SupportSessionStateRow>(
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

                if (supportState is null || !supportState.IsSuperAdmin || !supportState.SessionValid || !supportState.TenantActive)
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

                var supportAccessTokenId = context.User.FindFirstValue("jti");
                var supportAccessTokenActive = Guid.TryParse(supportAccessTokenId, out var supportAccessTokenJti)
                    && await Dapper.SqlMapper.ExecuteScalarAsync<bool>(conn, new Dapper.CommandDefinition(
                        """
                        select exists(
                            select 1
                            from agro360.identity_refresh_tokens rt
                            join agro360.identity_users u on u.tenant_id=rt.tenant_id and u.id=rt.user_id
                            where u.id=@UserId and rt.access_token_jti=@AccessTokenId
                              and u.status='ACTIVE' and u.deleted_at is null
                              and rt.revoked_at is null and rt.expires_at>now()
                        )
                        """,
                        new { UserId = userId, AccessTokenId = supportAccessTokenJti },
                        transaction: tx,
                        cancellationToken: context.RequestAborted)).ConfigureAwait(false);
                if (!supportAccessTokenActive)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        type = "session_revoked",
                        title = "A sessão de suporte foi revogada ou não está mais ativa",
                        status = 401,
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

                var accessTokenId = context.User.FindFirstValue("jti");
                var sessionActive = Guid.TryParse(accessTokenId, out var accessTokenJti)
                    && await Dapper.SqlMapper.ExecuteScalarAsync<bool>(conn, new Dapper.CommandDefinition(
                        """
                        select exists(
                            select 1 from agro360.identity_refresh_tokens
                            where tenant_id=@TenantId and user_id=@UserId and access_token_jti=@AccessTokenId
                              and revoked_at is null and expires_at>now()
                        )
                        """,
                        new { TenantId = tenantId, UserId = userId, AccessTokenId = accessTokenJti },
                        transaction: tx,
                        cancellationToken: context.RequestAborted)).ConfigureAwait(false);

                if (!sessionActive)
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        type = "session_revoked",
                        title = "A sessão foi revogada ou não está mais ativa",
                        status = 401,
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

            if (!isPortalUser && !isSupportSession)
            {
                var userScopes = (await Dapper.SqlMapper.QueryAsync<UserUnitScopeRow>(conn, new Dapper.CommandDefinition(
                    """
                    select scope_type as ScopeType, organization_id as OrganizationId, farm_id as FarmId
                    from agro360.identity_user_unit_scopes
                    where tenant_id = @TenantId and user_id = @UserId
                    """,
                    new { TenantId = tenantId, UserId = userId },
                    transaction: tx,
                    cancellationToken: context.RequestAborted)).ConfigureAwait(false)).ToList();

                if (userScopes.Count == 0)
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    await context.Response.WriteAsJsonAsync(new
                    {
                        type = "forbidden_unit_scope",
                        title = "Usuário não possui nenhuma unidade ou fazenda concedida",
                        status = 403,
                        traceId = context.TraceIdentifier
                    }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                    return;
                }

                var hasAllScope = userScopes.Any(s => string.Equals(s.ScopeType, "ALL", StringComparison.OrdinalIgnoreCase));
                if (!hasAllScope)
                {
                    if (!farmId.HasValue && !organizationId.HasValue)
                    {
                        var selectableScopes = userScopes
                            .Where(s =>
                                (string.Equals(s.ScopeType, "FARM", StringComparison.OrdinalIgnoreCase) && s.FarmId.HasValue)
                                || (string.Equals(s.ScopeType, "ORGANIZATION", StringComparison.OrdinalIgnoreCase) && s.OrganizationId.HasValue))
                            .DistinctBy(s => (s.ScopeType.ToUpperInvariant(), s.OrganizationId, s.FarmId))
                            .ToArray();
                        if (selectableScopes.Length == 1 && string.Equals(selectableScopes[0].ScopeType, "FARM", StringComparison.OrdinalIgnoreCase))
                            farmId = selectableScopes[0].FarmId;
                        else if (selectableScopes.Length == 1)
                            organizationId = selectableScopes[0].OrganizationId;
                        else
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            await context.Response.WriteAsJsonAsync(new
                            {
                                type = "forbidden_unit_scope",
                                title = "Selecione uma fazenda autorizada antes de consultar dados operacionais",
                                status = 403,
                                traceId = context.TraceIdentifier
                            }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                            return;
                        }
                    }

                    if (organizationId.HasValue && !farmId.HasValue)
                    {
                        var organizationAuthorized = userScopes.Any(s =>
                            string.Equals(s.ScopeType, "ORGANIZATION", StringComparison.OrdinalIgnoreCase)
                            && s.OrganizationId == organizationId);
                        if (!organizationAuthorized)
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            await context.Response.WriteAsJsonAsync(new
                            {
                                type = "forbidden_unit_scope",
                                title = "Usuário não possui permissão para a organização especificada",
                                status = 403,
                                traceId = context.TraceIdentifier
                            }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                            return;
                        }
                    }

                    if (farmId.HasValue)
                    {
                        var farmAuthorized = await Dapper.SqlMapper.ExecuteScalarAsync<bool>(conn, new Dapper.CommandDefinition(
                            """
                            select exists(
                                select 1
                                from agro360.geo_farms f
                                where f.tenant_id=@TenantId and f.id=@FarmId and f.deleted_at is null
                                  and (
                                      exists(select 1 from agro360.identity_user_unit_scopes s
                                             where s.tenant_id=@TenantId and s.user_id=@UserId
                                               and s.scope_type='FARM' and s.farm_id=f.id)
                                      or exists(select 1 from agro360.identity_user_unit_scopes s
                                                where s.tenant_id=@TenantId and s.user_id=@UserId
                                                  and s.scope_type='ORGANIZATION' and s.organization_id=f.organization_id)
                                  )
                            )
                            """,
                            new { TenantId = tenantId, UserId = userId, FarmId = farmId },
                            transaction: tx,
                            cancellationToken: context.RequestAborted)).ConfigureAwait(false);

                        if (!farmAuthorized)
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            await context.Response.WriteAsJsonAsync(new
                            {
                                type = "forbidden_unit_scope",
                                title = "Usuário não possui permissão para a fazenda especificada",
                                status = 403,
                                traceId = context.TraceIdentifier
                            }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                            return;
                        }

                        if (organizationId.HasValue && !userScopes.Any(s =>
                                string.Equals(s.ScopeType, "ORGANIZATION", StringComparison.OrdinalIgnoreCase)
                                && s.OrganizationId == organizationId))
                        {
                            context.Response.StatusCode = StatusCodes.Status403Forbidden;
                            await context.Response.WriteAsJsonAsync(new
                            {
                                type = "forbidden_unit_scope",
                                title = "Usuário não possui permissão para a organização especificada",
                                status = 403,
                                traceId = context.TraceIdentifier
                            }, cancellationToken: context.RequestAborted).ConfigureAwait(false);
                            return;
                        }
                    }
                }
            }

            // Cultura de apresentação: header da sessão > preferência do usuário > idioma padrão da organização > pt-BR.
            // A coluna ativa de platform_languages é a fonte do conjunto habilitado; valor inválido jamais derruba a requisição.
            var requestedCulture = context.Request.Headers["X-Culture"].FirstOrDefault() ?? string.Empty;
            var resolvedCulture = await Dapper.SqlMapper.ExecuteScalarAsync<string>(conn, new Dapper.CommandDefinition(
                """
                with habilitadas as (
                    select culture from agro360.platform_languages where active
                )
                select coalesce(
                    (select culture from habilitadas where lower(culture) = lower(@Requested)),
                    (select culture from habilitadas where lower(culture) = lower(pu.language)),
                    (select culture from habilitadas where lower(culture) = lower(ts.language)),
                    t.default_language, 'pt-BR')
                from agro360.platform_tenants t
                left join agro360.platform_tenant_settings ts on ts.tenant_id = t.id
                left join agro360.platform_user_preferences pu on pu.tenant_id = t.id and pu.user_id = @UserId
                where t.id = @TenantId
                """,
                new { Requested = requestedCulture, TenantId = tenantId, UserId = userId },
                transaction: tx,
                cancellationToken: context.RequestAborted)).ConfigureAwait(false);

            await tx.CommitAsync(context.RequestAborted).ConfigureAwait(false);

            culture = string.IsNullOrWhiteSpace(resolvedCulture) ? "pt-BR" : resolvedCulture;
        }

        var timeZone = context.Request.Headers["X-Timezone"].FirstOrDefault() ?? "America/Belem";
        tenantContext.SetScope(new TenantScope(tenantId, userId, organizationId, farmId, timeZone, culture));

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

    private sealed class UserUnitScopeRow
    {
        public string ScopeType { get; init; } = string.Empty;
        public Guid? OrganizationId { get; init; }
        public Guid? FarmId { get; init; }
    }

    private sealed class SupportSessionStateRow
    {
        public bool IsSuperAdmin { get; init; }
        public bool SessionValid { get; init; }
        public string Scope { get; init; } = string.Empty;
        public bool TenantActive { get; init; }
    }
}
