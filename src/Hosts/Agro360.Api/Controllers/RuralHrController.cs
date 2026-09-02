using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController,Route("api/rural-hr"),Authorize(Policy=Permissions.RuralHrRead)]
public sealed class RuralHrController(IRuralHrService service,ILogger<RuralHrController> logger):ControllerBase
{
 private static readonly IReadOnlyDictionary<string,string> Kinds=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){{"people","PERSON"},{"teams","TEAM"},{"time-entries","TIME_ENTRY"},{"allocations","ALLOCATION"},{"labor-costs","LABOR_COST"},{"trainings","TRAINING"},{"ppe","PPE"},{"safety-risks","RISK"},{"incidents","INCIDENT"},{"corrective-actions","CORRECTIVE_ACTION"},{"accommodations","ACCOMMODATION"},{"transport","TRANSPORT"}};
 [HttpGet("{resource}")]public Task<IReadOnlyList<RuralHrRecord>> List(string resource,[FromQuery]string? status,CancellationToken ct)=>service.ListAsync(Kind(resource),status,ct);
 [HttpPost("{resource:regex(^(teams|allocations|labor-costs|trainings|ppe|safety-risks|incidents|corrective-actions|accommodations|transport)$)}"),Authorize(Policy=Permissions.RuralHrWrite)]public async Task<IActionResult> Create(string resource,RuralHrCommand x,CancellationToken ct)=>await Boundary(async()=>{var id=await service.SaveAsync(null,x with{Kind=Kind(resource)},ct);return Created($"{Request.Path}/{id}",new{id});});
 [HttpPut("{resource}/{id:guid}"),Authorize(Policy=Permissions.RuralHrWrite)]public async Task<IActionResult> Update(string resource,Guid id,RuralHrCommand x,CancellationToken ct)=>await Boundary(async()=>{await service.SaveAsync(id,x with{Kind=Kind(resource)},ct);return NoContent();});
 [HttpPost("people"),Authorize(Policy=Permissions.RuralHrWrite)]public async Task<IActionResult> Person(PersonCommand x,CancellationToken ct)=>Created($"api/rural-hr/people/{await service.AddPersonAsync(x,ct)}",null);
 [HttpPost("time-entries/register"),Authorize(Policy=Permissions.RuralHrWrite)]public async Task<IActionResult> Register(TimeEntryCommand x,CancellationToken ct)=>Created($"api/rural-hr/time-entries/{await service.RegisterTimeAsync(x,ct)}",null);
 [HttpPost("time-entries/{id:guid}/end"),Authorize(Policy=Permissions.RuralHrWrite)]public async Task<IActionResult> End(Guid id,[FromBody]DateTimeOffset endedAt,CancellationToken ct){await service.EndTimeAsync(id,endedAt,ct);return NoContent();}
 [HttpPost("transport/schedule"),Authorize(Policy=Permissions.RuralHrWrite)]public async Task<IActionResult> Transport(TransportCommand x,CancellationToken ct)=>Created($"api/rural-hr/transport/{await service.AddTransportAsync(x,ct)}",null);
 [HttpPost("{resource}/{id:guid}/{action}"),Authorize(Policy=Permissions.RuralHrWrite)]public async Task<IActionResult> Action(string resource,Guid id,string action,CancellationToken ct){_ = Kind(resource);var status=action.ToLowerInvariant() switch{"activate"=>"ACTIVE","deactivate"=>"INACTIVE","complete"=>"COMPLETED","deliver"=>"DELIVERED","return"=>"RETURNED","report"=>"OPEN",_=>throw new ArgumentException("Ação inválida.")};await service.ChangeStatusAsync(id,status,ct);return NoContent();}
 [HttpGet("dashboard")]public Task<RuralHrDashboard> Dashboard(CancellationToken ct)=>service.DashboardAsync(ct);
 [HttpGet("{resource}/export")]public async Task<IActionResult> Export(string resource,CancellationToken ct)=>File(await service.ExportAsync(Kind(resource),ct),"text/csv",$"{resource}.csv");
 private static string Kind(string resource)=>Kinds.TryGetValue(resource,out var kind)?kind:throw new KeyNotFoundException("Recurso de RH Rural não encontrado.");
 private async Task<IActionResult> Boundary(Func<Task<IActionResult>> operation){try{return await operation();}catch(Exception ex){ApiLogMessages.RuralHrBoundaryFailed(logger,ex);throw;}}
}
