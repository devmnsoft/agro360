using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Route("api/v1/harvest"), Authorize]
public sealed class HarvestController(IHarvestService harvest) : ControllerBase
{
    [HttpPost("plans"), Authorize(Policy = Permissions.AgricultureWrite)]
    public async Task<IActionResult> Plan(CreateHarvestPlanCommand command,CancellationToken ct){var result=await harvest.CreatePlanAsync(command,ct);return Created($"/api/v1/harvest/plans/{result.Id}",result);}
    [HttpPost("records"), Authorize(Policy = Permissions.AgricultureWrite)]
    public async Task<IActionResult> Record(RegisterHarvestRecordCommand command,CancellationToken ct){var result=await harvest.RegisterAsync(command,ct);return Created($"/api/v1/harvest/records/{result.Id}",result);}
    [HttpPost("receipts"), Authorize(Policy = Permissions.ProductionWrite)]
    public async Task<IActionResult> Receive(ReceiveHarvestCommand command,CancellationToken ct){var result=await harvest.ReceiveAsync(command,ct);return Created($"/api/v1/harvest/receipts/{result.Id}",result);}
    [HttpPost("inspections"), Authorize(Policy = Permissions.ProductionQuality)]
    public async Task<IActionResult> Inspect(CompleteHarvestInspectionCommand command,CancellationToken ct){var result=await harvest.InspectAsync(command,ct);return Created($"/api/v1/harvest/inspections/{result.Id}",result);}
    [HttpPost("allocations"), Authorize(Policy = Permissions.ProductionRelease)]
    public async Task<IActionResult> Allocate(AllocateHarvestMaterialCommand command,CancellationToken ct){var result=await harvest.AllocateAsync(command,ct);return Created($"/api/v1/harvest/allocations/{result.Id}",result);}
    [HttpGet("dashboard"), Authorize(Policy = Permissions.AgricultureRead)] public Task<HarvestDashboardDto> Dashboard(Guid? seasonId,Guid? fieldId,CancellationToken ct)=>harvest.DashboardAsync(seasonId,fieldId,ct);
    [HttpGet("operations"), Authorize(Policy = Permissions.AgricultureRead)] public Task<IReadOnlyCollection<HarvestOperationDto>> List(string? kind,Guid? seasonId,CancellationToken ct)=>harvest.ListAsync(kind,seasonId,ct);
    [HttpGet("trace/{receiptId:guid}"), Authorize(Policy = Permissions.TraceabilityRead)] public Task<HarvestTraceDto> Trace(Guid receiptId,CancellationToken ct)=>harvest.TraceAsync(receiptId,ct);
    [HttpGet("closing"), Authorize(Policy = Permissions.AgricultureRead)] public Task<SeasonClosingDto> Closing(Guid seasonId, DateOnly cutoffDate, CancellationToken ct)=>harvest.GetClosingAsync(seasonId,cutoffDate,ct);
    [HttpPost("closing/checks"), Authorize(Policy = Permissions.AgricultureWrite)] public async Task<IActionResult> RunChecks(RunSeasonClosingCommand command,CancellationToken ct){var result=await harvest.RunClosingChecksAsync(command,ct);return Created($"/api/v1/harvest/closing/runs/{result.Id}",result);}
    [HttpPost("closing/versions"), Authorize(Policy = Permissions.AgricultureWrite)] public async Task<IActionResult> CreateClosing(CreateSeasonClosingCommand command,CancellationToken ct){var result=await harvest.CreateClosingAsync(command,ct);return Created($"/api/v1/harvest/closing/versions/{result.Id}",result);}
    [HttpPost("closing/versions/{id:guid}/close"), Authorize(Policy = Permissions.AgricultureWrite)] public Task<SeasonClosingVersionDto> Close(Guid id,ChangeSeasonClosingStateCommand command,CancellationToken ct)=>harvest.CloseAsync(id,command,ct);
    [HttpPost("closing/versions/{id:guid}/reopen"), Authorize(Policy = Permissions.AgricultureWrite)] public Task<SeasonClosingVersionDto> Reopen(Guid id,ReopenSeasonClosingCommand command,CancellationToken ct)=>harvest.ReopenAsync(id,command,ct);
    [HttpGet("genealogy/seasons/{seasonId:guid}"), Authorize(Policy = Permissions.AgricultureRead)] public Task<SeasonGenealogyDto> SeasonGenealogy(Guid seasonId, CancellationToken ct) => harvest.GetSeasonGenealogyAsync(seasonId, ct);
    [HttpGet("genealogy/lots/{lotNumber}"), Authorize(Policy = Permissions.AgricultureRead)] public Task<LotGenealogyDto> LotGenealogy(string lotNumber, CancellationToken ct) => harvest.GetLotGenealogyAsync(lotNumber, ct);
    [HttpPost("genealogy/links"), Authorize(Policy = Permissions.AgricultureWrite)] public async Task<IActionResult> RecordGenealogyLink(RecordGenealogyLinkCommand command, CancellationToken ct) { var id = await harvest.RecordGenealogyLinkAsync(command, ct); return Created($"/api/v1/harvest/genealogy/links/{id}", new { id }); }
    [HttpGet("operational-pendings"), Authorize(Policy = Permissions.AgricultureRead)] public Task<IReadOnlyCollection<OperationalPendingDto>> OperationalPendings(CancellationToken ct) => harvest.GetOperationalPendingsAsync(ct);
}
