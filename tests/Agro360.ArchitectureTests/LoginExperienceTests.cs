namespace Agro360.ArchitectureTests;

public sealed class LoginExperienceTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    [Fact]
    public void LoginUsesTenantDatabasePasswordHashPermissionsAndRealTokens()
    {
        var service = Read("src/Modules/Agro360.Infrastructure/Services/IdentityService.cs");

        Assert.Contains("where slug = lower(@TenantSlug)", service);
        Assert.Contains("new { TenantSlug = tenantSlug }", service);
        Assert.DoesNotContain("new { command.TenantSlug }", service);
        Assert.Contains("u.status = 'ACTIVE'", service);
        Assert.Contains("passwordHasher.Verify(command.Password, user.PasswordHash)", service);
        Assert.Contains("identity_role_permissions", service);
        Assert.Contains("tokenService.Create", service);
        Assert.Contains("set last_login_at = now()", service);
        Assert.Contains("u.normalized_document = @Identifier", service);
        Assert.Contains("lower(u.email) = @Identifier", service);
        Assert.Contains("document.Length != 11", service);
        Assert.Contains("CNPJ identifica a organização", service);
        Assert.Contains("ck_identity_users_normalized_document", Read("database/agro360-postgres-full.sql"));
    }

    [Fact]
    public void TenantTokensRespectContractedModulesAndCanonicalPermissionCodes()
    {
        var service = Read("src/Modules/Agro360.Infrastructure/Services/IdentityService.cs");
        var sql = Read("database/agro360-postgres-full.sql");

        Assert.Contains("platform_tenant_module_entitlements", service);
        Assert.Contains("platform_tenant_modules", service);
        Assert.Contains("IsPermissionContracted", service);
        Assert.Contains("SUPER_ADMIN", service);
        Assert.Contains("'inventory.read'", sql);
        Assert.DoesNotContain("('agro360.inventory_read', 'Inventory'", sql);
    }

    [Fact]
    public void LocalHostsSwaggerAndCorsUseDocumentedFixedUrls()
    {
        var apiLaunch = Read("src/Hosts/Agro360.Api/Properties/launchSettings.json");
        var webLaunch = Read("src/Hosts/Agro360.Web/Properties/launchSettings.json");
        var settings = Read("src/Hosts/Agro360.Api/appsettings.json");
        var program = Read("src/Hosts/Agro360.Api/Program.cs");

        Assert.Contains("https://localhost:7081;http://localhost:8081", apiLaunch);
        Assert.Contains("https://localhost:7080;http://localhost:8080", webLaunch);
        Assert.Contains("https://localhost:7080", settings);
        Assert.Contains("app.UseSwaggerUI", program);
        Assert.Contains("app.UseCors(\"web\")", program);
    }

    [Fact]
    public void LoginProvidesGlobalToastsConfirmationAndConnectivityCheck()
    {
        var layout = Read("src/Hosts/Agro360.Web/Pages/Shared/_Layout.cshtml");
        var client = Read("src/Hosts/Agro360.Web/wwwroot/js/agro360.js");

        foreach (var field in new[] { "tenantSlug", "email", "password" }) Assert.Contains($"name=\"{field}\"", layout);
        foreach (var function in new[] { "toastSuccess", "toastWarning", "toastError", "confirmDialog" }) Assert.Contains(function, client);
        Assert.Contains("/health", client);
        Assert.Contains("Fechar mensagem", client);
        Assert.Contains("E-mail ou CPF", layout);
        Assert.Contains("CNPJ identifica a organização", layout);
        Assert.DoesNotContain("abra ${apiBase}/swagger", client, StringComparison.Ordinal);
        Assert.Contains("Código MFA inválido ou expirado.", client, StringComparison.Ordinal);
        Assert.Contains("Credenciais inválidas.", client, StringComparison.Ordinal);
    }

    [Fact]
    public void HomologationProvisioningPreservesMfaAndRejectsAdministrativeStateChanges()
    {
        var migrator = Read("src/Hosts/Agro360.Migrator/Program.cs");

        Assert.Contains("protector.Unprotect(existingSuperAdmin.MfaSecretEncrypted)", migrator, StringComparison.Ordinal);
        Assert.Contains("está excluída logicamente", migrator, StringComparison.Ordinal);
        Assert.Contains("está com status administrativo", migrator, StringComparison.Ordinal);
        Assert.DoesNotContain("status='ACTIVE',deleted_at=null,mfa_enabled=true", migrator, StringComparison.Ordinal);
        Assert.Contains("identity_refresh_tokens set revoked_at", migrator, StringComparison.Ordinal);
        Assert.Contains("must_change_password=true", migrator, StringComparison.Ordinal);
        Assert.Contains("diagnose-homologation", migrator, StringComparison.Ordinal);
        Assert.Contains("VerifyProvisionedIdentitiesAsync", migrator, StringComparison.Ordinal);
        Assert.Contains("select set_config('app.tenant_id',@TenantId,true)", migrator, StringComparison.Ordinal);
        Assert.Contains("hasher.Verify(item.Password, row.PasswordHash)", migrator, StringComparison.Ordinal);
        Assert.Contains("IsSupportedPasswordHash(row.PasswordHash)", migrator, StringComparison.Ordinal);
        Assert.Contains("[switch]$DiagnosticOnly", Read("scripts/provision-homologation-local.ps1"), StringComparison.Ordinal);
        Assert.Contains("provision-santa-clara", Read("scripts/provision-homologation-local.ps1"), StringComparison.Ordinal);
        Assert.Contains("user_not_found", Read("src/Modules/Agro360.Infrastructure/Services/IdentityService.cs"), StringComparison.Ordinal);
        Assert.Contains("password_verification_failed", Read("src/Modules/Agro360.Infrastructure/Services/IdentityService.cs"), StringComparison.Ordinal);
    }

    [Fact]
    public void ApiDoesNotCarryACompetingDatabaseOrVersionedPassword()
    {
        var settings = Read("src/Hosts/Agro360.Api/appsettings.json");

        Assert.DoesNotContain("DefaultConnection", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", settings, StringComparison.OrdinalIgnoreCase);
    }
}
