using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Route("api/sustainability"), Authorize(Policy=Permissions.SustainabilityRead)]
public sealed class SustainabilityController(ISustainabilityService service, ILogger<SustainabilityController> logger) : ControllerBase
{
    [HttpGet("dashboard")]
    public Task<SustainabilityDashboard> Dashboard(CancellationToken ct) => service.DashboardAsync(ct);

    [HttpGet("environmental-compliances")]
    public Task<IReadOnlyList<EnvironmentalComplianceListItem>> Compliances(string? status,CancellationToken ct) => service.CompliancesAsync(status,ct);

    [HttpGet("farms")]
    public Task<IReadOnlyList<SustainabilityFarmOption>> Farms(string? search,CancellationToken ct) => service.FarmsAsync(search,ct);

    [HttpPost("environmental-compliances"), Authorize(Policy=Permissions.SustainabilityWrite)]
    public async Task<IActionResult> CreateCompliance(EnvironmentalComplianceCommand command,CancellationToken ct)
    {
        try { var id=await service.SaveComplianceAsync(command,ct); return Created($"api/sustainability/environmental-compliances/{id}",new{id}); }
        catch(Exception exception) { ApiLogMessages.SustainabilityRegistrationFailed(logger,exception); throw; }
    }

    [HttpGet("reports/{report}.csv"), Authorize(Policy=Permissions.SustainabilityReports)]
    public async Task<IActionResult> Report(string report,CancellationToken ct) => File(await service.ExportCsvAsync(report,ct),"text/csv; charset=utf-8",$"esg-{report}.csv");
}
