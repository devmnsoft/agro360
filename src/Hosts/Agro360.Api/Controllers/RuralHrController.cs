using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Route("api/rural-hr"), Authorize(Policy = Permissions.RuralHrRead)]
public sealed class RuralHrController(IRuralHrService service, ILogger<RuralHrController> logger) : ControllerBase
{
    private static readonly Dictionary<string, string> Kinds = new(StringComparer.OrdinalIgnoreCase) { { "people", "PERSON" }, { "teams", "TEAM" }, { "time-entries", "TIME_ENTRY" }, { "allocations", "ALLOCATION" }, { "labor-costs", "LABOR_COST" }, { "trainings", "TRAINING" }, { "ppe", "PPE" }, { "safety-risks", "RISK" }, { "incidents", "INCIDENT" }, { "corrective-actions", "CORRECTIVE_ACTION" }, { "accommodations", "ACCOMMODATION" }, { "transport", "TRANSPORT" } };
    [HttpGet("{resource}")] public Task<IReadOnlyList<RuralHrRecord>> List(string resource, [FromQuery] string? status, CancellationToken ct) => service.ListAsync(Kind(resource), status, ct);
    [HttpPost("{resource:regex(^(teams|allocations|labor-costs|trainings|ppe|safety-risks|incidents|corrective-actions|accommodations|transport)$)}"), Authorize(Policy = Permissions.RuralHrWrite)] public async Task<IActionResult> Create(string resource, RuralHrCommand x, CancellationToken ct) => await Boundary(async () => { var id = await service.SaveAsync(null, x with { Kind = Kind(resource) }, ct); return Created($"{Request.Path}/{id}", new { id }); });
    [HttpPut("{resource}/{id:guid}"), Authorize(Policy = Permissions.RuralHrWrite)] public async Task<IActionResult> Update(string resource, Guid id, RuralHrCommand x, CancellationToken ct) => await Boundary(async () => { await service.SaveAsync(id, x with { Kind = Kind(resource) }, ct); return NoContent(); });
    [HttpPost("people"), Authorize(Policy = Permissions.RuralHrWrite)] public async Task<IActionResult> Person(PersonCommand x, CancellationToken ct) => Created($"api/rural-hr/people/{await service.AddPersonAsync(x, ct)}", null);
    [HttpPost("time-entries/register"), Authorize(Policy = Permissions.RuralHrWrite)] public async Task<IActionResult> Register(TimeEntryCommand x, CancellationToken ct) => Created($"api/rural-hr/time-entries/{await service.RegisterTimeAsync(x, ct)}", null);
    [HttpPost("time-entries/{id:guid}/end"), Authorize(Policy = Permissions.RuralHrWrite)] public async Task<IActionResult> End(Guid id, [FromBody] DateTimeOffset endedAt, CancellationToken ct) { await service.EndTimeAsync(id, endedAt, ct); return NoContent(); }
    [HttpPost("transport/schedule"), Authorize(Policy = Permissions.RuralHrWrite)] public async Task<IActionResult> Transport(TransportCommand x, CancellationToken ct) => Created($"api/rural-hr/transport/{await service.AddTransportAsync(x, ct)}", null);
    [HttpGet("lookups/{kind}")] public Task<IReadOnlyList<LookupOption>> Lookups(string kind, CancellationToken ct) => service.LookupAsync(kind, ct);
    [HttpPost("{resource}/{id:guid}/activate"), Authorize(Policy = Permissions.RuralHrWrite)] public Task<IActionResult> Activate(string resource, Guid id, CancellationToken ct) => ChangeStatus(resource, id, "ACTIVE", ct);
    [HttpPost("{resource}/{id:guid}/deactivate"), Authorize(Policy = Permissions.RuralHrWrite)] public Task<IActionResult> Deactivate(string resource, Guid id, CancellationToken ct) => ChangeStatus(resource, id, "INACTIVE", ct);
    [HttpPost("{resource}/{id:guid}/dispatch"), Authorize(Policy = Permissions.RuralHrWrite)] public Task<IActionResult> Dispatch(string resource, Guid id, CancellationToken ct) => ChangeStatus(resource, id, "IN_FIELD", ct);
    [HttpPost("{resource}/{id:guid}/start"), Authorize(Policy = Permissions.RuralHrWrite)] public Task<IActionResult> Start(string resource, Guid id, CancellationToken ct) => ChangeStatus(resource, id, Kind(resource) switch { "CORRECTIVE_ACTION" => "IN_PROGRESS", "TRANSPORT" => "IN_TRANSIT", _ => "ACTIVE" }, ct);
    [HttpPost("{resource}/{id:guid}/investigate"), Authorize(Policy = Permissions.RuralHrWrite)] public Task<IActionResult> Investigate(string resource, Guid id, CancellationToken ct) => ChangeStatus(resource, id, "INVESTIGATING", ct);
    [HttpPost("{resource}/{id:guid}/complete"), Authorize(Policy = Permissions.RuralHrWrite)] public Task<IActionResult> Complete(string resource, Guid id, CancellationToken ct) => ChangeStatus(resource, id, "COMPLETED", ct);
    [HttpPost("{resource}/{id:guid}/close"), Authorize(Policy = Permissions.RuralHrWrite)] public Task<IActionResult> Close(string resource, Guid id, CancellationToken ct) => ChangeStatus(resource, id, "CLOSED", ct);
    [HttpPost("{resource}/{id:guid}/cancel"), Authorize(Policy = Permissions.RuralHrWrite)] public Task<IActionResult> Cancel(string resource, Guid id, CancellationToken ct) => ChangeStatus(resource, id, "CANCELLED", ct);
    [HttpPost("{resource}/{id:guid}/deliver"), Authorize(Policy = Permissions.RuralHrWrite)] public Task<IActionResult> Deliver(string resource, Guid id, CancellationToken ct) => ChangeStatus(resource, id, "DELIVERED", ct);
    [HttpPost("{resource}/{id:guid}/return"), Authorize(Policy = Permissions.RuralHrWrite)] public Task<IActionResult> Return(string resource, Guid id, CancellationToken ct) => ChangeStatus(resource, id, "RETURNED", ct);
    [HttpPost("{resource}/{id:guid}/discard"), Authorize(Policy = Permissions.RuralHrWrite)] public Task<IActionResult> Discard(string resource, Guid id, CancellationToken ct) => ChangeStatus(resource, id, "DISCARDED", ct);
    [HttpPost("{resource}/{id:guid}/report"), Authorize(Policy = Permissions.RuralHrWrite)] public Task<IActionResult> Report(string resource, Guid id, CancellationToken ct) => ChangeStatus(resource, id, "OPEN", ct);
    [HttpPost("{resource}/{id:guid}/change-status"), Authorize(Policy = Permissions.RuralHrWrite)] public Task<IActionResult> CustomChangeStatus(string resource, Guid id, [FromBody] RuralHrStatusChangeCommand command, CancellationToken ct) => ChangeStatus(resource, id, command.Status, ct);
    [HttpGet("dashboard")] public Task<RuralHrDashboard> Dashboard(CancellationToken ct) => service.DashboardAsync(ct);
    [HttpGet("{resource}/export")] public async Task<IActionResult> Export(string resource, CancellationToken ct) => File(await service.ExportAsync(Kind(resource), ct), "text/csv", $"{resource}.csv");
    private static string Kind(string resource) => Kinds.TryGetValue(resource, out var kind) ? kind : throw new KeyNotFoundException("Recurso de RH Rural não encontrado.");
    private async Task<IActionResult> ChangeStatus(string resource, Guid id, string status, CancellationToken ct) { var kind = Kind(resource); await service.ChangeStatusAsync(id, kind, status, ct); return NoContent(); }
    private async Task<IActionResult> Boundary(Func<Task<IActionResult>> operation) { try { return await operation(); } catch (Exception ex) { ApiLogMessages.RuralHrBoundaryFailed(logger, ex); throw; } }
}
