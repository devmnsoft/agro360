using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController,Route("api/traceability")]
public sealed class TraceabilityController(ISprint10Service service):ControllerBase
{
 [HttpGet("lots"),Authorize(Policy=Permissions.TraceabilityRead)] public Task<IReadOnlyList<dynamic>> Lots(CancellationToken ct)=>service.LotsAsync(ct);
 [HttpPost("lots"),Authorize(Policy=Permissions.TraceabilityWrite)] public async Task<IActionResult> Add(TraceableLotCommand x,CancellationToken ct)=>Created("api/traceability/lots",new{id=await service.SaveLotAsync(null,x,ct)});
 [HttpPut("lots/{id:guid}"),Authorize(Policy=Permissions.TraceabilityWrite)] public async Task<IActionResult> Edit(Guid id,TraceableLotCommand x,CancellationToken ct){await service.SaveLotAsync(id,x,ct);return NoContent();}
 [HttpPost("lots/{id:guid}/events"),Authorize(Policy=Permissions.TraceabilityWrite)] public async Task<IActionResult> Event(Guid id,LotEventCommand x,CancellationToken ct){await service.AddLotEventAsync(id,x,ct);return NoContent();}
 [HttpGet("lots/{id:guid}/timeline"),Authorize(Policy=Permissions.TraceabilityRead)] public Task<IReadOnlyList<dynamic>> Timeline(Guid id,CancellationToken ct)=>service.TimelineAsync(id,ct);
 [HttpPost("certificates"),Authorize(Policy=Permissions.TraceabilityWrite)] public async Task<IActionResult> Certificate(CertificateCommand x,CancellationToken ct){var result=await service.CreateCertificateAsync(x,ct);return Created($"api/traceability/certificates/{result.Code}",new{id=result.Id,code=result.Code});}
 [HttpGet("certificates/{code}"),AllowAnonymous] public async Task<IActionResult> PublicCertificate(string code,CancellationToken ct)=>await service.CertificateAsync(code,ct) is{} value?Ok(value):NotFound();
 [HttpGet("dashboard"),Authorize(Policy=Permissions.TraceabilityRead)] public async Task<IActionResult> Dashboard(CancellationToken ct)=>Ok(await service.DashboardAsync(ct));
}
[ApiController,Route("api/processing"),Authorize]
public sealed class ProcessingComplianceController(ISprint10Service service):ControllerBase { [HttpPost("compliance-events"),Authorize(Policy=Permissions.TraceabilityWrite)] public async Task<IActionResult> Add(ComplianceEventCommand x,CancellationToken ct)=>Created("api/processing/compliance-events",new{id=await service.AddComplianceAsync(x,ct)}); }
[ApiController,Route("api/ledger"),Authorize]
public sealed class LedgerController(IImmutableLedgerService service):ControllerBase { [HttpPost("validate"),Authorize(Policy=Permissions.LedgerValidate)] public async Task<IActionResult> Validate(CancellationToken ct)=>Ok(await service.ValidateAsync(ct)); }
[ApiController,Route("api/regional-logistics"),Authorize]
public sealed class RegionalLogisticsController(ISprint10Service service):ControllerBase
{
 [HttpGet("routes"),Authorize(Policy=Permissions.RegionalLogisticsRead)] public Task<IReadOnlyList<dynamic>> Routes(CancellationToken ct)=>service.RoutesAsync(ct);
 [HttpPost("routes"),Authorize(Policy=Permissions.RegionalLogisticsWrite)] public async Task<IActionResult> Route(RouteCommand x,CancellationToken ct)=>Created("api/regional-logistics/routes",new{id=await service.AddRouteAsync(x,ct)});
 [HttpPost("trips"),Authorize(Policy=Permissions.RegionalLogisticsWrite)] public async Task<IActionResult> Trip(TripRegionalCommand x,CancellationToken ct)=>Created("api/regional-logistics/trips",new{id=await service.AddTripAsync(x,ct)});
 [HttpPost("trips/{id:guid}/occurrences"),Authorize(Policy=Permissions.RegionalLogisticsWrite)] public async Task<IActionResult> Occurrence(Guid id,RegionalOccurrenceCommand x,CancellationToken ct){await service.AddOccurrenceAsync(id,x,ct);return NoContent();}
}
[ApiController,Route("api/sales-network"),Authorize]
public sealed class SalesNetworkController(ISprint10Service service):ControllerBase
{
 [HttpGet("partners"),Authorize(Policy=Permissions.SalesNetworkRead)] public Task<IReadOnlyList<dynamic>> Partners(CancellationToken ct)=>service.PartnersAsync(ct);
 [HttpPost("partners"),Authorize(Policy=Permissions.SalesNetworkWrite)] public async Task<IActionResult> Partner(SalesNetworkPartnerCommand x,CancellationToken ct)=>Created("api/sales-network/partners",new{id=await service.AddPartnerAsync(x,ct)});
 [HttpPost("commission-rules"),Authorize(Policy=Permissions.SalesNetworkWrite)] public async Task<IActionResult> Rule(CommissionRuleCommand x,CancellationToken ct)=>Created("api/sales-network/commission-rules",new{id=await service.AddCommissionRuleAsync(x,ct)});
 [HttpPost("splits/calculate"),Authorize(Policy=Permissions.SalesNetworkWrite)] public async Task<IActionResult> Calculate(SplitCalculationCommand x,CancellationToken ct)=>Ok(await service.CalculateSplitAsync(x,ct));
 [HttpPost("splits/{id:guid}/approve"),Authorize(Policy=Permissions.SalesNetworkApprove)] public async Task<IActionResult> Approve(Guid id,CancellationToken ct){await service.ApproveSplitAsync(id,ct);return NoContent();}
}
