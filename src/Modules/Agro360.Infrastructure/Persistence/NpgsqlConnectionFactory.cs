using System.Data.Common;
using Agro360.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Agro360.Infrastructure.Persistence;

public sealed partial class NpgsqlConnectionFactory : IDbConnectionFactory, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public NpgsqlConnectionFactory(
        PostgreSqlConnectionConfiguration configuration,
        ILogger<NpgsqlConnectionFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (string.IsNullOrWhiteSpace(configuration.ConnectionString))
        {
            throw new InvalidOperationException("A connection string 'Agro360' é obrigatória.");
        }

        var builder = new NpgsqlDataSourceBuilder(configuration.ConnectionString);
        builder.EnableParameterLogging(false);
        _dataSource = builder.Build();
        LogConfigurationLoaded(logger,
            configuration.Key,
            configuration.Source,
            configuration.Environment,
            configuration.AuthenticationMechanism,
            configuration.AuthenticationSource,
            configuration.UsesLegacyKey);
        if (configuration.UsesLegacyKey)
        {
            LogLegacyKey(logger,
                PostgreSqlConnectionConfiguration.LegacyKey,
                PostgreSqlConnectionConfiguration.CanonicalKey);
        }
    }

    public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
        await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();

    [LoggerMessage(2101, LogLevel.Information, "Configuração PostgreSQL carregada. Key: {Key}; Source: {Source}; Environment: {Environment}; AuthenticationMechanism: {AuthenticationMechanism}; AuthenticationSource: {AuthenticationSource}; LegacyKey: {LegacyKey}")]
    private static partial void LogConfigurationLoaded(ILogger logger, string key, string source, string environment,
        string authenticationMechanism, string authenticationSource, bool legacyKey);

    [LoggerMessage(2102, LogLevel.Warning, "A chave legada {LegacyKey} está em uso. Migre para {CanonicalKey}; a compatibilidade será removida.")]
    private static partial void LogLegacyKey(ILogger logger, string legacyKey, string canonicalKey);
}
