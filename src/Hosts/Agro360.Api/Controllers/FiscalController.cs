using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Agro360.Api.Controllers;
[ApiController,Route("api/fiscal"),Authorize]
public sealed class FiscalController(IFiscalService service,IFiscalEmissionService emission):ControllerBase
{
 [HttpGet("dashboard"),Authorize(Policy=Permissions.FiscalRead)] public async Task<IActionResult> Dashboard(CancellationToken ct)=>Ok(await service.DashboardAsync(ct));
 [HttpGet("operations"),Authorize(Policy=Permissions.FiscalRead)] public Task<IReadOnlyList<dynamic>> Operations([FromQuery]FiscalQuery q,CancellationToken ct)=>service.OperationsAsync(q,ct);
 [HttpPost("operations"),Authorize(Policy=Permissions.FiscalWrite)] public async Task<IActionResult> Operation(FiscalOperationCommand x,CancellationToken ct)=>Created("api/fiscal/operations",new{id=await service.SaveOperationAsync(null,x,ct)});
 [HttpPut("operations/{id:guid}"),Authorize(Policy=Permissions.FiscalWrite)] public async Task<IActionResult> Operation(Guid id,FiscalOperationCommand x,CancellationToken ct){await service.SaveOperationAsync(id,x,ct);return NoContent();}
 [HttpGet("rules"),Authorize(Policy=Permissions.FiscalRead)] public Task<IReadOnlyList<dynamic>> Rules([FromQuery]FiscalQuery q,CancellationToken ct)=>service.RulesAsync(q,ct);
 [HttpPost("rules"),Authorize(Policy=Permissions.FiscalWrite)] public async Task<IActionResult> Rule(FiscalRuleCommand x,CancellationToken ct)=>Created("api/fiscal/rules",new{id=await service.SaveRuleAsync(null,x,ct)});
 [HttpGet("invoices"),Authorize(Policy=Permissions.FiscalRead)] public Task<IReadOnlyList<dynamic>> Invoices([FromQuery]FiscalQuery q,CancellationToken ct)=>service.InvoicesAsync(q,ct);
 [HttpPost("invoices"),Authorize(Policy=Permissions.FiscalWrite)] public async Task<IActionResult> Invoice(FiscalInvoiceCommand x,CancellationToken ct)=>Created("api/fiscal/invoices",new{id=await service.CreateInvoiceAsync(x,ct)});
 [HttpPost("invoices/{id:guid}/confirm"),Authorize(Policy=Permissions.FiscalApprove)] public async Task<IActionResult> Confirm(Guid id,CancellationToken ct){await service.ConfirmInvoiceAsync(id,ct);return NoContent();}
 [HttpPost("invoices/{id:guid}/cancel"),Authorize(Policy=Permissions.FiscalApprove)] public async Task<IActionResult> Cancel(Guid id,[FromBody]string reason,CancellationToken ct){await service.CancelInvoiceAsync(id,reason,ct);return NoContent();}
 [HttpGet("documents"),Authorize(Policy=Permissions.FiscalRead)] public Task<IReadOnlyList<dynamic>> Documents([FromQuery]FiscalQuery q,CancellationToken ct)=>service.DocumentsAsync(q,ct);
 [HttpPost("documents"),Authorize(Policy=Permissions.FiscalWrite)] public async Task<IActionResult> Document(FiscalDocumentCommand x,CancellationToken ct)=>Created("api/fiscal/documents",new{id=await service.CreateDocumentAsync(x,ct)});
 [HttpPost("documents/{id:guid}/status"),Authorize(Policy=Permissions.FiscalApprove)] public async Task<IActionResult> Status(Guid id,[FromBody]FiscalStatusRequest x,CancellationToken ct){await service.ChangeDocumentStatusAsync(id,x.Status,x.Reason,ct);return NoContent();}
 [HttpPost("documents/{id:guid}/submit"),Authorize(Policy=Permissions.FiscalApprove)] public async Task<IActionResult> Submit(Guid id,CancellationToken ct)=>Ok(await emission.SubmitAsync(id,ct));
 [HttpPost("documents/{id:guid}/query"),Authorize(Policy=Permissions.FiscalRead)] public async Task<IActionResult> Query(Guid id,CancellationToken ct)=>Ok(await emission.QueryAsync(id,ct));
 [HttpPost("documents/{id:guid}/cancel"),Authorize(Policy=Permissions.FiscalApprove)] public async Task<IActionResult> CancelDocument(Guid id,[FromBody]FiscalCancellationRequest x,CancellationToken ct)=>Ok(await emission.CancelAsync(id,x.Reason,ct));
 [HttpPost("documents/{id:guid}/corrections"),Authorize(Policy=Permissions.FiscalApprove)] public async Task<IActionResult> CorrectDocument(Guid id,FiscalCorrectionCommand x,CancellationToken ct)=>Created($"api/fiscal/documents/{id}/corrections",new{id=await service.CreateCorrectionAsync(id,x,ct)});
 [HttpGet("catalog/{resource}"),Authorize(Policy=Permissions.FiscalRead)] public Task<IReadOnlyList<dynamic>> Catalog(string resource,CancellationToken ct)=>service.CatalogAsync(resource,ct);
 [HttpPost("purchase-checks"),Authorize(Policy=Permissions.FiscalApprove)] public async Task<IActionResult> Check(FiscalPurchaseCheckCommand x,CancellationToken ct)=>Created("api/fiscal/purchase-checks",new{id=await service.CheckPurchaseAsync(x,ct)});
 [HttpGet("reports/{report}.csv"),Authorize(Policy=Permissions.FiscalReports)] public async Task<IActionResult> Report(string report,[FromQuery]FiscalQuery q,CancellationToken ct)=>File(await service.ExportCsvAsync(report,q,ct),"text/csv; charset=utf-8",$"fiscal-{report}-{DateTime.UtcNow:yyyyMMdd}.csv");
}
public sealed record FiscalStatusRequest(string Status,string Reason);
public sealed record FiscalCancellationRequest(string Reason);
