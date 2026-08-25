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
}
