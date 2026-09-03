namespace Agro360.ArchitectureTests;

public sealed class LoginExperienceTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
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
    }
}
