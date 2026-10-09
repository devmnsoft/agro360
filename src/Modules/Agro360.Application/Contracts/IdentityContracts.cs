namespace Agro360.Application.Contracts;

public sealed record BootstrapCommand(
    string TenantName,
    string TenantSlug,
    string AdminName,
    string Email,
    string Password,
    string TimeZoneId = "America/Belem");

public sealed record BootstrapResult(Guid TenantId, Guid OrganizationId, Guid UserId, string TenantSlug);

public sealed record LoginCommand(string TenantSlug, string Email, string Password, string? MfaCode = null, string? NewPassword = null);

public sealed record RefreshTokenCommand(string RefreshToken);

public sealed record LanguagePreferenceCommand(string Language);

public sealed record AuthenticationResult(
    Guid TenantId,
    Guid UserId,
    string Name,
    string Email,
    string AccessToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    IReadOnlyCollection<string> Permissions,
    IReadOnlyCollection<string> Roles,
    string Language = "pt-BR");

public sealed record SessionValidationResult(
    bool IsValid,
    Guid? TenantId = null,
    Guid? UserId = null,
    string? Name = null,
    string? Email = null,
    IReadOnlyCollection<string>? Roles = null,
    IReadOnlyCollection<string>? Permissions = null,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    string? Language = null);

public interface IIdentityService
{
    Task<BootstrapResult> BootstrapAsync(BootstrapCommand command, CancellationToken cancellationToken);

    Task<AuthenticationResult> LoginAsync(LoginCommand command, CancellationToken cancellationToken);

    Task<AuthenticationResult> RefreshAsync(RefreshTokenCommand command, CancellationToken cancellationToken);

    Task LogoutAsync(RefreshTokenCommand command, CancellationToken cancellationToken);

    Task<SessionValidationResult> ValidateSessionAsync(System.Security.Claims.ClaimsPrincipal principal, CancellationToken cancellationToken);

    Task<string> GetCulturePreferenceAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken);

    Task<string> ChangeCulturePreferenceAsync(Guid tenantId, Guid userId, string language, CancellationToken cancellationToken);
}

