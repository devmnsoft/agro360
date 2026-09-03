using Agro360.Application.Abstractions;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Agro360.Infrastructure.Persistence;

public sealed partial class DatabaseExecutor(
    IDbConnectionFactory connectionFactory,
    ITenantContext tenantContext,
    ILogger<DatabaseExecutor> logger)
{
    public async Task InTenantTransactionAsync(
        Func<NpgsqlConnection, NpgsqlTransaction, Task> action,
        CancellationToken cancellationToken)
    {
        await InTenantTransactionAsync(async (connection, transaction) =>
        {
            await action(connection, transaction).ConfigureAwait(false);
            return true;
        }, cancellationToken).ConfigureAwait(false);
    }

    public Task<T> InTenantTransactionAsync<T>(
        Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> action,
        CancellationToken cancellationToken) =>
        InTenantTransactionAsync(tenantContext.TenantId, action, cancellationToken);

    public async Task<T> InTenantTransactionAsync<T>(
        Guid tenantId,
        Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> action,
        CancellationToken cancellationToken)
    {
        await using var connection = (NpgsqlConnection)await connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await connection.ExecuteAsync(new CommandDefinition(
                "select set_config('app.tenant_id', @TenantId, true);",
                new { TenantId = tenantId.ToString() },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);

            var tenantStatus = await connection.ExecuteScalarAsync<short?>(new CommandDefinition(
                "select status from agro360.tenancy_tenants where id = @TenantId and deleted_at is null;",
                new { TenantId = tenantId },
                transaction,
                cancellationToken: cancellationToken)).ConfigureAwait(false);
            if (tenantStatus is not (1 or 2))
            {
                throw new ForbiddenException("O tenant está suspenso, cancelado ou indisponível.");
            }

            var result = await action(connection, transaction).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (PostgresException exception)
        {
            await SafeRollbackAsync(transaction, cancellationToken).ConfigureAwait(false);
            LogPostgresFailure(logger, exception.SqlState, exception.MessageText, SafeDiagnostic(exception.Detail),
                SafeDiagnostic(exception.Hint), exception.ColumnName, exception.TableName, exception.ConstraintName,
                exception.SchemaName, tenantId, "tenant-transaction", exception);
            throw Translate(exception);
        }
        catch (NpgsqlException exception)
        {
            await SafeRollbackAsync(transaction, cancellationToken).ConfigureAwait(false);
            LogCommunicationFailure(logger, tenantId, "tenant-transaction", exception);
            throw new PersistenceException("Falha de comunicação com o PostgreSQL.", exception);
        }
        catch
        {
            await SafeRollbackAsync(transaction, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task<T> InSystemTransactionAsync<T>(
        Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> action,
        CancellationToken cancellationToken)
    {
        await using var connection = (NpgsqlConnection)await connectionFactory
            .OpenConnectionAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            var result = await action(connection, transaction).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (PostgresException exception)
        {
            await SafeRollbackAsync(transaction, cancellationToken).ConfigureAwait(false);
            LogPostgresFailure(logger, exception.SqlState, exception.MessageText, SafeDiagnostic(exception.Detail),
                SafeDiagnostic(exception.Hint), exception.ColumnName, exception.TableName, exception.ConstraintName,
                exception.SchemaName, null, "system-transaction", exception);
            throw Translate(exception);
        }
        catch (NpgsqlException exception)
        {
            await SafeRollbackAsync(transaction, cancellationToken).ConfigureAwait(false);
            LogCommunicationFailure(logger, null, "system-transaction", exception);
            throw new PersistenceException("Falha de comunicação com o PostgreSQL.", exception);
        }
        catch
        {
            await SafeRollbackAsync(transaction, cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    private static Exception Translate(PostgresException exception) => exception.SqlState switch
    {
        PostgresErrorCodes.UniqueViolation => new ConflictException(
            "Já existe um registro com os mesmos dados únicos.",
            "persistence.unique_violation"),
        PostgresErrorCodes.ForeignKeyViolation => new ConflictException(
            "O registro informado está vinculado a uma referência inexistente ou incompatível.",
            "persistence.foreign_key_violation"),
        PostgresErrorCodes.CheckViolation => new DomainException(
            "A operação viola uma regra de consistência do banco de dados.",
            "persistence.check_violation"),
        PostgresErrorCodes.SerializationFailure => new ConflictException(
            "A operação concorreu com outra alteração. Atualize os dados e tente novamente."),
        _ => new PersistenceException($"Falha PostgreSQL ({exception.SqlState}).", exception)
    };

    private static async Task SafeRollbackAsync(NpgsqlTransaction transaction, CancellationToken cancellationToken)
    {
        try
        {
            if (transaction.Connection is not null)
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (NpgsqlException)
        {
            // A conexão original já falhou; o erro de rollback não substitui a causa raiz.
        }
    }

    private static string? SafeDiagnostic(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Contains("Key (", StringComparison.OrdinalIgnoreCase)
            || value.Contains("password", StringComparison.OrdinalIgnoreCase)
            || value.Contains("token", StringComparison.OrdinalIgnoreCase)) return null;
        return value.Length <= 512 ? value : value[..512];
    }

    [LoggerMessage(2001, LogLevel.Error, "Falha PostgreSQL. SqlState: {SqlState}; MessageText: {MessageText}; Detail: {Detail}; Hint: {Hint}; ColumnName: {ColumnName}; TableName: {TableName}; ConstraintName: {ConstraintName}; SchemaName: {SchemaName}; TenantId: {TenantId}; Operation: {Operation}")]
    private static partial void LogPostgresFailure(ILogger logger, string sqlState, string messageText,
        string? detail, string? hint, string? columnName, string? tableName, string? constraintName,
        string? schemaName, Guid? tenantId, string operation, Exception exception);

    [LoggerMessage(2002, LogLevel.Error, "Falha de comunicação PostgreSQL. TenantId: {TenantId}; Operation: {Operation}")]
    private static partial void LogCommunicationFailure(ILogger logger, Guid? tenantId, string operation, Exception exception);
}
