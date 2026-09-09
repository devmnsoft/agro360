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

    public Task<T> InTenantTransactionAsync<T>(
        Guid tenantId,
        Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> action,
        CancellationToken cancellationToken) =>
        ExecuteTransactionAsync(tenantId, "tenant-transaction", async (connection, transaction) =>
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

            return await action(connection, transaction).ConfigureAwait(false);
        }, cancellationToken);

    public Task<T> InSystemTransactionAsync<T>(
        Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> action,
        CancellationToken cancellationToken) =>
        ExecuteTransactionAsync(null, "system-transaction", action, cancellationToken);

    private async Task<T> ExecuteTransactionAsync<T>(
        Guid? tenantId,
        string operation,
        Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> action,
        CancellationToken cancellationToken)
    {
        NpgsqlConnection? connection = null;
        NpgsqlTransaction? transaction = null;
        try
        {
            connection = (NpgsqlConnection)await connectionFactory
                .OpenConnectionAsync(cancellationToken)
                .ConfigureAwait(false);
            transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

            var result = await action(connection, transaction).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await SafeRollbackAsync(transaction, cancellationToken).ConfigureAwait(false);
            throw;
        }
        catch (PostgresException exception)
        {
            await SafeRollbackAsync(transaction, cancellationToken).ConfigureAwait(false);
            LogPostgresFailure(logger, exception.SqlState, exception.MessageText, SafeDiagnostic(exception.Detail),
                SafeDiagnostic(exception.Hint), exception.ColumnName, exception.TableName, exception.ConstraintName,
                exception.SchemaName, tenantId, operation, exception);
            throw Translate(exception);
        }
        catch (NpgsqlException exception)
        {
            await SafeRollbackAsync(transaction, cancellationToken).ConfigureAwait(false);
            var translated = Translate(exception);
            LogCommunicationFailure(logger, tenantId, operation, translated.Code, exception);
            throw translated;
        }
        catch (TimeoutException exception)
        {
            await SafeRollbackAsync(transaction, cancellationToken).ConfigureAwait(false);
            LogCommunicationFailure(logger, tenantId, operation, "database_timeout", exception);
            throw new DatabaseTimeoutException("O PostgreSQL excedeu o tempo limite da operação.", exception);
        }
        catch
        {
            await SafeRollbackAsync(transaction, cancellationToken).ConfigureAwait(false);
            throw;
        }
        finally
        {
            await SafeDisposeAsync(transaction, operation).ConfigureAwait(false);
            await SafeDisposeAsync(connection, operation).ConfigureAwait(false);
        }
    }

    private static Exception Translate(PostgresException exception) => exception.SqlState switch
    {
        PostgresErrorCodes.InvalidPassword or PostgresErrorCodes.InvalidAuthorizationSpecification =>
            new DatabaseAuthenticationException("O PostgreSQL rejeitou a autenticação configurada.", exception),
        "08000" or "08003" or "08006" or "08001" or "08004" =>
            new DatabaseUnavailableException("O PostgreSQL está indisponível.", exception),
        PostgresErrorCodes.QueryCanceled =>
            new DatabaseTimeoutException("O PostgreSQL cancelou a operação por limite de tempo.", exception),
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

    private static PersistenceException Translate(NpgsqlException exception)
    {
        if (exception.Message.Contains("password has been provided", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("password authentication failed", StringComparison.OrdinalIgnoreCase))
        {
            return new DatabaseAuthenticationException("A autenticação PostgreSQL configurada não pôde ser utilizada.", exception);
        }

        if (exception.InnerException is TimeoutException || exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
        {
            return new DatabaseTimeoutException("O PostgreSQL excedeu o tempo limite da operação.", exception);
        }

        return new DatabaseUnavailableException("Falha de comunicação com o PostgreSQL.", exception);
    }

    private static async Task SafeRollbackAsync(NpgsqlTransaction? transaction, CancellationToken cancellationToken)
    {
        if (transaction?.Connection is null) return;
        try
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or ObjectDisposedException or OperationCanceledException)
        {
            // Preserva sempre a falha original; rollback é apenas a compensação de melhor esforço.
        }
    }

    private async Task SafeDisposeAsync(IAsyncDisposable? resource, string operation)
    {
        if (resource is null) return;
        try
        {
            await resource.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is NpgsqlException or InvalidOperationException or ObjectDisposedException)
        {
            LogDisposeFailure(logger, operation, resource.GetType().Name, exception);
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

    [LoggerMessage(2002, LogLevel.Error, "Falha de dependência PostgreSQL. TenantId: {TenantId}; Operation: {Operation}; Code: {Code}")]
    private static partial void LogCommunicationFailure(ILogger logger, Guid? tenantId, string operation, string code, Exception exception);

    [LoggerMessage(2003, LogLevel.Warning, "Falha ao liberar recurso PostgreSQL. Operation: {Operation}; Resource: {Resource}")]
    private static partial void LogDisposeFailure(ILogger logger, string operation, string resource, Exception exception);
}
