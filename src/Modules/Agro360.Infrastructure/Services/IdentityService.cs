using System.Diagnostics;
using System.Net.Mail;
using Agro360.Application;
using Agro360.Application.Abstractions;
using Agro360.Application.Contracts;
using Agro360.Domain.Tenancy;
using Agro360.Infrastructure.Persistence;
using Agro360.SharedKernel;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Agro360.Infrastructure.Services;

public sealed class IdentityService(
    DatabaseExecutor database,
    IPasswordHasher passwordHasher,
    ITokenService tokenService,
    IClock clock,
    ILogger<IdentityService> logger) : IIdentityService
{
    public Task<BootstrapResult> BootstrapAsync(BootstrapCommand command, CancellationToken cancellationToken)
    {
        ValidateEmail(command.Email);
        var tenant = Tenant.Create(command.TenantName, command.TenantSlug, command.TimeZoneId);
        tenant.Activate();
        var organizationId = Guid.CreateVersion7();
        var userId = Guid.CreateVersion7();
        var roleId = Guid.CreateVersion7();
        var passwordHash = passwordHasher.Hash(command.Password);

        return database.InSystemTransactionAsync(async (connection, transaction) =>
        {
            var tenantAlreadyExists = await connection.ExecuteScalarAsync<bool>(new CommandDefinition(
                "select exists(select 1 from agro360.tenancy_tenants where slug = @Slug);",
                new { Slug = tenant.Slug },
                transaction: transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (tenantAlreadyExists)
            {
                throw new ForbiddenException("Já existe uma organização com este identificador.");
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.tenancy_tenants
                    (id, name, slug, timezone_id, status, created_at, version)
                values
                    (@Id, @Name, @Slug, @TimeZoneId, @Status, now(), 1);
                """,
                new
                {
                    tenant.Id,
                    tenant.Name,
                    tenant.Slug,
                    tenant.TimeZoneId,
                    Status = (short)tenant.Status
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            await connection.ExecuteAsync(new CommandDefinition(
                "select set_config('app.tenant_id', @TenantId, true);",
                new { TenantId = tenant.Id.ToString() },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            await connection.ExecuteAsync(new CommandDefinition(
                """
                insert into agro360.organization_organizations
                    (id, tenant_id, type, name, legal_name, created_at, version)
                values
                    (@Id, @TenantId, 'ECONOMIC_GROUP', @Name, @Name, now(), 1);

                insert into agro360.identity_users
                    (id, tenant_id, name, email, password_hash, status, created_at, version)
                values
                    (@UserId, @TenantId, @AdminName, lower(@Email), @PasswordHash, 'ACTIVE', now(), 1);

                insert into agro360.identity_roles
                    (id, tenant_id, code, name, is_system, created_at)
                values
                    (@RoleId, @TenantId, 'tenant-administrator', 'Administrador do tenant', false, now());

                insert into agro360.identity_user_roles (tenant_id, user_id, role_id)
                values (@TenantId, @UserId, @RoleId);

                insert into agro360.identity_role_permissions (tenant_id, role_id, permission_id)
                select @TenantId, @RoleId, id
                from agro360.identity_permissions
                where code = any(@Permissions);

                insert into agro360.audit_logs
                    (id, tenant_id, user_id, action, entity_type, entity_id, after_data, occurred_at)
                values
                    (@AuditId, @TenantId, @UserId, 'bootstrap', 'Tenant', @TenantId,
                     jsonb_build_object('slug', @TenantSlug, 'administrator', lower(@Email)), now());
                """,
                new
                {
                    Id = organizationId,
                    TenantId = tenant.Id,
                    Name = tenant.Name,
                    UserId = userId,
                    AdminName = Guard.Required(command.AdminName, nameof(command.AdminName), 160),
                    Email = command.Email.Trim(),
                    PasswordHash = passwordHash,
                    RoleId = roleId,
                    Permissions = Permissions.Administrator.ToArray(),
                    AuditId = Guid.CreateVersion7(),
                    TenantSlug = tenant.Slug
                },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return new BootstrapResult(tenant.Id, organizationId, userId, tenant.Slug);
        }, cancellationToken);
    }

    public async Task<AuthenticationResult> LoginAsync(LoginCommand command, CancellationToken cancellationToken)
    {
        var traceId = Activity.Current?.TraceId.ToString() ?? "unavailable";
        var identifierType = IdentifierType(command.Email);
        InfrastructureLogMessages.LoginStarted(logger, command.TenantSlug, identifierType, traceId);
        var identifier = NormalizeLoginIdentifier(command.Email);
        var isDocument = !identifier.Contains('@');
        var tenantSlug = command.TenantSlug.Trim();
        var tenant = await database.InSystemTransactionAsync(async (connection, transaction) =>
            await connection.QuerySingleOrDefaultAsync<TenantLookup>(new CommandDefinition(
                """
                select id, status
                from agro360.tenancy_tenants
                where slug = lower(@TenantSlug)
                  and deleted_at is null;
                """,
                new { TenantSlug = tenantSlug },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

        if (tenant is null)
        {
            InfrastructureLogMessages.LoginRejected(logger, "tenant_invalid", null, identifierType, traceId);
            throw new AuthenticationException("Cliente/organização inválido.", "tenant_invalid");
        }

        if (tenant.Status is 3 or 4 or 5)
        {
            InfrastructureLogMessages.LoginRejected(logger, "tenant_blocked", tenant.Id, identifierType, traceId);
            throw new ForbiddenException("Cliente/organização inativo ou bloqueado. Contate o suporte.");
        }

        var result = await database.InTenantTransactionAsync(tenant.Id, async (connection, transaction) =>
        {
            var user = await connection.QuerySingleOrDefaultAsync<UserLookup>(new CommandDefinition(
                """
                select id, tenant_id as TenantId, name, email, password_hash as PasswordHash,
                       status, deleted_at as DeletedAt
                from agro360.identity_users u
                where u.tenant_id = @TenantId
                  and ((not @IsDocument and u.email = lower(@Identifier))
                    or (@IsDocument and u.normalized_document = @Identifier));
                """,
                new { TenantId = tenant.Id, Identifier = identifier, IsDocument = isDocument },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (user is null)
            {
                InfrastructureLogMessages.LoginRejected(logger, "invalid_credentials", tenant.Id, identifierType, traceId);
                throw new AuthenticationException("Credenciais inválidas.", "invalid_credentials");
            }

            if (user.DeletedAt is not null || !string.Equals(user.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                InfrastructureLogMessages.LoginRejected(logger, "user_inactive_or_blocked", tenant.Id, identifierType, traceId);
                throw new ForbiddenException(user.Status is "BLOCKED" or "LOCKED"
                    ? "Usuário bloqueado. Contate o suporte."
                    : "Usuário inativo. Contate o suporte.");
            }

            if (!passwordHasher.Verify(command.Password, user.PasswordHash))
            {
                InfrastructureLogMessages.LoginRejected(logger, "invalid_credentials", tenant.Id, identifierType, traceId);
                throw new AuthenticationException("Credenciais inválidas.", "invalid_credentials");
            }

            return await IssueTokensAsync(connection, transaction, user, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        InfrastructureLogMessages.LoginSucceeded(logger, result.TenantId, result.UserId, traceId);
        return result;
    }

    public async Task<AuthenticationResult> RefreshAsync(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        const string origin = "api/v1/auth/refresh";
        var traceId = Activity.Current?.TraceId.ToString() ?? "unavailable";
        if (string.IsNullOrWhiteSpace(command.RefreshToken))
        {
            InfrastructureLogMessages.RefreshStarted(logger, null, null, traceId, origin);
            RejectRefresh("token ausente", null, null, traceId, origin);
        }

        if (!TryReadTenantId(command.RefreshToken, out var tenantId))
        {
            InfrastructureLogMessages.RefreshStarted(logger, null, null, traceId, origin);
            RejectRefresh("token inválido", null, null, traceId, origin);
        }

        var tokenHash = tokenService.HashRefreshToken(command.RefreshToken);
        var lookup = await database.InSystemTransactionAsync(async (connection, transaction) =>
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "select set_config('app.tenant_id', @TenantId, true);",
                new { TenantId = tenantId.ToString() }, transaction, cancellationToken: cancellationToken));
            return await connection.QuerySingleOrDefaultAsync<RefreshTokenLookup>(new CommandDefinition(
                """
                select rt.tenant_id as "TenantId", rt.user_id as "UserId",
                       rt.expires_at as "ExpiresAt", rt.revoked_at as "RevokedAt",
                       u.status as "UserStatus", u.deleted_at as "UserDeletedAt",
                       t.status as "TenantStatus", t.deleted_at as "TenantDeletedAt"
                from agro360.identity_refresh_tokens rt
                left join agro360.identity_users u
                  on u.tenant_id = rt.tenant_id and u.id = rt.user_id
                left join agro360.tenancy_tenants t on t.id = rt.tenant_id
                where rt.token_hash = @TokenHash
                order by rt.created_at desc
                limit 1;
                """,
                new { TokenHash = tokenHash }, transaction, cancellationToken: cancellationToken));
        },
            cancellationToken).ConfigureAwait(false);

        InfrastructureLogMessages.RefreshStarted(logger, lookup?.TenantId ?? tenantId, lookup?.UserId, traceId, origin);
        if (lookup is null) RejectRefresh("token não encontrado", tenantId, null, traceId, origin);
        if (lookup!.TenantId != tenantId) RejectRefresh("tenant divergente", tenantId, lookup.UserId, traceId, origin);
        if (lookup.RevokedAt is not null) RejectRefresh("token revogado", tenantId, lookup.UserId, traceId, origin);
        if (lookup.ExpiresAt <= clock.UtcNow) RejectRefresh("token expirado", tenantId, lookup.UserId, traceId, origin);
        if (lookup.UserDeletedAt is not null || !string.Equals(lookup.UserStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            RejectRefresh(lookup.UserStatus is "BLOCKED" or "LOCKED" ? "usuário bloqueado" : "usuário inativo", tenantId, lookup.UserId, traceId, origin);
        if (lookup.TenantDeletedAt is not null || lookup.TenantStatus is not (1 or 2))
            RejectRefresh("tenant divergente", tenantId, lookup.UserId, traceId, origin);

        var result = await database.InTenantTransactionAsync(tenantId, async (connection, transaction) =>
        {
            var user = await connection.QuerySingleOrDefaultAsync<UserLookup>(new CommandDefinition(
                """
                select u.id, u.tenant_id as TenantId, u.name, u.email, u.password_hash as PasswordHash
                from agro360.identity_refresh_tokens rt
                join agro360.identity_users u on u.id = rt.user_id and u.tenant_id = rt.tenant_id
                join agro360.tenancy_tenants t on t.id = rt.tenant_id
                where rt.tenant_id = @TenantId
                  and rt.token_hash = @TokenHash
                  and rt.revoked_at is null and rt.expires_at > now()
                  and u.status = 'ACTIVE' and u.deleted_at is null and t.status in (1, 2)
                for update of rt;
                """,
                new { TenantId = tenantId, TokenHash = tokenHash },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            if (user is null)
            {
                RejectRefresh("token revogado ou alterado concorrentemente", tenantId, lookup.UserId, traceId, origin);
            }

            await connection.ExecuteAsync(new CommandDefinition(
                """
                update agro360.identity_refresh_tokens
                set revoked_at = now()
                where tenant_id = @TenantId and token_hash = @TokenHash and revoked_at is null;
                """,
                new { TenantId = tenantId, TokenHash = tokenHash },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            return await IssueTokensAsync(connection, transaction, user!, cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        InfrastructureLogMessages.RefreshSucceeded(logger, result.TenantId, result.UserId, result.ExpiresAt, true, traceId);
        return result;
    }

    public async Task LogoutAsync(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        if (!TryReadTenantId(command.RefreshToken, out var tenantId))
        {
            return;
        }

        var tokenHash = tokenService.HashRefreshToken(command.RefreshToken);
        var revoked = await database.InTenantTransactionAsync(tenantId, async (connection, transaction) =>
            await connection.ExecuteAsync(new CommandDefinition(
                """
                update agro360.identity_refresh_tokens
                set revoked_at = now()
                where tenant_id = @TenantId and token_hash = @TokenHash and revoked_at is null;
                """,
                new { TenantId = tenantId, TokenHash = tokenHash },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false), cancellationToken).ConfigureAwait(false);

        var traceId = Activity.Current?.TraceId.ToString() ?? "unavailable";
        InfrastructureLogMessages.LogoutCompleted(logger, tenantId, revoked > 0, traceId);
    }

    private void RejectRefresh(string reason, Guid? tenantId, Guid? userId, string traceId, string origin)
    {
        InfrastructureLogMessages.RefreshRejected(logger, reason, tenantId, userId, traceId, origin);
        throw new AuthenticationException("Sua sessão expirou. Faça login novamente.", $"refresh_token_{reason.Replace(' ', '_')}");
    }

    private async Task<AuthenticationResult> IssueTokensAsync(
        Npgsql.NpgsqlConnection connection,
        Npgsql.NpgsqlTransaction transaction,
        UserLookup user,
        CancellationToken cancellationToken)
    {
        var grantedPermissions = (await connection.QueryAsync<string>(new CommandDefinition(
            """
            select distinct p.code
            from agro360.identity_user_roles ur
            join agro360.identity_role_permissions rp
              on rp.role_id = ur.role_id and rp.tenant_id = ur.tenant_id
            join agro360.identity_permissions p on p.id = rp.permission_id
            where ur.tenant_id = @TenantId and ur.user_id = @UserId
            order by p.code;
            """,
            new { user.TenantId, UserId = user.Id },
            transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        var roles = (await connection.QueryAsync<string>(new CommandDefinition(
            """
            select distinct r.code
            from agro360.identity_user_roles ur
            join agro360.identity_roles r
              on r.id = ur.role_id and r.tenant_id = ur.tenant_id
            where ur.tenant_id = @TenantId and ur.user_id = @UserId
            order by r.code;
            """,
            new { user.TenantId, UserId = user.Id },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false)).ToArray();

        var permissions = grantedPermissions;
        if (!roles.Contains("SUPER_ADMIN", StringComparer.OrdinalIgnoreCase))
        {
            var contractedModules = (await connection.QueryAsync<string>(new CommandDefinition(
                """
                select distinct module_code
                from (
                    select unnest(p.modules) as module_code
                    from agro360.saas_organizations o
                    join agro360.saas_plans p on p.id = o.plan_id
                    where o.tenant_id = @TenantId and o.status = 'ACTIVE' and p.active
                    union
                    select c.code
                    from agro360.platform_tenant_module_entitlements e
                    join agro360.platform_module_catalog c on c.id = e.module_id
                    where e.tenant_id = @TenantId and e.status in ('CONTRACTED','ACTIVE','TRIAL')
                    union
                    select m.code
                    from agro360.platform_tenant_modules tm
                    join agro360.platform_marketplace_modules m on m.id = tm.module_id
                    where tm.tenant_id = @TenantId and tm.status = 'ACTIVE'
                      and (tm.trial_ends_at is null or tm.trial_ends_at > now())
                ) contracted
                where module_code is not null;
                """,
                new { user.TenantId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            permissions = grantedPermissions
                .Where(permission => IsPermissionContracted(permission, contractedModules))
                .ToArray();
        }

        var pair = tokenService.Create(user.TenantId, user.Id, user.Email, permissions, roles);
        var refreshExpiresAt = clock.UtcNow.AddDays(14);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            update agro360.identity_users
            set last_login_at = now(), updated_at = now(), version = version + 1
            where tenant_id = @TenantId and id = @UserId;

            insert into agro360.identity_refresh_tokens
                (id, tenant_id, user_id, token_hash, expires_at, created_at)
            values
                (@Id, @TenantId, @UserId, @TokenHash, @ExpiresAt, now());
            """,
            new
            {
                Id = Guid.CreateVersion7(),
                user.TenantId,
                UserId = user.Id,
                TokenHash = tokenService.HashRefreshToken(pair.RefreshToken),
                ExpiresAt = refreshExpiresAt
            },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return new AuthenticationResult(
            user.TenantId,
            user.Id,
            user.Name,
            user.Email,
            pair.AccessToken,
            pair.RefreshToken,
            pair.ExpiresAt,
            permissions,
            roles);
    }

    private static bool IsPermissionContracted(string permission, IReadOnlySet<string> contractedModules)
    {
        var permissionGroup = permission.Split('.', 2, StringSplitOptions.TrimEntries)[0];
        string[] acceptedModules = permissionGroup switch
        {
            "properties" => ["properties"],
            "agriculture" => ["agriculture"],
            "inventory" => ["inventory"],
            "livestock" => ["livestock"],
            "crm" or "commercial" or "commercial-saas" or "customer-success" => ["commercial"],
            "finance" => ["finance"],
            "purchasing" => ["purchasing"],
            "production" => ["agroindustry"],
            "fleet" or "maintenance" => ["fleet"],
            "dashboard" => ["reports", "analytics"],
            "storage" => ["inventory", "warehousing"],
            "logistics" or "regional-logistics" => ["logistics"],
            "traceability" or "ledger" or "sales-network" => ["traceability"],
            "intelligence" => ["reports", "intelligence", "analytics", "ai", "predictive-ai"],
            "compliance" or "esg" or "sustainability" => ["environment-esg"],
            "maps" => ["properties", "analytics"],
            "cooperative" => ["cooperatives"],
            "rural-hr" or "sst" => ["verticals", "rural-hr"],
            "documents" or "evidences" or "dossiers" or "certificates" => ["documents"],
            "mobile" or "field-checklists" => ["mobile"],
            "export" or "fiscal" => [permissionGroup],
            "marketplace" or "partners" or "api-keys" or "integrations" => ["platform", "marketplace"],
            "deployment" or "governance" or "lgpd" or "security" or "work" or "support" or "portal" => ["platform"],
            _ => Array.Empty<string>()
        };

        return acceptedModules.Any(contractedModules.Contains);
    }

    private static bool TryReadTenantId(string refreshToken, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return false;
        }

        var separator = refreshToken.IndexOf('.', StringComparison.Ordinal);
        return separator == 32 && Guid.TryParseExact(refreshToken[..separator], "N", out tenantId);
    }

    private static void ValidateEmail(string email)
    {
        try
        {
            _ = new MailAddress(Guard.Required(email, nameof(email), 254));
        }
        catch (FormatException exception)
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(email)] = ["E-mail inválido."]
            })
            {
                Source = exception.Source
            };
        }
    }

    private static string NormalizeLoginIdentifier(string identifier)
    {
        var value = Guard.Required(identifier, "email", 254).Trim();
        if (value.Contains('@'))
        {
            ValidateEmail(value);
            return value.ToLowerInvariant();
        }

        var document = new string(value.Where(char.IsDigit).ToArray());
        if (document.Length is not (11 or 14) || value.Any(character =>
                !char.IsDigit(character) && character is not ('.' or '-' or '/' or ' ')))
        {
            throw new ValidationException(new Dictionary<string, string[]>
            {
                ["email"] = ["Informe um e-mail, CPF ou CNPJ válido."]
            });
        }

        return document;
    }

    private static string IdentifierType(string identifier)
    {
        if (identifier.Contains('@'))
        {
            return "EMAIL";
        }

        return new string(identifier.Where(char.IsDigit).ToArray()).Length switch
        {
            11 => "CPF",
            14 => "CNPJ",
            _ => "INVALID"
        };
    }

    private sealed class TenantLookup
    {
        public Guid Id { get; init; }

        public short Status { get; init; }
    }

    private sealed class UserLookup
    {
        public Guid Id { get; init; }

        public Guid TenantId { get; init; }

        public string Name { get; init; } = string.Empty;

        public string Email { get; init; } = string.Empty;

        public string PasswordHash { get; init; } = string.Empty;

        public string Status { get; init; } = string.Empty;

        public DateTimeOffset? DeletedAt { get; init; }
    }

    private sealed class RefreshTokenLookup
    {
        public Guid TenantId { get; set; }
        public Guid UserId { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public DateTimeOffset? RevokedAt { get; set; }
        public string? UserStatus { get; set; }
        public DateTimeOffset? UserDeletedAt { get; set; }
        public short? TenantStatus { get; set; }
        public DateTimeOffset? TenantDeletedAt { get; set; }
    }
}
