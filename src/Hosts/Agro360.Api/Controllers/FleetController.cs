using System.Globalization;
using System.Text;
using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController,Route("api/fleet"),Authorize(Policy=Permissions.FleetRead)]
public sealed class FleetController(IFleetService service,ILogger<FleetController> logger):ControllerBase
{
 [HttpGet("dashboard")] public Task<FleetDashboard> Dashboard(CancellationToken ct)=>service.DashboardAsync(ct);
 [HttpGet("lookups/{kind}")] public Task<IReadOnlyList<FleetLookup>> Lookups(string kind,string? search,CancellationToken ct)=>service.LookupsAsync(kind,search,ct);
 [HttpGet("assets")] public Task<IReadOnlyList<FleetAsset>> Assets(string? search,string? status,int page=1,int pageSize=25,CancellationToken ct=default)=>service.AssetsAsync(search,status,page,pageSize,ct);
 [HttpPost("assets"),Authorize(Policy=Permissions.FleetWrite)] public Task<IActionResult> Asset(FleetAssetCommand x,CancellationToken ct)=>Created("assets",()=>service.SaveAssetAsync(null,x,ct));
 [HttpPut("assets/{id:guid}"),Authorize(Policy=Permissions.FleetWrite)] public async Task<IActionResult> Asset(Guid id,FleetAssetCommand x,CancellationToken ct){await service.SaveAssetAsync(id,x,ct);return NoContent();}
 [HttpPost("operators"),Authorize(Policy=Permissions.FleetWrite)] public Task<IActionResult> Operator(FleetOperatorCommand x,CancellationToken ct)=>Created("operators",()=>service.CreateOperatorAsync(x,ct));
 [HttpPost("maintenance-plans"),Authorize(Policy=Permissions.MaintenanceWrite)] public Task<IActionResult> Plan(MaintenancePlanCommand x,CancellationToken ct)=>Created("maintenance-plans",()=>service.CreateMaintenancePlanAsync(x,ct));
 [HttpGet("work-orders")] public Task<IReadOnlyList<WorkOrder>> Orders(string? search,string? status,int page=1,int pageSize=25,CancellationToken ct=default)=>service.WorkOrdersAsync(search,status,page,pageSize,ct);
 [HttpPost("work-orders"),Authorize(Policy=Permissions.MaintenanceWrite)] public Task<IActionResult> Order(WorkOrderCommand x,CancellationToken ct)=>Created("work-orders",()=>service.OpenWorkOrderAsync(x,ct));
 [HttpPost("work-orders/{id:guid}/transition"),Authorize(Policy=Permissions.MaintenanceWrite)] public async Task<IActionResult> Transition(Guid id,WorkOrderTransitionCommand x,CancellationToken ct){await service.TransitionWorkOrderAsync(id,x,User.HasClaim("permission",Permissions.FleetMeterOverride),ct);return NoContent();}
 [HttpPost("refuelings"),Authorize(Policy=Permissions.FleetWrite)] public Task<IActionResult> Refuel(RefuelingCommand x,CancellationToken ct)=>Created("refuelings",()=>service.RefuelAsync(x,User.HasClaim("permission",Permissions.FleetMeterOverride),ct));
 [HttpPost("downtimes"),Authorize(Policy=Permissions.FleetWrite)] public Task<IActionResult> Downtime(DowntimeCommand x,CancellationToken ct)=>Created("downtimes",()=>service.OpenDowntimeAsync(x,ct));
 [HttpGet("reports/assets.csv")] public async Task<IActionResult> Csv(CancellationToken ct){var rows=await service.AssetsAsync(null,null,1,10000,ct);var csv=new StringBuilder("codigo;nome;tipo;status;placa;odometro;horimetro\n");foreach(var x in rows)csv.AppendLine(CultureInfo.InvariantCulture,$"{Cell(x.InternalCode)};{Cell(x.Name)};{Cell(x.Type)};{x.Status};{Cell(x.Plate??"")};{x.Odometer};{x.HourMeter}");return File(Encoding.UTF8.GetBytes(csv.ToString()),"text/csv; charset=utf-8","frota-ativos.csv");}
 private async Task<IActionResult> Created(string route,Func<Task<Guid>> action){try{var id=await action();return Created($"/api/fleet/{route}/{id}",new{id});}catch(Exception ex){ApiLogMessages.FleetOperationFailed(logger,route,ex);throw;}}
 private static string Cell(string value)=>$"\"{value.Replace("\"","\"\"")}\"";
}
