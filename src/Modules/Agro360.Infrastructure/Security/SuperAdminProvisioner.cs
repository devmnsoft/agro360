using System.Data.Common;
using System.Net.Mail;
using Agro360.Application;
using Agro360.Application.Abstractions;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Agro360.Infrastructure.Security;

public sealed partial class SuperAdminProvisioner(
    IDbConnectionFactory connectionFactory,
    IPasswordHasher passwordHasher,
    IDataProtectionProvider dataProtectionProvider,
    IConfiguration configuration,
    ILogger<SuperAdminProvisioner> logger) : IHostedService
{
    private static readonly Guid PlatformTenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SuperAdminUserId = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid SuperAdminRoleId = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static readonly Guid SuperAdminAuthorityId = Guid.Parse("00000000-0000-0000-0000-000000000004");

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var email = configuration["SuperAdmin:Email"];
        var password = configuration["SuperAdmin:Password"];
        var totpSecret = configuration["SuperAdmin:TotpSecret"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(totpSecret))
        {
            ProvisioningSkipped(logger);
            return;
        }

        _ = new MailAddress(email);
        if (!TotpVerifier.IsValidSecret(totpSecret))
            throw new InvalidOperationException("SuperAdmin:TotpSecret deve ser um segredo Base32 com pelo menos 160 bits.");
        var passwordHash = passwordHasher.Hash(password);
        var protectedTotpSecret = dataProtectionProvider.CreateProtector(DataProtectionSettings.MfaPurpose).Protect(totpSecret);

        await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        await connection.ExecuteAsync(new CommandDefinition(
            """
            select set_config('app.tenant_id',@TenantContext,true);
            insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status,mfa_enabled,mfa_secret_encrypted,must_change_password)
            values(@UserId,@TenantId,'Super Administrador MNSOFT',lower(@Email),@PasswordHash,'ACTIVE',true,@MfaSecret,true)
            on conflict(id) do update set
                password_hash=case when agro360.identity_users.password_hash like 'unprovisioned$%' then excluded.password_hash else agro360.identity_users.password_hash end,
                mfa_enabled=true,
                mfa_secret_encrypted=coalesce(agro360.identity_users.mfa_secret_encrypted,excluded.mfa_secret_encrypted),
                updated_at=case when agro360.identity_users.mfa_secret_encrypted is null then now() else agro360.identity_users.updated_at end;
            insert into agro360.identity_roles(id,tenant_id,code,name,is_system)
            values(@RoleId,@TenantId,'SUPER_ADMIN','Super Administrador',true)
            on conflict(id) do update set code='SUPER_ADMIN',name='Super Administrador',is_system=true;
            insert into agro360.identity_user_roles(tenant_id,user_id,role_id) values(@TenantId,@UserId,@RoleId) on conflict do nothing;
            insert into agro360.identity_role_permissions(tenant_id,role_id,permission_id)
            select @TenantId,@RoleId,id from agro360.identity_permissions on conflict do nothing;
            insert into agro360.platform_super_admins(id,user_id,active)
            values(@AuthorityId,@UserId,true) on conflict(user_id) do nothing;
            update agro360.identity_refresh_tokens set revoked_at=coalesce(revoked_at,now())
            where tenant_id=@TenantId and user_id=@UserId and revoked_at is null and exists(select 1 from agro360.identity_users where id=@UserId and must_change_password);
            """,
            new
            {
                TenantContext = PlatformTenantId.ToString(),
                TenantId = PlatformTenantId,
                UserId = SuperAdminUserId,
                RoleId = SuperAdminRoleId,
                AuthorityId = SuperAdminAuthorityId,
                Email = email.Trim(),
                PasswordHash = passwordHash,
                MfaSecret = protectedTotpSecret,
                Permissions = Permissions.Administrator
            }, transaction, cancellationToken: cancellationToken)).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        ProvisioningReady(logger, SuperAdminUserId);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    [LoggerMessage(4101, LogLevel.Warning, "Super administrador não foi provisionado: defina SuperAdmin__Email, SuperAdmin__Password e SuperAdmin__TotpSecret no secret manager local.")]
    private static partial void ProvisioningSkipped(ILogger logger);

    [LoggerMessage(4102, LogLevel.Information, "Provisionamento seguro do super administrador verificado. UserId: {UserId}")]
    private static partial void ProvisioningReady(ILogger logger, Guid userId);
}
