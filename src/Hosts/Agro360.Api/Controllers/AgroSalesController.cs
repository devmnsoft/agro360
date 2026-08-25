using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Agro360.Api.Controllers;
[ApiController,Route("api/commercial/sales"),Authorize]
public sealed class AgroSalesController(ISalesService sales):ControllerBase
{
 [HttpGet,Authorize(Policy=Permissions.FinanceRead)] public Task<IReadOnlyList<dynamic>> List(CancellationToken ct)=>sales.ListAsync(ct);
 [HttpGet("{id:guid}"),Authorize(Policy=Permissions.FinanceRead)] public async Task<IActionResult> Get(Guid id,CancellationToken ct)=>Ok(await sales.GetAsync(id,ct));
 [HttpPost,Authorize(Policy=Permissions.CommercialWrite)] public async Task<IActionResult> Add(SaleCommand x,CancellationToken ct)=>Created("api/commercial/sales",new{id=await sales.SaveAsync(null,x,ct)});
 [HttpPut("{id:guid}"),Authorize(Policy=Permissions.CommercialWrite)] public async Task<IActionResult> Edit(Guid id,SaleCommand x,CancellationToken ct){await sales.SaveAsync(id,x,ct);return NoContent();}
 [HttpPost("{id:guid}/confirm"),Authorize(Policy=Permissions.CommercialWrite)] public async Task<IActionResult> Confirm(Guid id,CancellationToken ct){await sales.ConfirmAsync(id,ct);return NoContent();}
 [HttpPost("{id:guid}/invoice"),Authorize(Policy=Permissions.CommercialWrite)] public async Task<IActionResult> Invoice(Guid id,CancellationToken ct){await sales.InvoiceAsync(id,ct);return NoContent();}
 [HttpPost("{id:guid}/cancel"),Authorize(Policy=Permissions.CommercialWrite)] public async Task<IActionResult> Cancel(Guid id,CancelFinanceCommand x,CancellationToken ct){await sales.CancelAsync(id,x.Reason,ct);return NoContent();}
}
