using System.Globalization;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Serilog;

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()
    .Build();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .CreateLogger();

var connectionString = configuration.GetConnectionString("PostgreSQL")
    ?? throw new InvalidOperationException("ConnectionStrings:PostgreSQL é obrigatória.");
var migrationDirectory = Path.Combine(AppContext.BaseDirectory, "database", "migrations");

try
{
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync().ConfigureAwait(false);
    await connection.ExecuteAsync("select pg_advisory_lock(hashtext('mnsoft-agro360-migrator'));").ConfigureAwait(false);
    try
    {
        await connection.ExecuteAsync(
            """
            create schema if not exists platform;
            create table if not exists platform.schema_migrations (
                version varchar(160) primary key,
                checksum varchar(64) not null,
                applied_at timestamptz not null default now()
            );
            """).ConfigureAwait(false);

        var applied = (await connection.QueryAsync<AppliedMigration>(
            "select version, checksum from platform.schema_migrations;").ConfigureAwait(false))
            .ToDictionary(item => item.Version, item => item.Checksum, StringComparer.Ordinal);
        var files = Directory.EnumerateFiles(migrationDirectory, "*.sql", SearchOption.TopDirectoryOnly)
            .Order(StringComparer.Ordinal)
            .ToArray();

        foreach (var file in files)
        {
            var version = Path.GetFileName(file);
            var sql = await File.ReadAllTextAsync(file).ConfigureAwait(false);
            var checksum = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(sql)))
                .ToLowerInvariant();
            if (applied.TryGetValue(version, out var previousChecksum))
            {
                if (!string.Equals(previousChecksum, checksum, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"A migração aplicada '{version}' foi alterada.");
                }

                Log.Information("Migração {Version} já aplicada.", version);
                continue;
            }

            Log.Information("Aplicando migração {Version}...", version);
            await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
            try
            {
                await connection.ExecuteAsync(sql, transaction: transaction, commandTimeout: 180).ConfigureAwait(false);
                await connection.ExecuteAsync(
                    "insert into platform.schema_migrations (version, checksum) values (@Version, @Checksum);",
                    new { Version = version, Checksum = checksum },
                    transaction).ConfigureAwait(false);
                await transaction.CommitAsync().ConfigureAwait(false);
            }
            catch
            {
                await transaction.RollbackAsync().ConfigureAwait(false);
                throw;
            }
        }
    }
    finally
    {
        await connection.ExecuteAsync("select pg_advisory_unlock(hashtext('mnsoft-agro360-migrator'));").ConfigureAwait(false);
    }

    Log.Information("Banco Agro 360 atualizado com sucesso.");
    return 0;
}
catch (Exception exception)
{
    Log.Fatal(exception, "Falha ao atualizar o banco Agro 360.");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync().ConfigureAwait(false);
}

internal sealed class AppliedMigration
{
    public string Version { get; init; } = string.Empty;

    public string Checksum { get; init; } = string.Empty;
}
