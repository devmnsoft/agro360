using Agro360.Application.Abstractions;
using Dapper;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Agro360.Api.Health;

public sealed class DatabaseHealthCheck(IDbConnectionFactory connectionFactory) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            var result = await connection.ExecuteScalarAsync<int>(new CommandDefinition(
                "select 1;",
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            return result == 1
                ? HealthCheckResult.Healthy("PostgreSQL disponível.")
                : HealthCheckResult.Unhealthy("PostgreSQL retornou resultado inesperado.");
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("PostgreSQL indisponível.", exception);
        }
    }
}
