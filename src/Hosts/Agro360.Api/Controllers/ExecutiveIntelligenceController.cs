using System.Security.Claims;
using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Route("api/intelligence360"), Authorize(Policy = Permissions.IntelligenceRead)]
public sealed class ExecutiveIntelligenceController(IExecutiveIntelligenceService service) : ControllerBase
{
    [HttpGet("panel")] public Task<ExecutivePanel> Panel(CancellationToken ct) => service.GetPanelAsync(ct);
    [HttpPost("indicators"), Authorize(Policy = Permissions.IntelligenceWrite)] public async Task<IActionResult> Create(KpiDefinitionCommand command, CancellationToken ct) { var id = await service.CreateKpiAsync(command, UserId(), User.HasClaim("permission", Permissions.IntelligenceStrategic), ct); return Created($"api/intelligence360/indicators/{id}", new { id }); }
    [HttpPost("indicators/{id:guid}/recalculate"), Authorize(Policy = Permissions.IntelligenceWrite)] public async Task<IActionResult> Recalculate(Guid id, CancellationToken ct) { await service.RecalculateAsync(id, UserId(), ct); return NoContent(); }
    [HttpPost("alerts/{id:guid}/decision"), Authorize(Policy = Permissions.IntelligenceWrite)] public async Task<IActionResult> Alert(Guid id, AlertDecisionCommand command, CancellationToken ct) { await service.DecideAlertAsync(id, command, UserId(), ct); return NoContent(); }
    [HttpPost("recommendations/{id:guid}/decision"), Authorize(Policy = Permissions.IntelligenceWrite)] public async Task<IActionResult> Recommendation(Guid id, RecommendationStatusCommand command, CancellationToken ct) { await service.DecideRecommendationAsync(id, command, UserId(), ct); return NoContent(); }
    [HttpGet("reports/{report}.csv"), Authorize(Policy = Permissions.IntelligenceExport)] public async Task<IActionResult> Export(string report, [FromQuery] IntelligencePageFilter filter, CancellationToken ct) => File(await service.ExportAsync(report, filter, UserId(), ct), "text/csv; charset=utf-8", $"agro360-{report}.csv");
    private Guid UserId() => Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new UnauthorizedAccessException("Usuário inválido.");
}
