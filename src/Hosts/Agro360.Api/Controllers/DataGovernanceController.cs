using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Route("api/governance"), Authorize(Policy = Permissions.GovernanceRead)]
public sealed class DataGovernanceController(IDataGovernanceService service) : ControllerBase
{
    [HttpGet("imports")] public Task<PagedResult<ImportBatch>> Imports(int page=1,int pageSize=25,CancellationToken ct=default)=>service.ImportsAsync(page,pageSize,ct);
    [HttpGet("imports/{id:guid}/errors")] public Task<IReadOnlyList<ImportError>> Errors(Guid id,CancellationToken ct)=>service.ErrorsAsync(id,ct);
    [HttpPost("imports"),Authorize(Policy=Permissions.GovernanceWrite)] public async Task<IActionResult> Import(ImportBatchCommand command,CancellationToken ct){var batch=await service.CreateImportAsync(command,ct);return Created($"/api/governance/imports/{batch.Id}",batch);}
    [HttpPost("imports/{id:guid}/reprocess"),Authorize(Policy=Permissions.GovernanceWrite)] public async Task<IActionResult> Reprocess(Guid id,CancellationToken ct){await service.ReprocessAsync(id,ct);return NoContent();}
    [HttpPost("imports/{id:guid}/cancel"),Authorize(Policy=Permissions.GovernanceWrite)] public async Task<IActionResult> Cancel(Guid id,[FromBody]string reason,CancellationToken ct){await service.CancelAsync(id,reason,ct);return NoContent();}
    [HttpPost("exports"),Authorize(Policy=Permissions.GovernanceExport)] public async Task<IActionResult> Export(ExportRequestCommand command,CancellationToken ct){var id=await service.CreateExportAsync(command,ct);return Accepted($"/api/governance/exports/{id}",new{id});}
    [HttpPost("lgpd"),Authorize(Policy=Permissions.LgpdManage)] public async Task<IActionResult> Lgpd(LgpdRequestCommand command,CancellationToken ct){var id=await service.CreateLgpdAsync(command,ct);return Created($"/api/governance/lgpd/{id}",new{id});}
    [HttpPost("lgpd/{id:guid}/transition"),Authorize(Policy=Permissions.LgpdManage)] public async Task<IActionResult> Lgpd(Guid id,LgpdTransition command,CancellationToken ct){await service.TransitionLgpdAsync(id,command,ct);return NoContent();}
    [HttpPost("quality/{id:guid}/action"),Authorize(Policy=Permissions.GovernanceWrite)] public async Task<IActionResult> Finding(Guid id,FindingAction command,CancellationToken ct){await service.ActOnFindingAsync(id,command,ct);return NoContent();}
}
