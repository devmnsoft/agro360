using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Serilog;

const int MinimumPostgresVersion = 14;
var command = args.FirstOrDefault(static value => !value.StartsWith("--", StringComparison.Ordinal))?.ToLowerInvariant() ?? "migrate";
var migrationDirectory = GetOption(args, "--migrations")
    ?? Environment.GetEnvironmentVariable("AGRO360_MIGRATIONS_PATH")
    ?? Path.Combine(AppContext.BaseDirectory, "database", "migrations");

var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true)
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

Log.Logger = new LoggerConfiguration().MinimumLevel.Information()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture).CreateLogger();

try
{
    var connectionString = configuration.GetConnectionString("Agro360")
        ?? throw new InvalidOperationException("Defina ConnectionStrings__Agro360 (ou User Secrets). Nenhuma conexão padrão é assumida.");
    await using var connection = new NpgsqlConnection(connectionString);
    await connection.OpenAsync().ConfigureAwait(false);
    if (connection.PostgreSqlVersion.Major < MinimumPostgresVersion)
    {
        throw new InvalidOperationException($"PostgreSQL {MinimumPostgresVersion}+ é obrigatório; servidor detectado: {connection.PostgreSqlVersion}.");
    }

    var files = Directory.Exists(migrationDirectory)
        ? Directory.EnumerateFiles(migrationDirectory, "*.sql").Order(StringComparer.Ordinal).ToArray()
        : throw new DirectoryNotFoundException($"Diretório de migrations não encontrado: {migrationDirectory}");

    await EnsureHistoryAsync(connection).ConfigureAwait(false);
    var applied = (await connection.QueryAsync<AppliedMigration>(
        "select version, name, checksum from agro360.platform_schema_migrations order by version;").ConfigureAwait(false))
        .ToDictionary(item => item.Version, StringComparer.Ordinal);
    var migrations = files.Select(file => Migration.Load(file)).ToArray();
    ValidateChecksums(migrations, applied);

    switch (command)
    {
        case "status":
            Log.Information("PostgreSQL {Version}; {Applied} aplicada(s), {Pending} pendente(s).", connection.PostgreSqlVersion, applied.Count, migrations.Count(item => !applied.ContainsKey(item.Version)));
            foreach (var item in migrations.Where(item => !applied.ContainsKey(item.Version))) Log.Information("Pendente: {Migration}", item.Name);
            break;
        case "validate":
            await ValidateExtensionsAsync(connection).ConfigureAwait(false);
            Log.Information("Validação concluída sem alterações; checksums e requisitos são compatíveis.");
            break;
        case "migrate":
            await MigrateAsync(connection, migrations).ConfigureAwait(false);
            break;
        case "seed":
            var environment = GetOption(args, "--environment") ?? GetSeedProfile(args)
                ?? throw new ArgumentException("Use seed minimal|demo ou seed --environment Development|Homologation|Production.");
            await SeedAsync(connection, environment).ConfigureAwait(false);
            break;
        default:
            throw new ArgumentException("Comando inválido. Use status, validate, migrate, seed minimal ou seed demo.");
    }

    return 0;
}
catch (Exception exception)
{
    Log.Fatal(exception, "Operação {Command} falhou; nenhuma migration com falha foi registrada.", command);
    return 1;
}
finally { await Log.CloseAndFlushAsync().ConfigureAwait(false); }

static async Task EnsureHistoryAsync(NpgsqlConnection connection) => await connection.ExecuteAsync(
    """
    create schema if not exists agro360;
    create table if not exists agro360.platform_schema_migrations (
      version varchar(160) primary key, name varchar(260) not null default '',
      checksum varchar(64) not null, applied_at timestamptz not null default now()
    );
    alter table agro360.platform_schema_migrations add column if not exists name varchar(260) not null default '';
    """).ConfigureAwait(false);

static void ValidateChecksums(IEnumerable<Migration> migrations, IReadOnlyDictionary<string, AppliedMigration> applied)
{
    foreach (var migration in migrations)
        if (applied.TryGetValue(migration.Version, out var previous) && !string.Equals(previous.Checksum, migration.Checksum, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Migration aplicada '{migration.Name}' foi alterada (checksum divergente).");
}

static async Task MigrateAsync(NpgsqlConnection connection, Migration[] migrations)
{
    await connection.ExecuteAsync("select pg_advisory_lock(hashtext('mnsoft-agro360-migrator'));").ConfigureAwait(false);
    try
    {
        // Releia o histórico depois de adquirir o lock. Assim, um processo que
        // aguardou outro migrator não tenta reaplicar migrations recém-concluídas.
        var applied = (await connection.QueryAsync<AppliedMigration>(
            "select version, name, checksum from agro360.platform_schema_migrations order by version;").ConfigureAwait(false))
            .ToDictionary(item => item.Version, StringComparer.Ordinal);
        ValidateChecksums(migrations, applied);
        foreach (var migration in migrations.Where(item => !applied.ContainsKey(item.Version)))
        {
            Log.Information("Aplicando {Migration}...", migration.Name);
            await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
            await connection.ExecuteAsync(migration.Sql, transaction: transaction, commandTimeout: 300).ConfigureAwait(false);
            await connection.ExecuteAsync("insert into agro360.platform_schema_migrations(version,name,checksum) values (@Version,@Name,@Checksum);", migration, transaction).ConfigureAwait(false);
            await transaction.CommitAsync().ConfigureAwait(false);
        }
    }
    finally { await connection.ExecuteAsync("select pg_advisory_unlock(hashtext('mnsoft-agro360-migrator'));").ConfigureAwait(false); }
    Log.Information("Banco Agro 360 atualizado com sucesso.");
}

static async Task ValidateExtensionsAsync(NpgsqlConnection connection)
{
    var available = await connection.ExecuteScalarAsync<bool>("select exists(select 1 from pg_available_extensions where name='postgis');").ConfigureAwait(false);
    if (!available) throw new InvalidOperationException("PostGIS não está disponível no servidor. Solicite ao administrador a instalação do pacote PostGIS e execute CREATE EXTENSION postgis.");
}

static async Task SeedAsync(NpgsqlConnection connection, string environment)
{
    var normalized = environment.ToLowerInvariant() switch { "production" => "minimal-production", "development" => "development", "homologation" => "homologation", _ => throw new ArgumentException("Ambiente de seed inválido.") };
    var root = Environment.GetEnvironmentVariable("AGRO360_DATABASE_PATH") ?? Path.Combine(AppContext.BaseDirectory, "database");
    var file = Path.Combine(root, "seeds", normalized + ".sql");
    if (!File.Exists(file)) throw new FileNotFoundException("Seed não encontrado.", file);
    await connection.ExecuteAsync(await File.ReadAllTextAsync(file).ConfigureAwait(false), commandTimeout: 180).ConfigureAwait(false);
    Log.Information("Seed {Environment} aplicado explicitamente.", environment);
}

static string? GetOption(string[] values, string name)
{
    var index = Array.FindIndex(values, value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));
    return index >= 0 && index + 1 < values.Length ? values[index + 1] : null;
}

static string? GetSeedProfile(string[] values)
{
    var seedIndex = Array.FindIndex(values, value => string.Equals(value, "seed", StringComparison.OrdinalIgnoreCase));
    if (seedIndex < 0 || seedIndex + 1 >= values.Length) return null;
    return values[seedIndex + 1].ToLowerInvariant() switch
    {
        "minimal" => "Production",
        "demo" => "Homologation",
        _ => throw new ArgumentException("Perfil de seed inválido. Use minimal ou demo.")
    };
}

internal sealed record Migration(string Version, string Name, string Checksum, string Sql)
{
    public static Migration Load(string file)
    {
        var sql = File.ReadAllText(file);
        var name = Path.GetFileName(file);
        return new(name, name, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant(), sql);
    }
}

internal sealed class AppliedMigration
{
    public string Version { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Checksum { get; init; } = string.Empty;
}
