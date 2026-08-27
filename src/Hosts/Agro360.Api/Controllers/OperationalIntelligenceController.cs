using System.Security.Claims;
using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Route("api/intelligence/operations"), Authorize(Policy = Permissions.IntelligenceRead)]
public sealed class OperationalIntelligenceController(IOperationalIntelligenceService service) : ControllerBase
{
    [HttpGet("dashboard")] public Task<IntelligenceDashboardSummary> Dashboard(CancellationToken ct) => service.DashboardAsync(ct);
    [HttpGet("recommendations")] public Task<IntelligencePage<RecommendationSummary>> Recommendations([FromQuery] IntelligencePageFilter filter, CancellationToken ct) => service.RecommendationsAsync(filter, ct);
    [HttpGet("recommendations/{id:guid}")] public Task<RecommendationDetail> Recommendation(Guid id, CancellationToken ct) => service.RecommendationAsync(id, ct);
    [HttpPost("recommendations/{id:guid}/decision"), Authorize(Policy = Permissions.IntelligenceWrite)] public async Task<IActionResult> Decide(Guid id, RecommendationDecision decision, CancellationToken ct) { await service.DecideRecommendationAsync(id, decision, UserId(), ct); return NoContent(); }
    [HttpGet("scores")] public Task<IntelligencePage<RiskScoreSummary>> Scores([FromQuery] IntelligencePageFilter filter, CancellationToken ct) => service.ScoresAsync(filter, ct);
    [HttpGet("scores/{id:guid}")] public Task<RiskScoreDetail> Score(Guid id, CancellationToken ct) => service.ScoreAsync(id, ct);
    [HttpGet("anomalies")] public Task<IntelligencePage<AnomalySummary>> Anomalies([FromQuery] IntelligencePageFilter filter, CancellationToken ct) => service.AnomaliesAsync(filter, ct);
    [HttpPost("anomalies/{id:guid}/archive"), Authorize(Policy = Permissions.IntelligenceWrite)] public async Task<IActionResult> Archive(Guid id, [FromBody] ArchiveRequest request, CancellationToken ct) { await service.ArchiveAnomalyAsync(id, request.Reason, UserId(), ct); return NoContent(); }
    [HttpGet("priorities")] public Task<IReadOnlyList<PrioritySummary>> Priorities(CancellationToken ct) => service.PrioritiesAsync(ct);
    [HttpGet("rules")] public Task<IReadOnlyList<IntelligenceRuleSummary>> Rules(CancellationToken ct) => service.RulesAsync(ct);
    [HttpPost("rules"), Authorize(Policy = Permissions.IntelligenceWrite)] public async Task<IActionResult> CreateRule(IntelligenceRuleCommand command, CancellationToken ct) { var id = await service.SaveRuleAsync(null, command, UserId(), ct); return Created($"api/intelligence/operations/rules/{id}", new { id }); }
    [HttpPut("rules/{id:guid}"), Authorize(Policy = Permissions.IntelligenceWrite)] public async Task<IActionResult> UpdateRule(Guid id, IntelligenceRuleCommand command, CancellationToken ct) { await service.SaveRuleAsync(id, command, UserId(), ct); return NoContent(); }
    private Guid UserId() => Guid.TryParse(User.FindFirstValue("sub"), out var id) ? id : throw new UnauthorizedAccessException("Identidade inválida.");
}

public sealed record ArchiveRequest(string? Reason);
