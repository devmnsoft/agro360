using System.Text;
using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController,Route("api/sst"),Authorize(Policy=Permissions.SstRead)]
public sealed class SstController(ISstService service,ILogger<SstController> logger):ControllerBase
{
 [HttpGet("dashboard")] public Task<SstDashboard> Dashboard(CancellationToken ct)=>service.DashboardAsync(ct);
 [HttpGet("lookups/{kind}")] public Task<IReadOnlyList<SstLookup>> Lookups(string kind,string? search,CancellationToken ct)=>service.LookupsAsync(kind,search,ct);
 [HttpGet("workers")] public Task<IReadOnlyList<SstWorker>> Workers(string? search,string? status,int page=1,int pageSize=25,CancellationToken ct=default)=>service.WorkersAsync(search,status,page,pageSize,ct);
 [HttpPost("workers"),Authorize(Policy=Permissions.SstWrite)] public Task<IActionResult> Worker(SstWorkerCommand x,CancellationToken ct)=>Created("workers",()=>service.CreateWorkerAsync(x,ct));
 [HttpGet("risks")] public Task<IReadOnlyList<SstRisk>> Risks(string? search,string? status,int page=1,int pageSize=25,CancellationToken ct=default)=>service.RisksAsync(search,status,page,pageSize,ct);
 [HttpPost("risks"),Authorize(Policy=Permissions.SstWrite)] public Task<IActionResult> Risk(SstRiskCommand x,CancellationToken ct)=>Created("risks",()=>service.CreateRiskAsync(x,ct));
 [HttpPost("epi-deliveries"),Authorize(Policy=Permissions.SstWrite)] public Task<IActionResult> Delivery(EpiDeliveryCommand x,CancellationToken ct)=>Created("epi-deliveries",()=>service.DeliverEpiAsync(x,ct));
 [HttpGet("incidents")] public Task<IReadOnlyList<SstIncident>> Incidents(string? search,string? status,int page=1,int pageSize=25,CancellationToken ct=default)=>service.IncidentsAsync(search,status,page,pageSize,ct);
 [HttpPost("incidents"),Authorize(Policy=Permissions.SstWrite)] public Task<IActionResult> Incident(SstIncidentCommand x,CancellationToken ct)=>Created("incidents",()=>service.CreateIncidentAsync(x,ct));
 [HttpPost("checklists/runs"),Authorize(Policy=Permissions.SstWrite)] public Task<IActionResult> Checklist(SstChecklistRunCommand x,CancellationToken ct)=>Created("checklists/runs",()=>service.ExecuteChecklistAsync(x,ct));
 [HttpGet("risks.csv")] public async Task<IActionResult> RisksCsv(CancellationToken ct){var rows=await service.RisksAsync(null,null,1,10000,ct);var csv=new StringBuilder("nome;tipo;area;severidade;probabilidade;nivel;status\n");foreach(var x in rows)csv.AppendLine($"{Cell(x.Name)};{Cell(x.Type)};{Cell(x.AreaName)};{x.Severity};{x.Probability};{x.Level};{x.Status}");return File(Encoding.UTF8.GetBytes(csv.ToString()),"text/csv; charset=utf-8","riscos-sst.csv");}
 private async Task<IActionResult> Created(string route,Func<Task<Guid>> action){try{var id=await action();return Created($"/api/sst/{route}/{id}",new{id});}catch(Exception ex){ApiLogMessages.SstOperationFailed(logger,route,ex);throw;}}
 private static string Cell(string value)=>$"\"{value.Replace("\"","\"\"")}\"";
}
