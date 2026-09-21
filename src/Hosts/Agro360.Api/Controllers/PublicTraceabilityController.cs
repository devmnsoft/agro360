using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Route("api/public/trace")]
public sealed class PublicTraceabilityController(IPublicTraceabilityService service) : ControllerBase
{
    [AllowAnonymous, HttpGet("{publicCode}")]
    [ResponseCache(Duration = 60, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<PublicTraceDto>> Get(string publicCode, CancellationToken ct) =>
        await service.GetAsync(publicCode, ct) is { } trace ? Ok(trace) : NotFound();

    [Authorize(Policy = Permissions.TraceabilityPublish), HttpPost]
    public async Task<IActionResult> Publish(PublishPublicTraceCommand command, CancellationToken ct)
    {
        var publication = await service.PublishAsync(command, ct);
        return Created($"/api/public/trace/{publication.PublicCode}", publication);
    }

    [Authorize(Policy = Permissions.TraceabilityPublish), HttpPost("{publicCode}/revoke")]
    public async Task<IActionResult> Revoke(string publicCode, RevokePublicTraceCommand command, CancellationToken ct)
    {
        await service.RevokeAsync(publicCode, command, ct);
        return NoContent();
    }
}
