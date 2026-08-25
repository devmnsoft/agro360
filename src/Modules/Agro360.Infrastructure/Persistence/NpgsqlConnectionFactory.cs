using System.Data.Common;
using Agro360.Application.Abstractions;
using Npgsql;

namespace Agro360.Infrastructure.Persistence;

public sealed class NpgsqlConnectionFactory : IDbConnectionFactory, IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public NpgsqlConnectionFactory(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("A connection string 'PostgreSQL' é obrigatória.");
        }

        var builder = new NpgsqlDataSourceBuilder(connectionString);
        builder.EnableParameterLogging(false);
        _dataSource = builder.Build();
    }

    public async ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default) =>
        await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);

    public ValueTask DisposeAsync() => _dataSource.DisposeAsync();
}
