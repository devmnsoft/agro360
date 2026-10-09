using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController]
[Route("api/v1")]
public sealed class IdentityController(IIdentityService identityService, IConfiguration configuration) : ControllerBase
{
    [HttpPost("bootstrap")]
    [AllowAnonymous]
    [ProducesResponseType<BootstrapResult>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Bootstrap(BootstrapCommand command, CancellationToken cancellationToken)
    {
        if (!configuration.GetValue<bool>("Bootstrap:Enabled"))
        {
            return NotFound();
        }

        var result = await identityService.BootstrapAsync(command, cancellationToken).ConfigureAwait(false);
        return Created("/api/v1/auth/login", result);
    }

    [HttpPost("auth/login")]
    [AllowAnonymous]
    [ProducesResponseType<AuthenticationResult>(StatusCodes.Status200OK)]
    public Task<AuthenticationResult> Login(LoginCommand command, CancellationToken cancellationToken) =>
        identityService.LoginAsync(command, cancellationToken);

    [HttpPost("auth/refresh")]
    [AllowAnonymous]
    [ProducesResponseType<AuthenticationResult>(StatusCodes.Status200OK)]
    public Task<AuthenticationResult> Refresh(RefreshTokenCommand command, CancellationToken cancellationToken) =>
        identityService.RefreshAsync(command, cancellationToken);

    [HttpPost("auth/logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(RefreshTokenCommand command, CancellationToken cancellationToken)
    {
        await identityService.LogoutAsync(command, cancellationToken).ConfigureAwait(false);
        return NoContent();
    }

    [HttpGet("auth/session")]
    [Authorize]
    [ProducesResponseType<SessionValidationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetSession(CancellationToken cancellationToken)
    {
        var result = await identityService.ValidateSessionAsync(User, cancellationToken).ConfigureAwait(false);
        if (!result.IsValid)
        {
            return Unauthorized(new
            {
                type = result.ErrorCode ?? "session_invalid",
                title = "Sessão inválida ou revogada.",
                detail = result.ErrorMessage ?? "Acesso não autorizado.",
                status = 401
            });
        }
        return Ok(result);
    }

    [HttpGet("auth/preferences/language")]
    [Authorize]
    public async Task<IActionResult> GetLanguagePreference(CancellationToken cancellationToken)
    {
        if (!TryGetUserScope(out var tenantId, out var userId))
        {
            return Unauthorized();
        }

        var language = await identityService.GetCulturePreferenceAsync(tenantId, userId, cancellationToken).ConfigureAwait(false);
        return Ok(new { language });
    }

    [HttpPut("auth/preferences/language")]
    [Authorize]
    public async Task<IActionResult> ChangeLanguagePreference(LanguagePreferenceCommand command, CancellationToken cancellationToken)
    {
        if (!TryGetUserScope(out var tenantId, out var userId))
        {
            return Unauthorized();
        }

        var language = await identityService.ChangeCulturePreferenceAsync(tenantId, userId, command.Language, cancellationToken).ConfigureAwait(false);
        return Ok(new { language });
    }

    private bool TryGetUserScope(out Guid tenantId, out Guid userId)
    {
        var tenantStr = User.FindFirst("tenant_id")?.Value;
        var sub = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        var tenantOk = Guid.TryParse(tenantStr, out tenantId);
        var userOk = Guid.TryParse(sub, out userId);
        return tenantOk && userOk;
    }
}
