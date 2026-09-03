namespace Agro360.ArchitectureTests;

public sealed class AuthenticationAndLivestockRegressionTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../"));
    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    [Fact]
    public void RefreshClassifiesExpectedFailuresAndReturnsUnauthorized()
    {
        var identity = Read("src/Modules/Agro360.Infrastructure/Services/IdentityService.cs");
        var middleware = Read("src/Hosts/Agro360.Api/Middleware/ExceptionHandlingMiddleware.cs");

        foreach (var reason in new[] { "token ausente", "token não encontrado", "token expirado", "token revogado", "tenant divergente", "usuário inativo", "usuário bloqueado" })
            Assert.Contains(reason, identity, StringComparison.Ordinal);
        Assert.Contains("AuthenticationException => (StatusCodes.Status401Unauthorized", middleware, StringComparison.Ordinal);
        Assert.Contains("for update of rt", identity, StringComparison.Ordinal);
        Assert.Contains("set revoked_at = now()", identity, StringComparison.Ordinal);
        Assert.Contains("IssueTokensAsync", identity, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshClientClearsAllSessionsAndPreventsRetryLoops()
    {
        var client = Read("src/Hosts/Agro360.Web/wwwroot/js/agro360.js");

        Assert.Contains("refreshPromise", client, StringComparison.Ordinal);
        Assert.Contains("refreshStopped", client, StringComparison.Ordinal);
        Assert.Contains("sessionStorage.clear()", client, StringComparison.Ordinal);
        Assert.Contains("Sua sessão expirou. Faça login novamente.", client, StringComparison.Ordinal);
    }

    [Fact]
    public void LivestockDashboardUsesCanonicalHerdLifecycleColumn()
    {
        var service = Read("src/Modules/Agro360.Infrastructure/Services/Livestock360Service.cs");
        var schema = Read("database/agro360-postgres-full.sql");
        var dashboard = service[service.IndexOf("DashboardAsync", StringComparison.Ordinal)..];
        dashboard = dashboard[..dashboard.IndexOf("private Task<IReadOnlyList<dynamic>>", StringComparison.Ordinal)];

        Assert.DoesNotContain(" active", dashboard, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("status='ACTIVE' and deleted_at is null", dashboard, StringComparison.Ordinal);
        Assert.Contains("status varchar(20) not null default 'ACTIVE'", schema, StringComparison.Ordinal);
        Assert.DoesNotContain("head_count integer not null default 0 check(head_count>=0),active boolean", schema, StringComparison.Ordinal);
    }

    [Fact]
    public void LivestockDashboardUsesValueReturningTransactionOverload()
    {
        var service = Read("src/Modules/Agro360.Infrastructure/Services/Livestock360Service.cs");
        var dashboard = service[service.IndexOf("DashboardAsync", StringComparison.Ordinal)..];
        dashboard = dashboard[..dashboard.IndexOf("private Task<IReadOnlyList<dynamic>>", StringComparison.Ordinal)];

        Assert.Contains("InTenantTransactionAsync<LivestockDashboardDto>", dashboard, StringComparison.Ordinal);
        Assert.Contains("return new(", dashboard, StringComparison.Ordinal);
    }

    [Fact]
    public void LivestockDashboardUsesRealSchemaColumnsAndStrongEndpointContract()
    {
        var service = Read("src/Modules/Agro360.Infrastructure/Services/Livestock360Service.cs");
        var contract = Read("src/Modules/Agro360.Application/Contracts/Livestock360Contracts.cs");
        var controller = Read("src/Hosts/Agro360.Api/Controllers/Livestock360Controller.cs");

        Assert.Contains("Task<LivestockDashboardDto> DashboardAsync", contract, StringComparison.Ordinal);
        Assert.Contains("Task<LivestockDashboardDto> Dashboard", controller, StringComparison.Ordinal);
        Assert.Contains("left join agro360.livestock_herds", service, StringComparison.Ordinal);
        Assert.DoesNotContain("select coalesce(category", service, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("recentWeighings", service, StringComparison.Ordinal);
        Assert.Contains("LivestockDashboardStarted", service, StringComparison.Ordinal);
        Assert.Contains("LivestockDashboardCompleted", service, StringComparison.Ordinal);
    }

    [Fact]
    public void DatabaseFailuresLogStructuredPostgresMetadataWithoutSqlParameters()
    {
        var executor = Read("src/Modules/Agro360.Infrastructure/Persistence/DatabaseExecutor.cs");
        foreach (var field in new[] { "SqlState", "MessageText", "Detail", "Hint", "ColumnName", "TableName", "ConstraintName", "SchemaName", "TenantId", "Operation" })
            Assert.Contains($"{{{field}}}", executor, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameters", executor, StringComparison.Ordinal);
    }
}
