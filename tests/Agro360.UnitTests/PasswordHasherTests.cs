using Agro360.Infrastructure.Security;

namespace Agro360.UnitTests;

public sealed class PasswordHasherTests
{
    [Fact]
    public void DevelopmentSuperAdministratorHashUsesTheRealLoginAlgorithm()
    {
        const string storedHash = "pbkdf2-sha512$210000$QWdybzM2ME1OU09GVCEh$hiccVEYBSwMAvQ4i85qQ+EN09O0fKa7TGmXfJyqHrGQ=";

        var hasher = new PasswordHasher();

        Assert.True(hasher.Verify("Admin@123456", storedHash));
        Assert.False(hasher.Verify("senha-incorreta", storedHash));
    }
}
