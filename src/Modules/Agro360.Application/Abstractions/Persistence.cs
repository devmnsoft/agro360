using System.Data.Common;

namespace Agro360.Application.Abstractions;

public interface IDbConnectionFactory
{
    ValueTask<DbConnection> OpenConnectionAsync(CancellationToken cancellationToken = default);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }

    DateOnly Today { get; }
}

public interface IPasswordHasher
{
    string Hash(string password);

    bool Verify(string password, string encodedHash);
}

public interface ITokenService
{
    TokenPair Create(Guid tenantId, Guid userId, string email, IReadOnlyCollection<string> permissions);

    string HashRefreshToken(string refreshToken);
}

public sealed record TokenPair(string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
