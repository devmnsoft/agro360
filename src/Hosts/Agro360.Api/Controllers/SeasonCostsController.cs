using System.Text;
using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Route("api/finance/season-costs"), Authorize]
public sealed class SeasonCostsController(ISeasonCostService service) : ControllerBase
{
    [HttpGet("overview"), Authorize(Policy=Permissions.FinanceRead)] public Task<dynamic> Overview([FromQuery] SeasonCostQuery q,CancellationToken ct)=>service.OverviewAsync(q,ct);
    [HttpGet, Authorize(Policy=Permissions.FinanceRead)] public Task<dynamic> List([FromQuery] SeasonCostQuery q,CancellationToken ct)=>service.ListAsync(q,ct);
    [HttpGet("{id:guid}"), Authorize(Policy=Permissions.FinanceRead)] public async Task<IActionResult> Detail(Guid id,CancellationToken ct){var value=await service.GetAsync(id,ct);return value is null?NotFound():Ok(value);}
    [HttpPost("manual"), Authorize(Policy=Permissions.FinanceWrite)] public async Task<IActionResult> Manual(ManualSeasonCostCommand x,CancellationToken ct)=>Created("api/finance/season-costs",new{id=await service.AddManualAsync(x,ct)});
    [HttpPost("allocations/preview"), Authorize(Policy=Permissions.FinanceWrite)] public Task<CostAllocationPreviewDto> Preview(CostAllocationPreviewCommand x,CancellationToken ct)=>service.PreviewAsync(x,ct);
    [HttpPost("allocations/confirm"), Authorize(Policy=Permissions.FinanceWrite)] public async Task<IActionResult> Confirm(ConfirmCostAllocationCommand x,CancellationToken ct)=>Created("api/finance/season-costs/history",new{id=await service.ConfirmAsync(x,ct)});
    [HttpPost("allocations/{id:guid}/reverse"), Authorize(Policy=Permissions.FinanceWrite)] public async Task<IActionResult> Reverse(Guid id,ReverseCostAllocationCommand x,CancellationToken ct){await service.ReverseAsync(id,x,ct);return NoContent();}
    [HttpGet("history"), Authorize(Policy=Permissions.FinanceRead)] public Task<IReadOnlyList<dynamic>> History(Guid? seasonId,CancellationToken ct)=>service.HistoryAsync(seasonId,ct);
    [HttpGet("destinations"), Authorize(Policy=Permissions.FinanceRead)] public Task<dynamic> Destinations(Guid? farmId,CancellationToken ct)=>service.DestinationsAsync(farmId,ct);
    [HttpGet("closing/{seasonId:guid}"), Authorize(Policy=Permissions.FinanceRead)] public Task<dynamic> Closing(Guid seasonId,DateOnly cutoffDate,CancellationToken ct)=>service.ClosingReviewAsync(seasonId,cutoffDate,ct);
    [HttpGet("export.csv"), Authorize(Policy=Permissions.FinanceExport)] public async Task<FileContentResult> Export([FromQuery] SeasonCostQuery q,CancellationToken ct)=>File(Encoding.UTF8.GetBytes("\uFEFF"+await service.ExportCsvAsync(q,ct)),"text/csv; charset=utf-8",$"custos-safra-{DateTime.UtcNow:yyyyMMdd}.csv");
}
