using Agro360.Application;
using Agro360.Application.Contracts;
using Agro360.SharedKernel;
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

    [HttpPost("{module}/{id:guid}/{action}"), Authorize(Policy = Permissions.AgricultureWrite)]
    public Task<AgricultureRecord> Transition([FromRoute(Name = "module")] string moduleCode, Guid id, string action, AgricultureCommand? command, CancellationToken cancellationToken) =>
        agriculture.TransitionAsync(moduleCode, id, action, command, cancellationToken);

    [HttpGet("dashboard"), Authorize(Policy = Permissions.AgricultureRead)]
    public Task<AgricultureDashboard> Dashboard(CancellationToken cancellationToken) => agriculture.DashboardAsync(cancellationToken);
}
