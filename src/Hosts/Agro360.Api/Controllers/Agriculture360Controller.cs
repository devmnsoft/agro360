using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Route("api/agriculture"), Authorize]
public sealed class Agriculture360Controller(IAgriculture360Service agriculture) : ControllerBase
{
    [HttpGet("{module}"), Authorize(Policy = Permissions.AgricultureRead)]
    public Task<PagedResult<AgricultureRecord>> List([FromRoute(Name = "module")] string moduleCode, [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25, CancellationToken cancellationToken = default) =>
        agriculture.ListAsync(moduleCode, page, pageSize, cancellationToken);

    [HttpPost("{module}"), Authorize(Policy = Permissions.AgricultureWrite)]
    public async Task<IActionResult> Create([FromRoute(Name = "module")] string moduleCode, AgricultureCommand command, CancellationToken cancellationToken)
    {
        var result = await agriculture.CreateAsync(moduleCode, command, cancellationToken);
        return Created($"/api/agriculture/{moduleCode}/{result.Id}", result);
    }

    [HttpPut("{module}/{id:guid}"), Authorize(Policy = Permissions.AgricultureWrite)]
    public Task<AgricultureRecord> Update([FromRoute(Name = "module")] string moduleCode, Guid id, AgricultureCommand command, CancellationToken cancellationToken) =>
        agriculture.UpdateAsync(moduleCode, id, command, cancellationToken);

    [HttpPost("{module}/{id:guid}/approve"), Authorize(Policy = Permissions.AgricultureWrite)]
    public Task<AgricultureRecord> Approve([FromRoute(Name = "module")] string moduleCode, Guid id, AgricultureCommand? command, CancellationToken cancellationToken) =>
        agriculture.TransitionAsync(moduleCode, id, "approve", command, cancellationToken);

    [HttpPost("{module}/{id:guid}/revise"), Authorize(Policy = Permissions.AgricultureWrite)]
    public Task<AgricultureRecord> Revise([FromRoute(Name = "module")] string moduleCode, Guid id, AgricultureCommand? command, CancellationToken cancellationToken) =>
        agriculture.TransitionAsync(moduleCode, id, "revise", command, cancellationToken);

    [HttpPost("{module}/{id:guid}/start"), Authorize(Policy = Permissions.AgricultureWrite)]
    public Task<AgricultureRecord> Start([FromRoute(Name = "module")] string moduleCode, Guid id, AgricultureCommand? command, CancellationToken cancellationToken) =>
        agriculture.TransitionAsync(moduleCode, id, "start", command, cancellationToken);

    [HttpPost("{module}/{id:guid}/pause"), Authorize(Policy = Permissions.AgricultureWrite)]
    public Task<AgricultureRecord> Pause([FromRoute(Name = "module")] string moduleCode, Guid id, AgricultureCommand? command, CancellationToken cancellationToken) =>
        agriculture.TransitionAsync(moduleCode, id, "pause", command, cancellationToken);

    [HttpPost("{module}/{id:guid}/complete"), Authorize(Policy = Permissions.AgricultureWrite)]
    public Task<AgricultureRecord> Complete([FromRoute(Name = "module")] string moduleCode, Guid id, AgricultureCommand? command, CancellationToken cancellationToken) =>
        agriculture.TransitionAsync(moduleCode, id, "complete", command, cancellationToken);

    [HttpPost("{module}/{id:guid}/cancel"), Authorize(Policy = Permissions.AgricultureWrite)]
    public Task<AgricultureRecord> Cancel([FromRoute(Name = "module")] string moduleCode, Guid id, AgricultureCommand? command, CancellationToken cancellationToken) =>
        agriculture.TransitionAsync(moduleCode, id, "cancel", command, cancellationToken);

    [HttpGet("dashboard"), Authorize(Policy = Permissions.AgricultureRead)]
    public Task<AgricultureDashboard> Dashboard(CancellationToken cancellationToken) => agriculture.DashboardAsync(cancellationToken);
}
