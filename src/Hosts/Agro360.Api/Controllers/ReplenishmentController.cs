using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Agro360.Api.Controllers;

[ApiController, Route("api/replenishment"), Authorize]
public sealed class ReplenishmentController(IReplenishmentService service) : ControllerBase
{
    [HttpGet("options"), Authorize(Policy=Permissions.InventoryRead)] public Task<dynamic> Options(CancellationToken ct) => service.OptionsAsync(ct);
    [HttpGet("policies"), Authorize(Policy=Permissions.InventoryRead)] public Task<IReadOnlyList<dynamic>> Policies([FromQuery] MaterialNeedQuery q, CancellationToken ct) => service.PoliciesAsync(q,ct);
    [HttpPost("policies"), Authorize(Policy=Permissions.InventoryAdjust)] public async Task<IActionResult> Policy(ReplenishmentPolicyCommand x,CancellationToken ct)=>Created("api/replenishment/policies",new{id=await service.SavePolicyAsync(null,x,ct)});
    [HttpPut("policies/{id:guid}"), Authorize(Policy=Permissions.InventoryAdjust)] public async Task<IActionResult> Policy(Guid id,ReplenishmentPolicyCommand x,CancellationToken ct){await service.SavePolicyAsync(id,x,ct);return NoContent();}
    [HttpGet("needs"), Authorize(Policy=Permissions.InventoryRead)] public Task<dynamic> Needs([FromQuery] MaterialNeedQuery q,CancellationToken ct)=>service.NeedsAsync(q,ct);
    [HttpPost("needs/analyze"), Authorize(Policy=Permissions.InventoryRead)] public async Task<IActionResult> Analyze(AnalyzeMaterialNeedCommand x,CancellationToken ct)=>Created("api/replenishment/needs",new{id=await service.AnalyzeAsync(x,ct)});
    [HttpGet("needs/{id:guid}"), Authorize(Policy=Permissions.InventoryRead)] public Task<dynamic> Detail(Guid id,CancellationToken ct)=>service.DetailAsync(id,ct);
    [HttpPost("needs/{id:guid}/purchase"), Authorize(Policy=Permissions.PurchasingRequest)] public async Task<IActionResult> Purchase(Guid id,ConfirmMaterialNeedCommand x,CancellationToken ct)=>Created("api/procurement/requisitions",new{id=await service.ConfirmPurchaseAsync(id,x,ct)});
    [HttpPost("needs/{id:guid}/dismiss"), Authorize(Policy=Permissions.InventoryAdjust)] public async Task<IActionResult> Dismiss(Guid id,[FromQuery] long version,[FromBody] string reason,CancellationToken ct){await service.DismissAsync(id,version,reason,ct);return NoContent();}
}
