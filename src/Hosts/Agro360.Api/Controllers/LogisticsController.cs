using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Agro360.Api.Controllers;

[ApiController, Route("api/logistics/trips"), Authorize]
public sealed class LogisticsController(ILogisticsService service) : ControllerBase
{
    [HttpGet, Authorize(Policy = Permissions.LogisticsRead)] public Task<IReadOnlyList<dynamic>> List(CancellationToken ct) => service.ListAsync(ct);
    [HttpPost, Authorize(Policy = Permissions.LogisticsWrite)] public async Task<IActionResult> Add(TripCommand x, CancellationToken ct) => Created("api/logistics/trips", new { id = await service.SaveAsync(null, x, ct) });
    [HttpPut("{id:guid}"), Authorize(Policy = Permissions.LogisticsWrite)] public async Task<IActionResult> Edit(Guid id, TripCommand x, CancellationToken ct) { await service.SaveAsync(id, x, ct); return NoContent(); }
    [HttpPost("{id:guid}/occurrences"), Authorize(Policy = Permissions.LogisticsWrite)] public async Task<IActionResult> Occurrence(Guid id, TripOccurrenceCommand x, CancellationToken ct) { await service.AddOccurrenceAsync(id, x, ct); return NoContent(); }
    [HttpPost("{id:guid}/complete"), Authorize(Policy = Permissions.LogisticsWrite)] public async Task<IActionResult> Complete(Guid id, CancellationToken ct) { await service.CompleteAsync(id, ct); return NoContent(); }
    [HttpGet("fulfillment/queue"), Authorize(Policy = Permissions.LogisticsRead)] public Task<IReadOnlyList<dynamic>> Queue([FromQuery] string? customer, [FromQuery] Guid? unitId, [FromQuery] DateOnly? dueUntil, [FromQuery] string? status, CancellationToken ct) => service.FulfillmentQueueAsync(customer, unitId, dueUntil, status, ct);
    [HttpGet("fulfillment/indicators"), Authorize(Policy = Permissions.LogisticsRead)] public Task<FulfillmentIndicators> Indicators(CancellationToken ct) => service.FulfillmentIndicatorsAsync(ct);
    [HttpGet("fulfillment/{id:guid}"), Authorize(Policy = Permissions.LogisticsRead)] public async Task<IActionResult> Fulfillment(Guid id, CancellationToken ct) { var result = await service.FulfillmentDetailAsync(id, ct); return result is null ? NotFound() : Ok(result); }
    [HttpPost("fulfillment"), Authorize(Policy = Permissions.LogisticsWrite)] public async Task<IActionResult> CreateFulfillment(CreateFulfillmentCommand command, CancellationToken ct) { var id = await service.CreateFulfillmentAsync(command, ct); return Created($"api/logistics/trips/fulfillment/{id}", new { id }); }
    [HttpPost("fulfillment/{id:guid}/dispatch"), Authorize(Policy = Permissions.LogisticsWrite)] public async Task<IActionResult> Dispatch(Guid id, DispatchFulfillmentCommand command, CancellationToken ct) { await service.DispatchFulfillmentAsync(id, command, ct); return NoContent(); }
    [HttpPost("fulfillment/{id:guid}/attempts"), Authorize(Policy = Permissions.LogisticsWrite)] public async Task<IActionResult> Attempt(Guid id, DeliveryAttemptCommand command, CancellationToken ct) { var attemptId = await service.RecordDeliveryAttemptAsync(id, command, ct); return Created($"api/logistics/trips/fulfillment/{id}/attempts/{attemptId}", new { id = attemptId }); }
    [HttpPost("fulfillment/returns"), Authorize(Policy = Permissions.LogisticsWrite)] public async Task<IActionResult> Return(ReturnCommand command, CancellationToken ct) { var id = await service.RegisterReturnAsync(command, ct); return Created($"api/logistics/trips/fulfillment/returns/{id}", new { id }); }
}
