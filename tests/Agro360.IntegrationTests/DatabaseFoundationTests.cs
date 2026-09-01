using Dapper;
using Npgsql;

namespace Agro360.IntegrationTests;

public sealed class DatabaseFoundationTests
{
    private static string? ConnectionString => Environment.GetEnvironmentVariable("AGRO360_TEST_CONNECTION_STRING");

    [Fact]
    public async Task RequiredExtensionsAndMigrationsAreInstalled()
    {
        await using var connection = new NpgsqlConnection(GetRequiredConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var extensions = (await connection.QueryAsync<string>(
            "select extname from pg_extension where extname = any(array['postgis','pg_trgm','unaccent','pgcrypto']) order by extname;"))
            .ToArray();
        var migrations = await connection.ExecuteScalarAsync<int>("select count(*) from agro360.platform_schema_migrations;");

        Assert.Equal(["pg_trgm", "pgcrypto", "postgis", "unaccent"], extensions);
        Assert.True(migrations >= 4);
    }

    [Fact]
    public async Task OutboxSupportsBoundedRetriesAndOperationalDiagnosis()
    {
        await using var connection = new NpgsqlConnection(GetRequiredConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var columns = (await connection.QueryAsync<string>(
            """
            select column_name
            from information_schema.columns
            where table_schema = 'platform'
              and table_name = 'outbox_messages'
              and column_name = any(array['correlation_id', 'last_attempt_at', 'dead_lettered_at'])
            order by column_name;
            """)).ToArray();

        Assert.Equal(["correlation_id", "dead_lettered_at", "last_attempt_at"], columns);
    }

    [Fact]
    public async Task EveryOperationalTableHasForcedTenantRls()
    {
        await using var connection = new NpgsqlConnection(GetRequiredConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var unprotected = (await connection.QueryAsync<string>(
            """
            select quote_ident(n.nspname) || '.' || quote_ident(c.relname)
            from pg_class c
            join pg_namespace n on n.oid = c.relnamespace
            where c.relkind = 'r'
              and n.nspname in (
                  'identity','organization','geo','agriculture','livestock','inventory','cost',
                  'commercial','finance','traceability','documents','environment','hr','workflow',
                  'notification','analytics','iot','logistics','fleet','purchasing','audit'
              )
              and exists (
                  select 1 from information_schema.columns col
                  where col.table_schema = n.nspname and col.table_name = c.relname and col.column_name = 'tenant_id'
              )
              and (not c.relrowsecurity or not c.relforcerowsecurity)
            order by 1;
            """)).ToArray();

        Assert.Empty(unprotected);
    }

    [Fact]
    public async Task StructuralSeedContainsPermissionsUnitsAndModuleCatalog()
    {
        await using var connection = new NpgsqlConnection(GetRequiredConnectionString());
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        var units = await connection.ExecuteScalarAsync<int>("select count(*) from agro360.platform_units;");
        var permissions = await connection.ExecuteScalarAsync<int>("select count(*) from agro360.identity_permissions;");
        var modules = await connection.ExecuteScalarAsync<int>("select count(*) from agro360.platform_modules;");

        Assert.True(units >= 12);
        Assert.True(permissions >= 13);
        Assert.True(modules >= 26);
    }

    private static string GetRequiredConnectionString()
    {
        var connectionString = ConnectionString;
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(connectionString),
            "Defina AGRO360_TEST_CONNECTION_STRING para executar os testes reais de PostgreSQL/PostGIS.");
        return connectionString!;
    }
}
