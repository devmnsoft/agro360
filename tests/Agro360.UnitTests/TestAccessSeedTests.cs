using Agro360.Infrastructure.Security;

namespace Agro360.UnitTests;

public sealed class TestAccessSeedTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));

    [Theory]
    [InlineData("Admin@123456", "pbkdf2-sha512$210000$QWdybzM2MFN1cGVyQWRtIQ==$Rw4HKe5g05CZwbA/Qiob2Q5i4oX/RfWIT6HzLtIduRo=")]
    [InlineData("Cliente@123456", "pbkdf2-sha512$210000$QWdybzM2MENsaWVudGUh$tljrR4u+oXvvHnMM3fcTR/HtHJ8iuX5pcXqXehWteH4=")]
    public void TestCredentialHashesUseTheApplicationPasswordHasher(string password, string encodedHash)
    {
        Assert.True(new PasswordHasher().Verify(password, encodedHash));
        Assert.False(new PasswordHasher().Verify(password + "!", encodedHash));
    }

    [Fact]
    public void TestAccessSeedIsPersistentIdempotentAndTenantScoped()
    {
        var sql = File.ReadAllText(Path.Combine(Root, "database/seed-test-access.sql"));

        Assert.Contains("on conflict(id) do update", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("on conflict(user_id) do update", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("select set_config('app.tenant_id'", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("superadmin@agro360.local", sql, StringComparison.Ordinal);
        Assert.Contains("admin.cliente@agro360.local", sql, StringComparison.Ordinal);
        Assert.Contains("'11222333000181','CNPJ'", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Admin@123456", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("Cliente@123456", sql, StringComparison.Ordinal);
    }
}
