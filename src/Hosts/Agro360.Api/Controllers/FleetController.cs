using System.Globalization;
using System.Text;
using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Route("api/fleet"), Authorize(Policy = Permissions.FleetRead)]
public sealed class FleetController(IFleetService service, IFleetOperationsService operations, ILogger<FleetController> logger) : ControllerBase
{
    [HttpGet("dashboard")] public Task<FleetDashboard> Dashboard(CancellationToken ct) => service.DashboardAsync(ct);
    [HttpGet("lookups/{kind}")] public Task<IReadOnlyList<FleetLookup>> Lookups(string kind, string? search, CancellationToken ct) => service.LookupsAsync(kind, search, ct);
    [HttpGet("assets")] public Task<IReadOnlyList<FleetAsset>> Assets(string? search, string? status, int page = 1, int pageSize = 25, CancellationToken ct = default) => service.AssetsAsync(search, status, page, pageSize, ct);
    [HttpGet("assets/{id:guid}/detail")] public async Task<IActionResult> AssetDetail(Guid id, CancellationToken ct) => await operations.AssetDetailAsync(id, ct) is { } detail ? Ok(detail) : NotFound();
    [HttpPost("assets"), Authorize(Policy = Permissions.FleetWrite)] public Task<IActionResult> Asset(FleetAssetCommand x, CancellationToken ct) => Created("assets", () => service.SaveAssetAsync(null, x, ct));
    [HttpPut("assets/{id:guid}"), Authorize(Policy = Permissions.FleetWrite)] public async Task<IActionResult> Asset(Guid id, FleetAssetCommand x, CancellationToken ct) { await service.SaveAssetAsync(id, x, ct); return NoContent(); }
    [HttpPost("assets/{id:guid}/release"), Authorize(Policy = Permissions.FleetWrite)] public async Task<IActionResult> Release(Guid id, [FromBody] string reason, CancellationToken ct) { await operations.ReleaseAssetAsync(id, reason, ct); return NoContent(); }

    [HttpPost("operators"), Authorize(Policy = Permissions.FleetWrite)] public Task<IActionResult> Operator(FleetOperatorCommand x, CancellationToken ct) => Created("operators", () => service.CreateOperatorAsync(x, ct));
    [HttpGet("maintenance-plans")] public Task<IReadOnlyList<dynamic>> Plans(Guid? assetId, CancellationToken ct) => operations.ListPlansAsync(assetId, ct);
    [HttpPost("maintenance-plans"), Authorize(Policy = Permissions.MaintenanceWrite)] public Task<IActionResult> Plan(MaintenancePlanCommand x, CancellationToken ct) => Created("maintenance-plans", () => service.CreateMaintenancePlanAsync(x, ct));
    [HttpPost("maintenance-plans/evaluate"), Authorize(Policy = Permissions.MaintenanceWrite)] public async Task<IActionResult> EvaluatePlans(CancellationToken ct) { await operations.EvaluatePlansAsync(ct); return NoContent(); }

    [HttpGet("requests")] public Task<IReadOnlyList<dynamic>> Requests(string? status, CancellationToken ct) => operations.ListRequestsAsync(status, ct);
    [HttpPost("requests"), Authorize(Policy = Permissions.MaintenanceWrite)]
    public async Task<IActionResult> CreateMaintenanceRequest([FromBody] MaintenanceRequestBody body, CancellationToken ct)
    {
        var id = await operations.CreateRequestAsync(body.AssetId, body.DefectClass, body.Severity, body.ProblemDescription, body.BlocksAsset, ct);
        return Created($"/api/fleet/requests/{id}", new { id });
    }

    [HttpGet("work-orders")] public Task<IReadOnlyList<WorkOrder>> Orders(string? search, string? status, int page = 1, int pageSize = 25, CancellationToken ct = default) => service.WorkOrdersAsync(search, status, page, pageSize, ct);
    [HttpGet("work-orders/{id:guid}")] public async Task<IActionResult> OrderDetail(Guid id, CancellationToken ct) => await operations.WorkOrderDetailAsync(id, ct) is { } detail ? Ok(detail) : NotFound();
    [HttpPost("work-orders"), Authorize(Policy = Permissions.MaintenanceWrite)] public Task<IActionResult> Order(WorkOrderCommand x, CancellationToken ct) => Created("work-orders", () => service.OpenWorkOrderAsync(x, ct));
    [HttpPost("work-orders/{id:guid}/transition"), Authorize(Policy = Permissions.MaintenanceWrite)] public async Task<IActionResult> Transition(Guid id, WorkOrderTransitionCommand x, CancellationToken ct) { await service.TransitionWorkOrderAsync(id, x, User.HasClaim("permission", Permissions.FleetMeterOverride), ct); return NoContent(); }
    [HttpPost("work-orders/{id:guid}/parts/reserve"), Authorize(Policy = Permissions.MaintenanceWrite)] public async Task<IActionResult> ReservePart(Guid id, WorkOrderPartCommand x, CancellationToken ct) { await operations.ReservePartAsync(x with { WorkOrderId = id }, ct); return NoContent(); }
    [HttpPost("work-orders/parts/{partId:guid}/consume"), Authorize(Policy = Permissions.MaintenanceWrite)] public async Task<IActionResult> ConsumePart(Guid partId, [FromBody] ConsumeBody body, CancellationToken ct) { await operations.ConsumePartAsync(partId, body.Quantity, body.IdempotencyKey, ct); return NoContent(); }
    [HttpPost("work-orders/parts/{partId:guid}/return"), Authorize(Policy = Permissions.MaintenanceWrite)] public async Task<IActionResult> ReturnPart(Guid partId, PartReturnCommand x, CancellationToken ct) { await operations.ReturnPartAsync(partId, x, ct); return NoContent(); }
    [HttpPost("work-orders/{id:guid}/time-logs"), Authorize(Policy = Permissions.MaintenanceWrite)] public async Task<IActionResult> TimeLog(Guid id, TimeLogCommand x, CancellationToken ct) { await operations.LogTimeAsync(id, x, ct); return NoContent(); }
    [HttpPost("work-orders/{id:guid}/inspect"), Authorize(Policy = Permissions.MaintenanceWrite)] public async Task<IActionResult> Inspect(Guid id, InspectionCommand x, CancellationToken ct) { await operations.InspectAsync(id, x, ct); return NoContent(); }

    [HttpGet("assets/{id:guid}/readings")] public Task<IReadOnlyList<dynamic>> Readings(Guid id, string? meterKind, CancellationToken ct) => operations.ListReadingsAsync(id, meterKind, ct);
    [HttpPost("readings"), Authorize(Policy = Permissions.FleetWrite)] public async Task<IActionResult> Reading(MeterReadingCommand x, CancellationToken ct) { var id = await operations.RecordReadingAsync(x, User.HasClaim("permission", Permissions.FleetMeterOverride), ct); return Created($"/api/fleet/readings/{id}", new { id }); }

    [HttpGet("assets/{id:guid}/availability")] public Task<IReadOnlyList<dynamic>> Availability(Guid id, DateTimeOffset from, DateTimeOffset until, CancellationToken ct) => operations.AvailabilityAsync(id, from, until, ct);
    [HttpPost("reservations"), Authorize(Policy = Permissions.FleetWrite)] public async Task<IActionResult> Reservation(AssetReservationCommand x, CancellationToken ct) { var id = await operations.ReserveAssetAsync(x, ct); return Created($"/api/fleet/reservations/{id}", new { id }); }
    [HttpPost("reservations/{id:guid}/cancel"), Authorize(Policy = Permissions.FleetWrite)] public async Task<IActionResult> ReservationCancel(Guid id, [FromBody] string reason, CancellationToken ct) { await operations.CancelReservationAsync(id, reason, ct); return NoContent(); }

    [HttpGet("refuelings")] public Task<IReadOnlyList<dynamic>> Refuelings(Guid? assetId, CancellationToken ct) => operations.ListRefuelingsAsync(assetId, ct);
    [HttpPost("refuelings"), Authorize(Policy = Permissions.FleetWrite)] public Task<IActionResult> Refuel(RefuelingCommand x, CancellationToken ct) => Created("refuelings", () => service.RefuelAsync(x, User.HasClaim("permission", Permissions.FleetMeterOverride), ct));
    [HttpPost("refuelings/operational"), Authorize(Policy = Permissions.FleetWrite)] public async Task<IActionResult> RefuelOperational(OperationalRefuelCommand x, CancellationToken ct) { var id = await operations.RefuelOperationalAsync(x, User.HasClaim("permission", Permissions.FleetMeterOverride), ct); return Created($"/api/fleet/refuelings/{id}", new { id }); }
    [HttpPost("downtimes"), Authorize(Policy = Permissions.FleetWrite)] public Task<IActionResult> Downtime(DowntimeCommand x, CancellationToken ct) => Created("downtimes", () => service.OpenDowntimeAsync(x, ct));

    [HttpGet("costs")] public Task<IReadOnlyList<dynamic>> Costs(Guid? assetId, DateOnly? from, DateOnly? until, CancellationToken ct) => operations.CostSummaryAsync(assetId, from, until, ct);
    [HttpGet("reports/export")] public async Task<IActionResult> ExportReport(string kind, string? status, Guid? assetId, CancellationToken ct)
    {
        var bytes = await operations.ExportCsvAsync(new FleetExportFilter(kind, status, assetId), ct);
        return File(bytes, "text/csv; charset=utf-8", $"frota-{kind}.csv");
    }
    [HttpGet("reports/assets.csv")] public async Task<IActionResult> AssetsCsv(CancellationToken ct)
    {
        var bytes = await operations.ExportCsvAsync(new FleetExportFilter("assets", null, null), ct);
        return File(bytes, "text/csv; charset=utf-8", "frota-ativos.csv");
    }

    private async Task<IActionResult> Created(string route, Func<Task<Guid>> action)
    {
        try { var id = await action(); return Created($"/api/fleet/{route}/{id}", new { id }); }
        catch (Exception ex) { ApiLogMessages.FleetOperationFailed(logger, route, ex); throw; }
    }
}

public sealed record MaintenanceRequestBody(Guid AssetId, string DefectClass, string Severity, string ProblemDescription, bool BlocksAsset);
public sealed record ConsumeBody(decimal Quantity, string? IdempotencyKey);
