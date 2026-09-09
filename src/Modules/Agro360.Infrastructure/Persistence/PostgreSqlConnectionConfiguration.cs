using Microsoft.Extensions.Configuration;
using Npgsql;

namespace Agro360.Infrastructure.Persistence;

public sealed record PostgreSqlConnectionConfiguration(
    string ConnectionString,
    string Key,
    string Source,
    string Environment,
    string AuthenticationMechanism,
    string AuthenticationSource,
    bool UsesLegacyKey)
{
    public const string CanonicalKey = "ConnectionStrings:Agro360";
    public const string LegacyKey = "ConnectionStrings:DefaultConnection";
    public const string PasswordKey = "PostgreSql:Password";
    public const string PassfileKey = "PostgreSql:Passfile";

    public static PostgreSqlConnectionConfiguration Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var environment = configuration["ASPNETCORE_ENVIRONMENT"]
            ?? configuration["DOTNET_ENVIRONMENT"]
            ?? "Production";
        var canonical = configuration[CanonicalKey];
        var legacy = configuration[LegacyKey];

        if (!string.IsNullOrWhiteSpace(canonical) && !string.IsNullOrWhiteSpace(legacy)
            && !Equivalent(canonical, legacy))
        {
            throw new InvalidOperationException(
                $"Configuração PostgreSQL conflitante no ambiente '{environment}': '{CanonicalKey}' e a chave legada " +
                $"'{LegacyKey}' apontam para conexões diferentes. Remova a chave legada; nenhuma conexão foi escolhida silenciosamente.");
        }

        var usesLegacy = string.IsNullOrWhiteSpace(canonical) && !string.IsNullOrWhiteSpace(legacy);
        var key = usesLegacy ? LegacyKey : CanonicalKey;
        var configured = usesLegacy ? legacy : canonical;
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"'{CanonicalKey}' não foi configurada no ambiente '{environment}'. Defina 'ConnectionStrings__Agro360' " +
                "ou use User Secrets; nenhuma conexão padrão é assumida.");
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(configured);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"'{key}' contém uma conexão PostgreSQL inválida no ambiente '{environment}'.", exception);
        }

        if (string.IsNullOrWhiteSpace(builder.Host)
            || string.IsNullOrWhiteSpace(builder.Database)
            || string.IsNullOrWhiteSpace(builder.Username))
        {
            throw new InvalidOperationException(
                $"'{key}' deve informar Host, Database e Username no ambiente '{environment}'.");
        }

        var password = configuration[PasswordKey];
        var configuredPassfile = configuration[PassfileKey];
        if (!string.IsNullOrWhiteSpace(password) && !string.IsNullOrWhiteSpace(configuredPassfile))
        {
            throw new InvalidOperationException(
                $"Configure apenas '{PasswordKey}' ou '{PassfileKey}' no ambiente '{environment}', não ambos.");
        }

        var authenticationMechanism = "none";
        var authenticationSource = "none";
        if (!string.IsNullOrWhiteSpace(builder.Password))
        {
            authenticationMechanism = "password";
            authenticationSource = FindSource(configuration, key);
        }
        else if (!string.IsNullOrWhiteSpace(password))
        {
            builder.Password = password;
            authenticationMechanism = "password";
            authenticationSource = FindSource(configuration, PasswordKey);
        }
        else if (!string.IsNullOrWhiteSpace(builder.Passfile))
        {
            EnsurePassfileExists(builder.Passfile, key, environment);
            authenticationMechanism = "passfile";
            authenticationSource = FindSource(configuration, key);
        }
        else if (!string.IsNullOrWhiteSpace(configuredPassfile))
        {
            EnsurePassfileExists(configuredPassfile, PassfileKey, environment);
            builder.Passfile = configuredPassfile;
            authenticationMechanism = "passfile";
            authenticationSource = FindSource(configuration, PassfileKey);
        }
        else if (!string.IsNullOrWhiteSpace(System.Environment.GetEnvironmentVariable("PGPASSWORD")))
        {
            authenticationMechanism = "password-environment";
            authenticationSource = "environment:PGPASSWORD";
        }
        else if (TryFindDefaultPassfile(out var defaultPassfile))
        {
            builder.Passfile = defaultPassfile;
            authenticationMechanism = "passfile";
            authenticationSource = "postgres-default-passfile";
        }

        if (authenticationMechanism == "none")
        {
            throw new InvalidOperationException(
                $"'{key}' foi encontrada em '{FindSource(configuration, key)}' no ambiente '{environment}', mas não há mecanismo de " +
                $"autenticação PostgreSQL. Configure '{PasswordKey}' em User Secrets/variável protegida ou '{PassfileKey}' com um passfile local. " +
                "Não envie nem versione a senha.");
        }

        return new PostgreSqlConnectionConfiguration(
            builder.ConnectionString,
            key,
            FindSource(configuration, key),
            environment,
            authenticationMechanism,
            authenticationSource,
            usesLegacy);
    }

    private static bool Equivalent(string left, string right)
    {
        try
        {
            return string.Equals(
                new NpgsqlConnectionStringBuilder(left).ConnectionString,
                new NpgsqlConnectionStringBuilder(right).ConnectionString,
                StringComparison.Ordinal);
        }
        catch (ArgumentException)
        {
            return string.Equals(left, right, StringComparison.Ordinal);
        }
    }

    private static void EnsurePassfileExists(string passfile, string key, string environment)
    {
        if (!File.Exists(System.Environment.ExpandEnvironmentVariables(passfile)))
        {
            throw new InvalidOperationException(
                $"O passfile configurado por '{key}' não existe no ambiente '{environment}'.");
        }
    }

    private static bool TryFindDefaultPassfile(out string path)
    {
        var explicitPath = System.Environment.GetEnvironmentVariable("PGPASSFILE");
        var candidate = !string.IsNullOrWhiteSpace(explicitPath)
            ? explicitPath
            : OperatingSystem.IsWindows()
                ? Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "postgresql", "pgpass.conf")
                : Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile), ".pgpass");
        path = System.Environment.ExpandEnvironmentVariables(candidate);
        return File.Exists(path);
    }

    private static string FindSource(IConfiguration configuration, string key)
    {
        if (configuration is not IConfigurationRoot root)
        {
            return "configuration-provider";
        }

        foreach (var provider in root.Providers.Reverse())
        {
            if (!provider.TryGet(key, out var value) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var type = provider.GetType().Name;
            if (type.Contains("EnvironmentVariables", StringComparison.Ordinal))
            {
                return $"environment:{key.Replace(":", "__", StringComparison.Ordinal)}";
            }

            if (type.Contains("CommandLine", StringComparison.Ordinal)) return "command-line";
            var description = provider.ToString() ?? type;
            if (description.Contains("secrets.json", StringComparison.OrdinalIgnoreCase)) return "user-secrets";
            if (description.Contains("appsettings.Development.json", StringComparison.OrdinalIgnoreCase)) return "appsettings.Development.json";
            if (description.Contains("appsettings.json", StringComparison.OrdinalIgnoreCase)) return "appsettings.json";
            return type;
        }

        return "unknown";
    }
}
