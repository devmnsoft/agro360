using Agro360.Application;using Agro360.Application.Contracts;using Microsoft.AspNetCore.Authorization;using Microsoft.AspNetCore.Mvc;
namespace Agro360.Api.Controllers;
[ApiController,Route("api/production"),Authorize]public sealed class IndustrialProductionController(IIndustrialProductionService service):ControllerBase
{
 [HttpGet("dashboard"),Authorize(Policy=Permissions.ProductionRead)]public async Task<IActionResult> Dashboard(CancellationToken ct)=>Ok(await service.DashboardAsync(ct));
 [HttpGet("lookups/{type}"),Authorize(Policy=Permissions.ProductionRead)]public Task<IReadOnlyList<dynamic>> Lookups(string type,CancellationToken ct)=>service.LookupsAsync(type,ct);
 [HttpGet("recipes"),Authorize(Policy=Permissions.ProductionRead)]public Task<IReadOnlyList<dynamic>> Recipes([FromQuery]ProductionQuery q,CancellationToken ct)=>service.RecipesAsync(q,ct);
 [HttpPost("recipes"),Authorize(Policy=Permissions.ProductionWrite)]public async Task<IActionResult> Recipe(RecipeCommand x,CancellationToken ct)=>Created("api/production/recipes",new{id=await service.CreateRecipeAsync(x,ct)});
 [HttpPost("recipes/{id:guid}/versions"),Authorize(Policy=Permissions.ProductionWrite)]public async Task<IActionResult> Version(Guid id,RecipeCommand x,CancellationToken ct)=>Created($"api/production/recipes/{id}",new{id=await service.VersionRecipeAsync(id,x,ct)});
 [HttpPost("recipe-versions/{id:guid}/approve"),Authorize(Policy=Permissions.ProductionRelease)]public async Task<IActionResult> Approve(Guid id,CancellationToken ct){await service.ApproveRecipeAsync(id,ct);return NoContent();}
 [HttpGet("orders"),Authorize(Policy=Permissions.ProductionRead)]public Task<IReadOnlyList<dynamic>> Orders([FromQuery]ProductionQuery q,CancellationToken ct)=>service.OrdersAsync(q,ct);
 [HttpGet("orders/{id:guid}"),Authorize(Policy=Permissions.ProductionRead)]public async Task<IActionResult> Order(Guid id,CancellationToken ct)=>Ok(await service.OrderAsync(id,ct));
 [HttpPost("orders"),Authorize(Policy=Permissions.ProductionWrite)]public async Task<IActionResult> Order(ProductionOrderCommand x,CancellationToken ct)=>Created("api/production/orders",new{id=await service.CreateOrderAsync(x,ct)});
 [HttpPost("orders/{id:guid}/status"),Authorize(Policy=Permissions.ProductionRelease)]public async Task<IActionResult> Status(Guid id,[FromQuery]string status,[FromBody]string? reason,CancellationToken ct){await service.ChangeStatusAsync(id,status,reason,ct);return NoContent();}
 [HttpPost("records"),Authorize(Policy=Permissions.ProductionOperate)]public async Task<IActionResult> Record(ProductionRecordCommand x,CancellationToken ct)=>Created("api/production/records",new{id=await service.RecordAsync(x,ct)});
 [HttpPost("consumptions"),Authorize(Policy=Permissions.ProductionOperate)]public async Task<IActionResult> Consume(MaterialConsumptionCommand x,CancellationToken ct)=>Created("api/production/consumptions",new{id=await service.ConsumeAsync(x,ct)});
 [HttpPost("quality"),Authorize(Policy=Permissions.ProductionQuality)]public async Task<IActionResult> Quality(QualityDecisionCommand x,CancellationToken ct)=>Created("api/production/quality",new{id=await service.QualityAsync(x,ct)});
 [HttpPost("stoppages"),Authorize(Policy=Permissions.ProductionOperate)]public async Task<IActionResult> Stop(StoppageCommand x,CancellationToken ct)=>Created("api/production/stoppages",new{id=await service.StopAsync(x,ct)});
 [HttpGet("traceability/{batch}"),Authorize(Policy=Permissions.ProductionRead)]public Task<IReadOnlyList<dynamic>> Trace(string batch,CancellationToken ct)=>service.TraceAsync(batch,ct);
 [HttpGet("reports/{report}.csv"),Authorize(Policy=Permissions.ProductionExport)]public async Task<IActionResult> Export(string report,[FromQuery]ProductionQuery q,CancellationToken ct)=>File(await service.ExportAsync(report,q,ct),"text/csv; charset=utf-8",$"producao-{report}-{DateTime.UtcNow:yyyyMMdd}.csv");
}
