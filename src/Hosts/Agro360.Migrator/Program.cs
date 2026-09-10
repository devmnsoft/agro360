using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Agro360.Infrastructure.Security;
using Dapper;
using Microsoft.AspNetCore.DataProtection;
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
    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
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
        case "provision-homologation":
            await ProvisionHomologationAsync(connection, configuration, args).ConfigureAwait(false);
            break;
        default:
            throw new ArgumentException("Comando inválido. Use status, validate, migrate, seed minimal, seed demo ou provision-homologation.");
    }

    return 0;
}
catch (Exception exception)
{
    Log.Fatal(exception, "Operação {Command} falhou; nenhuma migration com falha foi registrada.", command);
    return 1;
}
finally { await Log.CloseAndFlushAsync().ConfigureAwait(false); }

static async Task ProvisionHomologationAsync(NpgsqlConnection connection, IConfiguration configuration, string[] args)
{
    var environment = (GetOption(args, "--environment") ?? configuration["DOTNET_ENVIRONMENT"] ?? configuration["ASPNETCORE_ENVIRONMENT"] ?? "").Trim();
    if (string.Equals(environment, "Production", StringComparison.OrdinalIgnoreCase) ||
        (!string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase) && !string.Equals(environment, "Homologation", StringComparison.OrdinalIgnoreCase)))
        throw new InvalidOperationException("O provisionamento demonstrativo exige --environment Development ou Homologation e é proibido em Production.");

    var superPassword = RequireSecret("AGRO360_PROVISION_SUPERADMIN_PASSWORD");
    var tenantPassword = RequireSecret("AGRO360_PROVISION_SANTA_CLARA_PASSWORD");
    var totpSecret = RequireSecret("AGRO360_PROVISION_SUPERADMIN_TOTP_SECRET");
    var totpCode = RequireSecret("AGRO360_PROVISION_SUPERADMIN_TOTP_CODE");
    if (!TotpVerifier.IsValidSecret(totpSecret) || !TotpVerifier.Verify(totpSecret, totpCode, DateTimeOffset.UtcNow))
        throw new InvalidOperationException("Confirme no aplicativo autenticador um código TOTP atual antes de provisionar o SuperAdmin.");

    var dataProtectionPath = configuration["DataProtection:KeysPath"] ?? Environment.GetEnvironmentVariable("AGRO360_DATA_PROTECTION_KEYS_PATH");
    if (string.IsNullOrWhiteSpace(dataProtectionPath))
        throw new InvalidOperationException("Defina AGRO360_DATA_PROTECTION_KEYS_PATH para um diretório local persistente e não versionado.");
    Directory.CreateDirectory(dataProtectionPath);
    var protector = DataProtectionProvider.Create(
        new DirectoryInfo(dataProtectionPath),
        builder => builder.SetApplicationName(DataProtectionSettings.ApplicationName))
        .CreateProtector(DataProtectionSettings.MfaPurpose);
    var hasher = new PasswordHasher();
    var builder = new NpgsqlConnectionStringBuilder(connection.ConnectionString);
    Log.Information("Provisionamento {Environment} no PostgreSQL {Host}:{Port}/{Database} como {Username}. Senhas, hashes, tokens e segredo MFA não serão registrados.",
        environment, builder.Host, builder.Port, builder.Database, builder.Username);

    await using var transaction = await connection.BeginTransactionAsync().ConfigureAwait(false);
    var fixturesValid = await connection.ExecuteScalarAsync<bool>(
        "select exists(select 1 from agro360.tenancy_tenants where id='00000000-0000-0000-0000-000000000001' and slug='agro360-platform') and exists(select 1 from agro360.tenancy_tenants where id='30000000-0000-0000-0000-000000000001' and slug='santa-clara');", transaction: transaction);
    if (!fixturesValid)
        throw new InvalidOperationException("Os tenants canônicos de homologação não existem ou sua identidade diverge; execute a instalação/seed apropriada em uma base de homologação.");
    var identities = (await connection.QueryAsync<ProvisionedIdentity>(
        "select id,tenant_id as TenantId,email from agro360.identity_users where lower(email)=any(@Emails) for update;",
        new { Emails = new[] { "superadmin@mnsoft.com.br", "admin@santaclara.agro360.local" } }, transaction)).ToArray();
    foreach (var identity in identities)
    {
        var expectedTenant = identity.Email.Equals("superadmin@mnsoft.com.br", StringComparison.OrdinalIgnoreCase)
            ? Guid.Parse("00000000-0000-0000-0000-000000000001") : Guid.Parse("30000000-0000-0000-0000-000000000001");
        var expectedId = identity.Email.Equals("superadmin@mnsoft.com.br", StringComparison.OrdinalIgnoreCase)
            ? Guid.Parse("00000000-0000-0000-0000-000000000002") : Guid.Parse("30000000-0000-0000-0000-000000000002");
        if (identity.TenantId != expectedTenant || identity.Id != expectedId)
            throw new InvalidOperationException($"Identidade {identity.Email} não corresponde à fixture de homologação; nenhuma alteração foi aplicada.");
    }

    var occupiedFixtureIds = await connection.QueryAsync<ProvisionedIdentity>(
        "select id,tenant_id as TenantId,email from agro360.identity_users where id=any(@Ids) for update;",
        new { Ids = new[] { Guid.Parse("00000000-0000-0000-0000-000000000002"), Guid.Parse("30000000-0000-0000-0000-000000000002") } }, transaction);
    if (occupiedFixtureIds.Any(identity =>
            (identity.Id == Guid.Parse("00000000-0000-0000-0000-000000000002") &&
             (!identity.Email.Equals("superadmin@mnsoft.com.br", StringComparison.OrdinalIgnoreCase) || identity.TenantId != Guid.Parse("00000000-0000-0000-0000-000000000001"))) ||
            (identity.Id == Guid.Parse("30000000-0000-0000-0000-000000000002") &&
             (!identity.Email.Equals("admin@santaclara.agro360.local", StringComparison.OrdinalIgnoreCase) || identity.TenantId != Guid.Parse("30000000-0000-0000-0000-000000000001")))))
        throw new InvalidOperationException("Um ID reservado de homologação pertence a outra identidade; nenhuma alteração foi aplicada.");

    await connection.ExecuteAsync(
        """
        select set_config('app.tenant_id','00000000-0000-0000-0000-000000000001',true);
        insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status,mfa_enabled,mfa_secret_encrypted,must_change_password)
        values ('00000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000001','Super Administrador MNSOFT','superadmin@mnsoft.com.br',@SuperHash,'ACTIVE',true,@MfaSecret,true)
        on conflict(id) do update set name=excluded.name,email=excluded.email,password_hash=excluded.password_hash,status='ACTIVE',deleted_at=null,mfa_enabled=true,mfa_secret_encrypted=excluded.mfa_secret_encrypted,must_change_password=true,updated_at=now(),version=agro360.identity_users.version+1;
        insert into agro360.identity_user_roles(tenant_id,user_id,role_id) values ('00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000003') on conflict do nothing;
        insert into agro360.platform_super_admins(id,user_id,active) values ('00000000-0000-0000-0000-000000000004','00000000-0000-0000-0000-000000000002',true) on conflict(user_id) do update set active=true,deleted_at=null,updated_at=now();
        update agro360.identity_refresh_tokens set revoked_at=coalesce(revoked_at,now()) where tenant_id='00000000-0000-0000-0000-000000000001' and user_id='00000000-0000-0000-0000-000000000002';
        insert into agro360.audit_logs(id,tenant_id,user_id,action,entity_type,entity_id,after_data) values (gen_random_uuid(),'00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000002','homologation_access_provisioned','IdentityUser','00000000-0000-0000-0000-000000000002',jsonb_build_object('environment',@Environment,'sessionsRevoked',true,'mustChangePassword',true,'mfaConfirmed',true));
        select set_config('app.tenant_id','30000000-0000-0000-0000-000000000001',true);
        insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status,normalized_document,document_type,must_change_password)
        values ('30000000-0000-0000-0000-000000000003','30000000-0000-0000-0000-000000000001','Administrador Santa Clara','admin@santaclara.agro360.local',@TenantHash,'ACTIVE','52998224725','CPF',true)
        on conflict(id) do update set name=excluded.name,email=excluded.email,password_hash=excluded.password_hash,status='ACTIVE',deleted_at=null,must_change_password=true,updated_at=now(),version=agro360.identity_users.version+1;
        insert into agro360.identity_user_roles(tenant_id,user_id,role_id) values ('30000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000003','30000000-0000-0000-0000-000000000004') on conflict do nothing;
        update agro360.identity_refresh_tokens set revoked_at=coalesce(revoked_at,now()) where tenant_id='30000000-0000-0000-0000-000000000001' and user_id='30000000-0000-0000-0000-000000000003';
        insert into agro360.audit_logs(id,tenant_id,user_id,action,entity_type,entity_id,after_data) values (gen_random_uuid(),'30000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000003','homologation_access_provisioned','IdentityUser','30000000-0000-0000-0000-000000000003',jsonb_build_object('environment',@Environment,'sessionsRevoked',true,'mustChangePassword',true));
        """, new { SuperHash = hasher.Hash(superPassword), TenantHash = hasher.Hash(tenantPassword), MfaSecret = protector.Protect(totpSecret), Environment = environment }, transaction).ConfigureAwait(false);
    await transaction.CommitAsync().ConfigureAwait(false);
    Log.Information("Duas identidades de homologação foram ativadas; sessões anteriores revogadas e troca de senha exigida.");
}

static string RequireSecret(string name) => Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
    ? value : throw new InvalidOperationException($"Defina {name} no ambiente local; o valor não deve ser versionado.");

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

internal sealed record ProvisionedIdentity(Guid Id, Guid TenantId, string Email);

internal sealed record Migration(string Version, string Name, string Checksum, string Sql)
{
    public static Migration Load(string file)
    {
        var sql = File.ReadAllText(file);
        var name = Path.GetFileName(file);
        var checksum = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sql))).ToLowerInvariant();
        // The migrator owns the transaction so applying a file and recording its
        // checksum are atomic. Some historical scripts carry standalone BEGIN /
        // COMMIT commands; executing those inside the outer transaction completes
        // the Npgsql transaction before history can be written. Strip only those
        // boundary lines at execution time and retain the original bytes for the
        // checksum, preserving compatibility with already applied migrations.
        var executableSql = Regex.Replace(
            sql,
            @"^\s*(?:begin|commit)\s*;\s*$",
            string.Empty,
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant);
        return new(name, name, checksum, executableSql);
    }
}

internal sealed class AppliedMigration
{
    public string Version { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Checksum { get; init; } = string.Empty;
}
