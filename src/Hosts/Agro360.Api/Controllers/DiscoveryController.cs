using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize]
public sealed class DiscoveryController(
    IDashboardService dashboard,
    IGlobalSearchService search,
    ITraceabilityService traceability) : ControllerBase
{
    [HttpGet("dashboard/command-center")]
    [Authorize(Policy = Permissions.DashboardRead)]
    public Task<CommandCenterResult> CommandCenter(
        [FromQuery] Guid? farmId = null,
        CancellationToken cancellationToken = default) =>
        dashboard.GetCommandCenterAsync(farmId, cancellationToken);

    [HttpGet("search")]
    [Authorize(Policy = Permissions.DashboardRead)]
    public Task<IReadOnlyCollection<GlobalSearchItem>> GlobalSearch(
        [FromQuery] string query,
        [FromQuery] int limit = 12,
        CancellationToken cancellationToken = default) =>
        search.SearchAsync(query, limit, cancellationToken);

    [HttpGet("traceability/{entityType}/{entityId:guid}")]
    [Authorize(Policy = Permissions.DashboardRead)]
    public Task<TraceabilityGraph> TraceabilityGraph(
        string entityType,
        Guid entityId,
        [FromQuery] int depth = 4,
        CancellationToken cancellationToken = default) =>
        traceability.GetGraphAsync(entityType, entityId, depth, cancellationToken);
}
