namespace Agro360.ArchitectureTests;

public sealed class AuthenticationAndLivestockRegressionTests
{
    private static readonly string Root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    private static string Read(string path) => File.ReadAllText(Path.Combine(Root, path));

    [Fact]
    public void OfflineShellCannotMaskApiFailuresOrCacheTenantData()
    {
        var worker = Read("src/Hosts/Agro360.Web/wwwroot/service-worker.js");
        var client = Read("src/Hosts/Agro360.Web/wwwroot/js/agro360.js");

        Assert.Contains("url.origin !== self.location.origin", worker, StringComparison.Ordinal);
        Assert.Contains("!SHELL.includes(url.pathname)", worker, StringComparison.Ordinal);
        Assert.Contains("event.request.headers.has(\"Authorization\")", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("/api/mobile/bootstrap", worker, StringComparison.Ordinal);
        Assert.DoesNotContain("caches.match(\"/field\")", worker, StringComparison.Ordinal);
        Assert.Contains("(await response.text()).trim() !== \"Healthy\"", client, StringComparison.Ordinal);
        Assert.Contains("typeof specification.openapi", client, StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshClassifiesExpectedFailuresAndReturnsUnauthorized()
    {
        var identity = Read("src/Modules/Agro360.Infrastructure/Services/IdentityService.cs");
        var middleware = Read("src/Hosts/Agro360.Api/Middleware/ExceptionHandlingMiddleware.cs");
        var program = Read("src/Hosts/Agro360.Api/Program.cs");

        foreach (var reason in new[] { "token ausente", "token não encontrado", "token expirado", "token revogado", "tenant divergente", "usuário inativo", "usuário bloqueado" })
            Assert.Contains(reason, identity, StringComparison.Ordinal);
        Assert.Contains("AuthenticationException => (StatusCodes.Status401Unauthorized", middleware, StringComparison.Ordinal);
        Assert.Contains("for update of rt", identity, StringComparison.Ordinal);
        Assert.Contains("set revoked_at = now()", identity, StringComparison.Ordinal);
        Assert.Contains("IssueTokensAsync", identity, StringComparison.Ordinal);
        Assert.True(
            program.IndexOf("UseSerilogRequestLogging", StringComparison.Ordinal) <
            program.IndexOf("UseMiddleware<ExceptionHandlingMiddleware>", StringComparison.Ordinal),
            "O logger deve observar o status final produzido pelo middleware de exceções.");
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
    public void LogoutRevokesRefreshTokenAndGlobalClientHandlesNonDashboardPages()
    {
        var identity = Read("src/Modules/Agro360.Infrastructure/Services/IdentityService.cs");
        var controller = Read("src/Hosts/Agro360.Api/Controllers/IdentityController.cs");
        var client = Read("src/Hosts/Agro360.Web/wwwroot/js/agro360.js");

        Assert.Contains("auth/logout", controller, StringComparison.Ordinal);
        Assert.Contains("set revoked_at = now()", identity, StringComparison.Ordinal);
        Assert.Contains("/api/v1/auth/logout", client, StringComparison.Ordinal);
        Assert.Contains("element(\"refresh-dashboard\")?.addEventListener", client, StringComparison.Ordinal);
        Assert.Contains("const subtitle = element(\"dashboard-subtitle\")", client, StringComparison.Ordinal);
        Assert.Contains("if (!subtitle) return", client, StringComparison.Ordinal);
    }

    [Fact]
    public void SuperAdministratorRoleIsLoadedIntoTokenAndDrivesGlobalNavigation()
    {
        var identity = Read("src/Modules/Agro360.Infrastructure/Services/IdentityService.cs");
        var tokens = Read("src/Modules/Agro360.Infrastructure/Security/SecurityServices.cs");
        var platform = Read("src/Hosts/Agro360.Api/Controllers/SaasControllers.cs");
        var client = Read("src/Hosts/Agro360.Web/wwwroot/js/agro360.js");

        Assert.Contains("select distinct r.code", identity, StringComparison.Ordinal);
        Assert.Contains("new Claim(\"role\", role)", tokens, StringComparison.Ordinal);
        Assert.Contains("Authorize(Policy = Permissions.PlatformAdmin)", platform, StringComparison.Ordinal);
        Assert.Contains("platform_super_admins", identity, StringComparison.Ordinal);
        Assert.Contains("roles ?? []", client, StringComparison.Ordinal);
        Assert.DoesNotContain("superadmin@mnsoft.com.br", client, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LivestockDashboardUsesCanonicalHerdLifecycleColumn()
    {
        var service = Read("src/Modules/Agro360.Infrastructure/Services/Livestock360Service.cs");
        var schema = Read("database/agro360-postgres-full.sql");
        var dashboard = service[service.IndexOf("DashboardAsync", StringComparison.Ordinal)..];
        dashboard = dashboard[..dashboard.IndexOf("private Task<IReadOnlyList<dynamic>>", StringComparison.Ordinal)];

        Assert.DoesNotContain("where active", dashboard, StringComparison.OrdinalIgnoreCase);
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
        Assert.Contains("ReadAsync<MetricRow>", service, StringComparison.Ordinal);
        Assert.Contains("Select(ToMetric)", service, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadAsync<MetricDto>", service, StringComparison.Ordinal);
        Assert.Contains("public long Value { get; set; }", service, StringComparison.Ordinal);
        Assert.Contains("coalesce(species,'NÃO INFORMADA') as \"Name\"", service, StringComparison.Ordinal);
    }

    [Fact]
    public void DatabaseFailuresLogStructuredPostgresMetadataWithoutSqlParameters()
    {
        var executor = Read("src/Modules/Agro360.Infrastructure/Persistence/DatabaseExecutor.cs");
        foreach (var field in new[] { "SqlState", "MessageText", "Detail", "Hint", "ColumnName", "TableName", "ConstraintName", "SchemaName", "TenantId", "Operation" })
            Assert.Contains($"{{{field}}}", executor, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameters", executor, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegratedLivestockKeepsAnimalsGroupsLocationsAndStockLotsSeparate()
    {
        var migration = Read("database/migrations/068_integrated_livestock_foundation.sql");

        Assert.Contains("livestock_animals", migration, StringComparison.Ordinal);
        Assert.Contains("livestock_herds", migration, StringComparison.Ordinal);
        Assert.Contains("livestock_locations", migration, StringComparison.Ordinal);
        Assert.Contains("control_mode", migration, StringComparison.Ordinal);
        Assert.DoesNotContain("inventory_stock_lots", migration, StringComparison.Ordinal);
        Assert.Contains("livestock_individualization_reconciliations", migration, StringComparison.Ordinal);
        Assert.Contains("collective_quantity = identified_quantity", migration, StringComparison.Ordinal);
    }

    [Fact]
    public void AnimalDetailIsTenantScopedAndCombinesAuditableSources()
    {
        var service = Read("src/Modules/Agro360.Infrastructure/Services/LivestockService.cs");

        Assert.Contains("GetAnimalDetailAsync", service, StringComparison.Ordinal);
        Assert.Contains("tenant_id=@TenantId and animal_id=@AnimalId", service, StringComparison.Ordinal);
        Assert.Contains("'ANIMAL_EVENT' as Source", service, StringComparison.Ordinal);
        Assert.Contains("'MOVEMENT' as Source", service, StringComparison.Ordinal);
        Assert.Contains("'HANDLING' as Source", service, StringComparison.Ordinal);
        Assert.Contains("'HEALTH' as Source", service, StringComparison.Ordinal);
        Assert.Contains("order by OccurredOn desc", service, StringComparison.Ordinal);
    }
}
