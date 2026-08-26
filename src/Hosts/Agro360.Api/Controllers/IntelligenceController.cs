using System.Security.Claims;
using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Agro360.Api.Controllers;

[ApiController,Route("api/intelligence"),Authorize(Policy=Permissions.IntelligenceRead)]
public sealed class IntelligenceController(IIntelligenceService service):ControllerBase
{
 [HttpGet("indicators")] public Task<IReadOnlyList<IndicatorResult>> Indicators([FromQuery]IntelligenceFilter f,CancellationToken ct)=>service.GetIndicatorsAsync(f,ct);
 [HttpGet("reports")] public Task<IReadOnlyList<ReportDefinition>> Reports(CancellationToken ct)=>service.GetReportsAsync(ct);
 [HttpPost("reports/run")] public Task<ReportResult> Run([FromQuery]string id,[FromBody]IntelligenceFilter f,CancellationToken ct)=>service.RunReportAsync(id,f,ct);
 [HttpGet("reports/{id}/export/csv")] public async Task<IActionResult> Csv(string id,[FromQuery]IntelligenceFilter f,CancellationToken ct)=>File(await service.ExportCsvAsync(id,f,ct),"text/csv; charset=utf-8",$"{id}.csv");
 [HttpGet("alerts")] public Task<IReadOnlyList<AlertResult>> Alerts([FromQuery]string? status,CancellationToken ct)=>service.GetAlertsAsync(status,ct);
 [HttpPost("alerts/{id:guid}/{action:regex(^resolve|snooze|ignore$)}"),Authorize(Policy=Permissions.IntelligenceWrite)] public async Task<IActionResult> Alert(Guid id,string action,AlertAction x,CancellationToken ct){await service.ActOnAlertAsync(id,action,x,ct);return NoContent();}
 [HttpGet("executive-dashboard")] public Task<ExecutiveDashboard> Dashboard([FromQuery]IntelligenceFilter f,CancellationToken ct)=>service.GetExecutiveDashboardAsync(f,ct);
 [HttpGet("forecasts")] public Task<IReadOnlyList<ForecastResult>> Forecasts([FromQuery]IntelligenceFilter f,CancellationToken ct)=>service.GetForecastsAsync(f,ct);
 [HttpPost("assistant/query")] public Task<AssistantAnswer> Ask(AssistantQuery q,CancellationToken ct)=>service.AskAsync(q,ct);
 [HttpGet("custom-dashboards")] public Task<IReadOnlyList<CustomDashboard>> Custom(CancellationToken ct)=>service.GetDashboardsAsync(ct);
 [HttpPost("custom-dashboards"),Authorize(Policy=Permissions.IntelligenceWrite)] public async Task<IActionResult> Create(DashboardCommand x,CancellationToken ct){var id=await service.SaveDashboardAsync(null,x,UserId(),ct);return Created($"api/intelligence/custom-dashboards/{id}",new{id});}
 [HttpPut("custom-dashboards/{id:guid}"),Authorize(Policy=Permissions.IntelligenceWrite)] public async Task<IActionResult> Update(Guid id,DashboardCommand x,CancellationToken ct){await service.SaveDashboardAsync(id,x,UserId(),ct);return NoContent();}
 [HttpPost("custom-dashboards/{id:guid}/widgets"),Authorize(Policy=Permissions.IntelligenceWrite)] public async Task<IActionResult> Widget(Guid id,WidgetCommand x,CancellationToken ct){var widgetId=await service.AddWidgetAsync(id,x,ct);return Created($"api/intelligence/custom-dashboards/{id}/widgets/{widgetId}",new{widgetId});}
 [HttpDelete("custom-dashboards/{id:guid}/widgets/{widgetId:guid}"),Authorize(Policy=Permissions.IntelligenceWrite)] public async Task<IActionResult> Delete(Guid id,Guid widgetId,CancellationToken ct){await service.DeleteWidgetAsync(id,widgetId,ct);return NoContent();}
 private Guid UserId()=>Guid.TryParse(User.FindFirstValue("sub"),out var id)?id:throw new UnauthorizedAccessException("Identidade inválida.");
}
