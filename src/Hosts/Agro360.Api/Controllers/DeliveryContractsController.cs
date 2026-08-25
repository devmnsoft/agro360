using Agro360.Application; using Agro360.Application.Contracts; using Microsoft.AspNetCore.Authorization; using Microsoft.AspNetCore.Mvc;
namespace Agro360.Api.Controllers;
[ApiController,Route("api/commercial/delivery-contracts"),Authorize]
public sealed class DeliveryContractsController(IDeliveryContractService service):ControllerBase
{
 [HttpGet,Authorize(Policy=Permissions.StorageRead)] public Task<IReadOnlyList<dynamic>> List(CancellationToken ct)=>service.ListAsync(ct);
 [HttpPost,Authorize(Policy=Permissions.CommercialWrite)] public async Task<IActionResult> Add(DeliveryContractCommand x,CancellationToken ct)=>Created("api/commercial/delivery-contracts",new{id=await service.SaveAsync(null,x,ct)});
 [HttpPut("{id:guid}"),Authorize(Policy=Permissions.CommercialWrite)] public async Task<IActionResult> Edit(Guid id,DeliveryContractCommand x,CancellationToken ct){await service.SaveAsync(id,x,ct);return NoContent();}
}
