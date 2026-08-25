namespace Agro360.Application.Contracts;

public sealed record BootstrapCommand(
    string TenantName,
    string TenantSlug,
    string AdminName,
    string Email,
    string Password,
    string TimeZoneId = "America/Belem");

public sealed record BootstrapResult(Guid TenantId, Guid OrganizationId, Guid UserId, string TenantSlug);

public sealed record LoginCommand(string TenantSlug, string Email, string Password);

public sealed record RefreshTokenCommand(string RefreshToken);

public sealed record AuthenticationResult(
    Guid TenantId,
    Guid UserId,
    string Name,
    string Email,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    IReadOnlyCollection<string> Permissions);

public interface IIdentityService
{
    Task<BootstrapResult> BootstrapAsync(BootstrapCommand command, CancellationToken cancellationToken);

    Task<AuthenticationResult> LoginAsync(LoginCommand command, CancellationToken cancellationToken);

    Task<AuthenticationResult> RefreshAsync(RefreshTokenCommand command, CancellationToken cancellationToken);
}
