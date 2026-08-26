using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController,Route("api/commercial"),Authorize(Policy=Permissions.CommercialRead)]
public sealed class Commercial360Controller(ICommercial360Service service):ControllerBase
{
 [HttpGet("dashboard")]public Task<CommercialDashboard> Dashboard(CancellationToken ct)=>service.DashboardAsync(ct);
 [HttpGet("lookups/{resource}")]public Task<IReadOnlyList<CommercialLookup>> Lookup(string resource,[FromQuery]string? search,CancellationToken ct)=>service.LookupAsync(resource,search,ct);
 [HttpGet("{resource}")]public Task<CommercialPage<CommercialRecord>> List(string resource,[FromQuery]string? search,[FromQuery]string? status,[FromQuery]int page=1,[FromQuery]int pageSize=20,CancellationToken ct=default)=>service.ListAsync(resource,search,status,page,pageSize,ct);
 [HttpPost("customers"),Authorize(Policy=Permissions.CommercialWrite)]public async Task<IActionResult> Customer(CustomerCommand x,CancellationToken ct){var id=await service.SaveCustomerAsync(null,x,ct);return Created($"api/commercial/customers/{id}",new{id});}
 [HttpPut("customers/{id:guid}"),Authorize(Policy=Permissions.CommercialWrite)]public async Task<IActionResult> Customer(Guid id,CustomerCommand x,CancellationToken ct){await service.SaveCustomerAsync(id,x,ct);return NoContent();}
 [HttpPost("opportunities"),Authorize(Policy=Permissions.CommercialWrite)]public async Task<IActionResult> Opportunity(OpportunityCommand x,CancellationToken ct){var id=await service.SaveOpportunityAsync(null,x,ct);return Created($"api/commercial/opportunities/{id}",new{id});}
 [HttpPut("opportunities/{id:guid}"),Authorize(Policy=Permissions.CommercialWrite)]public async Task<IActionResult> Opportunity(Guid id,OpportunityCommand x,CancellationToken ct){await service.SaveOpportunityAsync(id,x,ct);return NoContent();}
 [HttpPost("activities"),Authorize(Policy=Permissions.CommercialWrite)]public async Task<IActionResult> Activity(ActivityCommand x,CancellationToken ct){var id=await service.SaveActivityAsync(null,x,ct);return Created($"api/commercial/activities/{id}",new{id});}
 [HttpPut("activities/{id:guid}"),Authorize(Policy=Permissions.CommercialWrite)]public async Task<IActionResult> Activity(Guid id,ActivityCommand x,CancellationToken ct){await service.SaveActivityAsync(id,x,ct);return NoContent();}
 [HttpPost("orders"),Authorize(Policy=Permissions.CommercialWrite)]public async Task<IActionResult> Order(SalesOrderCommand x,CancellationToken ct){var id=await service.CreateOrderAsync(x,ct);return Created($"api/commercial/orders/{id}",new{id});}
 [HttpPost("orders/{id:guid}/status"),Authorize(Policy=Permissions.CommercialApproveOrder)]public async Task<IActionResult> OrderStatus(Guid id,StatusCommand x,CancellationToken ct){await service.ChangeOrderStatusAsync(id,x,User.HasClaim("permission",Permissions.CommercialOverrideBlock),ct);return NoContent();}
 [HttpPost("commissions/calculate"),Authorize(Policy=Permissions.CommercialManageCommission)]public async Task<IActionResult> Commission(CommissionCommand x,CancellationToken ct){var id=await service.CalculateCommissionAsync(x,ct);return Created($"api/commercial/commissions/{id}",new{id});}
 [HttpPost("commissions/{id:guid}/status"),Authorize(Policy=Permissions.CommercialManageCommission)]public async Task<IActionResult> CommissionStatus(Guid id,StatusCommand x,CancellationToken ct){await service.ChangeCommissionStatusAsync(id,x,ct);return NoContent();}
 [HttpPost("splits"),Authorize(Policy=Permissions.CommercialApproveSplit)]public async Task<IActionResult> Split(SplitAgreementCommand x,CancellationToken ct){var id=await service.SaveSplitAsync(x,ct);return Created($"api/commercial/splits/{id}",new{id});}
 [HttpPost("splits/{id:guid}/status"),Authorize(Policy=Permissions.CommercialApproveSplit)]public async Task<IActionResult> SplitStatus(Guid id,StatusCommand x,CancellationToken ct){await service.ChangeSplitStatusAsync(id,x,ct);return NoContent();}
}
