using Agro360.Application;
using Agro360.Application.Contracts;
using Agro360.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Route("api/v1/inventory/material-requests"), Authorize]
public sealed class MaterialRequestsController(IMaterialRequestService service) : ControllerBase
{
    [HttpGet, Authorize(Policy=Permissions.InventoryRead)] public Task<PagedResult<MaterialRequestListDto>> List(int page=1,int pageSize=25,string? search=null,string? status=null,CancellationToken ct=default)=>service.ListAsync(page,pageSize,search,status,ct);
    [HttpGet("{id:guid}"), Authorize(Policy=Permissions.InventoryRead)] public Task<MaterialRequestDetailDto> Get(Guid id,CancellationToken ct)=>service.GetAsync(id,ct);
    [HttpGet("lookups/{type}"), Authorize(Policy=Permissions.InventoryRead)] public Task<IReadOnlyCollection<InventoryLookupDto>> Lookups(string type,CancellationToken ct)=>service.LookupsAsync(type,ct);
    [HttpPost, Authorize(Policy=Permissions.InventoryMove)] public async Task<IActionResult> Create(CreateMaterialRequestCommand command,CancellationToken ct){var id=await service.CreateAsync(command,ct);return Created($"api/v1/inventory/material-requests/{id}",new{id});}
    [HttpPost("{id:guid}/decision"), Authorize(Policy=Permissions.InventoryAdjust)] public async Task<IActionResult> Decide(Guid id,MaterialRequestDecisionCommand command,CancellationToken ct){await service.DecideAsync(id,command,ct);return NoContent();}
    [HttpPost("{id:guid}/reservations"), Authorize(Policy=Permissions.InventoryMove)] public async Task<IActionResult> Reserve(Guid id,MaterialReservationCommand command,CancellationToken ct){await service.ReserveAsync(id,command,ct);return NoContent();}
    [HttpPost("{id:guid}/deliveries"), Authorize(Policy=Permissions.InventoryMove)] public async Task<IActionResult> Deliver(Guid id,MaterialDeliveryCommand command,CancellationToken ct)=>Created("deliveries",new{id=await service.DeliverAsync(id,command,ct)});
    [HttpPost("{id:guid}/consumptions"), Authorize(Policy=Permissions.InventoryMove)] public async Task<IActionResult> Consume(Guid id,MaterialConsumptionCommand command,CancellationToken ct)=>Created("consumptions",new{id=await service.ConsumeAsync(id,command,ct)});
    [HttpPost("{id:guid}/returns"), Authorize(Policy=Permissions.InventoryMove)] public async Task<IActionResult> Return(Guid id,MaterialReturnCommand command,CancellationToken ct)=>Created("returns",new{id=await service.ReturnAsync(id,command,ct)});
    [HttpPost("{id:guid}/cancel"), Authorize(Policy=Permissions.InventoryAdjust)] public async Task<IActionResult> Cancel(Guid id,MaterialRequestActionCommand command,CancellationToken ct){await service.CancelAsync(id,command,ct);return NoContent();}
}
