using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Authorize]
public sealed class Livestock360Controller(ILivestock360Service service, ILivestockService animals) : ControllerBase
{
 [HttpGet("api/livestock/animals"),Authorize(Policy=Permissions.LivestockRead)] public Task<PagedResult<AnimalDto>> Animals(Guid? farmId,int page=1,int pageSize=25,string? search=null,CancellationToken ct=default)=>animals.ListAnimalsAsync(farmId,page,pageSize,search,ct);
 [HttpPost("api/livestock/animals"),Authorize(Policy=Permissions.LivestockWrite)] public async Task<IActionResult> Animal(RegisterAnimalCommand x,CancellationToken ct){var a=await animals.RegisterAnimalAsync(x,ct);return Created($"/api/livestock/animals/{a.Id}",a);}
 [HttpGet("api/livestock/animals/{id:guid}"),Authorize(Policy=Permissions.LivestockRead)] public async Task<IActionResult> Animal(Guid id,CancellationToken ct)=>await service.GetAnimalAsync(id,ct) is {} a?Ok(a):NotFound();
 [HttpPut("api/livestock/animals/{id:guid}"),Authorize(Policy=Permissions.LivestockWrite)] public Task<AnimalDto> Animal(Guid id,RegisterAnimalCommand x,CancellationToken ct)=>service.UpdateAnimalAsync(id,x,ct);
 [HttpPost("api/livestock/animals/{id:guid}/death"),Authorize(Policy=Permissions.LivestockWrite)] public async Task<IActionResult> RegisterDeath(Guid id,AnimalStatusCommand x,CancellationToken ct){await service.ChangeAnimalStatusAsync(id,"DEAD",x,ct);return NoContent();}
 [HttpPost("api/livestock/animals/{id:guid}/sale"),Authorize(Policy=Permissions.LivestockWrite)] public async Task<IActionResult> RegisterSale(Guid id,AnimalStatusCommand x,CancellationToken ct){await service.ChangeAnimalStatusAsync(id,"SOLD",x,ct);return NoContent();}
 [HttpPost("api/livestock/animals/{id:guid}/transfer"),Authorize(Policy=Permissions.LivestockWrite)] public async Task<IActionResult> Transfer(Guid id,AnimalTransferCommand x,CancellationToken ct){await service.TransferAnimalAsync(id,x,ct);return NoContent();}
 [HttpGet("api/livestock/herds"),Authorize(Policy=Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Herds(CancellationToken ct)=>service.ListHerdsAsync(ct);
 [HttpPost("api/livestock/herds"),Authorize(Policy=Permissions.LivestockWrite)] public async Task<IActionResult> Herd(HerdCommand x,CancellationToken ct){var id=await service.SaveHerdAsync(null,x,ct);return Created($"/api/livestock/herds/{id}",new{id});}
 [HttpPut("api/livestock/herds/{id:guid}"),Authorize(Policy=Permissions.LivestockWrite)] public async Task<IActionResult> Herd(Guid id,HerdCommand x,CancellationToken ct){await service.SaveHerdAsync(id,x,ct);return NoContent();}
 [HttpGet("api/pastures"),Authorize(Policy=Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Pastures(CancellationToken ct)=>service.ListPasturesAsync(ct);
 [HttpPost("api/pastures"),Authorize(Policy=Permissions.LivestockWrite)] public async Task<IActionResult> Pasture(PastureCommand x,CancellationToken ct){var id=await service.SavePastureAsync(null,x,ct);return Created($"/api/pastures/{id}",new{id});}
 [HttpPut("api/pastures/{id:guid}"),Authorize(Policy=Permissions.LivestockWrite)] public async Task<IActionResult> Pasture(Guid id,PastureCommand x,CancellationToken ct){await service.SavePastureAsync(id,x,ct);return NoContent();}
 [HttpGet("api/pastures/paddocks"),Authorize(Policy=Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Paddocks(CancellationToken ct)=>service.ListPaddocksAsync(ct);
 [HttpPost("api/pastures/paddocks"),Authorize(Policy=Permissions.LivestockWrite)] public async Task<IActionResult> Paddock(PaddockCommand x,CancellationToken ct){var id=await service.SavePaddockAsync(null,x,ct);return Created($"/api/pastures/paddocks/{id}",new{id});}
 [HttpPut("api/pastures/paddocks/{id:guid}"),Authorize(Policy=Permissions.LivestockWrite)] public async Task<IActionResult> Paddock(Guid id,PaddockCommand x,CancellationToken ct){await service.SavePaddockAsync(id,x,ct);return NoContent();}
 [HttpPost("api/pastures/paddocks/{id:guid}/occupy"),Authorize(Policy=Permissions.LivestockWrite)] public async Task<IActionResult> OccupyPaddock(Guid id,PaddockMovementCommand x,CancellationToken ct){await service.MovePaddockAsync(id,true,x,ct);return NoContent();}
 [HttpPost("api/pastures/paddocks/{id:guid}/release"),Authorize(Policy=Permissions.LivestockWrite)] public async Task<IActionResult> ReleasePaddock(Guid id,PaddockMovementCommand x,CancellationToken ct){await service.MovePaddockAsync(id,false,x,ct);return NoContent();}
 [HttpGet("api/livestock/handling-events"),Authorize(Policy=Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Handling(CancellationToken ct)=>service.ListEventsAsync("handling",ct);
 [HttpPost("api/livestock/handling-events"),Authorize(Policy=Permissions.LivestockWrite)] public async Task<IActionResult> Handling(HandlingEventCommand x,CancellationToken ct){var id=await service.AddHandlingAsync(x,ct);return Created($"/api/livestock/handling-events/{id}",new{id});}
 [HttpGet("api/livestock/health-events"),Authorize(Policy=Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Health(CancellationToken ct)=>service.ListEventsAsync("health",ct);
 [HttpPost("api/livestock/health-events"),Authorize(Policy=Permissions.LivestockWrite)] public async Task<IActionResult> Health(HealthEventCommand x,CancellationToken ct){var id=await service.AddHealthAsync(x,ct);return Created($"/api/livestock/health-events/{id}",new{id});}
 [HttpGet("api/livestock/reproduction-events"),Authorize(Policy=Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Reproduction(CancellationToken ct)=>service.ListEventsAsync("reproduction",ct);
 [HttpPost("api/livestock/reproduction-events"),Authorize(Policy=Permissions.LivestockWrite)] public async Task<IActionResult> Reproduction(ReproductionEventCommand x,CancellationToken ct){var id=await service.AddReproductionAsync(x,ct);return Created($"/api/livestock/reproduction-events/{id}",new{id});}
 [HttpGet("api/livestock/nutrition-plans"),Authorize(Policy=Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Plans(CancellationToken ct)=>service.ListNutritionPlansAsync(ct);
 [HttpPost("api/livestock/nutrition-plans"),Authorize(Policy=Permissions.LivestockWrite)] public async Task<IActionResult> Plan(NutritionPlanCommand x,CancellationToken ct){var id=await service.AddNutritionPlanAsync(x,ct);return Created($"/api/livestock/nutrition-plans/{id}",new{id});}
 [HttpGet("api/livestock/feedings"),Authorize(Policy=Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Feedings(CancellationToken ct)=>service.ListFeedingsAsync(ct);
 [HttpPost("api/livestock/feedings"),Authorize(Policy=Permissions.LivestockWrite)] public async Task<IActionResult> Feeding(FeedingCommand x,CancellationToken ct){var id=await service.AddFeedingAsync(x,ct);return Created($"/api/livestock/feedings/{id}",new{id});}
 [HttpGet("api/livestock/production/milk"),Authorize(Policy=Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Milk(CancellationToken ct)=>service.ListMilkAsync(ct);
 [HttpPost("api/livestock/production/milk"),Authorize(Policy=Permissions.LivestockWrite)] public async Task<IActionResult> Milk(MilkProductionCommand x,CancellationToken ct){var id=await service.AddMilkAsync(x,ct);return Created($"/api/livestock/production/milk/{id}",new{id});}
 [HttpGet("api/livestock/production/weight-gain"),Authorize(Policy=Permissions.LivestockRead)] public Task<IReadOnlyList<dynamic>> Gain(CancellationToken ct)=>service.WeightGainAsync(ct);
 [HttpGet("api/livestock/dashboard"),Authorize(Policy=Permissions.DashboardRead)] public Task<LivestockDashboardDto> Dashboard(CancellationToken ct)=>service.DashboardAsync(ct);
}
