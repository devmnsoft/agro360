using Agro360.Application; using Agro360.Application.Contracts; using Microsoft.AspNetCore.Authorization; using Microsoft.AspNetCore.Mvc;
namespace Agro360.Api.Controllers;
[ApiController,Route("api/logistics/trips"),Authorize]
public sealed class LogisticsController(ILogisticsService service):ControllerBase
{
 [HttpGet,Authorize(Policy=Permissions.LogisticsRead)] public Task<IReadOnlyList<dynamic>> List(CancellationToken ct)=>service.ListAsync(ct);
 [HttpPost,Authorize(Policy=Permissions.LogisticsWrite)] public async Task<IActionResult> Add(TripCommand x,CancellationToken ct)=>Created("api/logistics/trips",new{id=await service.SaveAsync(null,x,ct)});
 [HttpPut("{id:guid}"),Authorize(Policy=Permissions.LogisticsWrite)] public async Task<IActionResult> Edit(Guid id,TripCommand x,CancellationToken ct){await service.SaveAsync(id,x,ct);return NoContent();}
 [HttpPost("{id:guid}/occurrences"),Authorize(Policy=Permissions.LogisticsWrite)] public async Task<IActionResult> Occurrence(Guid id,TripOccurrenceCommand x,CancellationToken ct){await service.AddOccurrenceAsync(id,x,ct);return NoContent();}
 [HttpPost("{id:guid}/complete"),Authorize(Policy=Permissions.LogisticsWrite)] public async Task<IActionResult> Complete(Guid id,CancellationToken ct){await service.CompleteAsync(id,ct);return NoContent();}
}
