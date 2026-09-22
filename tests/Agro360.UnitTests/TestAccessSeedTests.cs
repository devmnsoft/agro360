using Agro360.Infrastructure.Security;

namespace Agro360.UnitTests;

public sealed class TestAccessSeedTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Theory]
    [InlineData("Admin@123456", "pbkdf2-sha512$210000$QWdybzM2MFN1cGVyQWRtIQ==$Rw4HKe5g05CZwbA/Qiob2Q5i4oX/RfWIT6HzLtIduRo=")]
    [InlineData("Cliente@123456", "pbkdf2-sha512$210000$QWdybzM2MENsaWVudGUh$tljrR4u+oXvvHnMM3fcTR/HtHJ8iuX5pcXqXehWteH4=")]
    [InlineData("Operador@123456", "pbkdf2-sha512$210000$QWdybzM2ME9wZXJhZG9yIQ==$/0AX57LMqF6X3yzEPavcE2vFRSM/PxRg3sXMTjbZjkQ=")]
    public void TestCredentialHashesUseTheApplicationPasswordHasher(string password, string encodedHash)
    {
        Assert.True(new PasswordHasher().Verify(password, encodedHash));
        Assert.False(new PasswordHasher().Verify(password + "!", encodedHash));
    }

    [Fact]
    public void TestAccessSeedIsPersistentIdempotentAndTenantScoped()
    {
        var sql = File.ReadAllText(Path.Combine(Root, "database/seed-test-access.sql"));

        Assert.Contains("p.normalized_document='11222333000181'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("on conflict(user_id) do update", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("select set_config('app.tenant_id'", sql, StringComparison.OrdinalIgnoreCase);
        foreach (var tenant in new[] { "agro360-platform", "fazenda-santa-clara", "cooperativa-vale-verde", "fazenda-bloqueada-teste" })
            Assert.Contains(tenant, sql, StringComparison.Ordinal);
        foreach (var login in new[] { "superadmin@agro360.local", "admin.santaclara@agro360.local", "operador.santaclara@agro360.local", "admin.valeverde@agro360.local", "admin.bloqueado@agro360.local" })
            Assert.Contains(login, sql, StringComparison.Ordinal);
        foreach (var secret in new[] { "Admin@123456", "Cliente@123456", "Operador@123456" })
            Assert.DoesNotContain(secret, sql, StringComparison.Ordinal);
        Assert.Contains("Tenant de teste bloqueado para validação de acesso", sql, StringComparison.Ordinal);
        Assert.Contains("delete from agro360.platform_tenant_module_entitlements", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("profile_not_linked", File.ReadAllText(Path.Combine(Root, "src/Modules/Agro360.Infrastructure/Services/IdentityService.cs")), StringComparison.Ordinal);
    }
}
