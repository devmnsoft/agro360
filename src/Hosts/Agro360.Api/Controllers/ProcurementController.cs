using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Agro360.Api.Controllers;

[ApiController, Route("api/procurement"), Authorize]
public sealed class ProcurementController(IProcurementService service, IAuthorizationService authorization) : ControllerBase
{
    [HttpGet("dashboard"), Authorize(Policy = Permissions.PurchasingRead)] public async Task<IActionResult> Dashboard(CancellationToken ct) => Ok(await service.DashboardAsync(ct));
    [HttpGet("suppliers"), Authorize(Policy = Permissions.PurchasingRead)] public Task<IReadOnlyList<dynamic>> Suppliers([FromQuery] ProcurementQuery q, CancellationToken ct) => service.SuppliersAsync(q, ct);
    [HttpPost("suppliers"), Authorize(Policy = Permissions.PurchasingWrite)] public async Task<IActionResult> Supplier(ProcurementSupplierCommand x, CancellationToken ct) => Created("api/procurement/suppliers", new { id = await service.SaveSupplierAsync(null, x, ct) });
    [HttpPut("suppliers/{id:guid}"), Authorize(Policy = Permissions.PurchasingWrite)] public async Task<IActionResult> Supplier(Guid id, ProcurementSupplierCommand x, CancellationToken ct) { await service.SaveSupplierAsync(id, x, ct); return NoContent(); }
    [HttpPost("suppliers/{id:guid}/homologation"), Authorize(Policy = Permissions.PurchasingHomologate)] public async Task<IActionResult> Homologate(Guid id, [FromQuery] bool approve, HomologationCommand x, CancellationToken ct) { await service.HomologateAsync(id, approve, x, ct); return NoContent(); }
    [HttpGet("catalog"), Authorize(Policy = Permissions.PurchasingRead)] public Task<IReadOnlyList<dynamic>> Catalog([FromQuery] ProcurementQuery q, CancellationToken ct) => service.CatalogAsync(q, ct);
    [HttpPost("catalog"), Authorize(Policy = Permissions.PurchasingWrite)] public async Task<IActionResult> Catalog(CatalogItemCommand x, CancellationToken ct) => Created("api/procurement/catalog", new { id = await service.SaveCatalogItemAsync(null, x, ct) });
    [HttpGet("requisitions"), Authorize(Policy = Permissions.PurchasingRead)] public Task<IReadOnlyList<dynamic>> Requisitions([FromQuery] ProcurementQuery q, CancellationToken ct) => service.RequisitionsAsync(q, ct);
    [HttpPost("requisitions"), Authorize(Policy = Permissions.PurchasingRequest)] public async Task<IActionResult> Requisition(RequisitionCommand x, CancellationToken ct) => Created("api/procurement/requisitions", new { id = await service.CreateRequisitionAsync(x, ct) });
    [HttpGet("requisitions/{id:guid}"), Authorize(Policy = Permissions.PurchasingRead)] public Task<dynamic> Requisition(Guid id,CancellationToken ct)=>service.RequisitionAsync(id,ct);
    [HttpPost("requisitions/{id:guid}/submit"), Authorize(Policy = Permissions.PurchasingRequest)] public async Task<IActionResult> SubmitRequisition(Guid id,RequisitionTransitionCommand x,CancellationToken ct){await service.SubmitRequisitionAsync(id,x,ct);return NoContent();}
    [HttpPost("requisitions/{id:guid}/decision"), Authorize(Policy = Permissions.PurchasingApprove)] public async Task<IActionResult> DecideRequisition(Guid id,RequisitionDecisionCommand x,CancellationToken ct){await service.DecideRequisitionAsync(id,x,ct);return NoContent();}
    [HttpPost("requisitions/{id:guid}/cancel"), Authorize(Policy = Permissions.PurchasingRequest)] public async Task<IActionResult> CancelRequisition(Guid id,RequisitionTransitionCommand x,CancellationToken ct){await service.CancelRequisitionAsync(id,x,ct);return NoContent();}
    [HttpGet("approval-queue"), Authorize(Policy = Permissions.PurchasingApprove)] public Task<IReadOnlyList<dynamic>> ApprovalQueue([FromQuery] ProcurementQuery q,CancellationToken ct)=>service.ApprovalQueueAsync(q,ct);
    [HttpGet("orders"), Authorize(Policy = Permissions.PurchasingRead)] public Task<IReadOnlyList<dynamic>> Orders([FromQuery] ProcurementQuery q, CancellationToken ct) => service.OrdersAsync(q, ct);
    [HttpPost("orders"), Authorize(Policy = Permissions.PurchasingWrite)] public async Task<IActionResult> Order(PurchaseOrderCommand x, CancellationToken ct) => Created("api/procurement/orders", new { id = await service.CreateOrderAsync(x, ct) });
    [HttpPost("orders/{id:guid}/approve"), Authorize(Policy = Permissions.PurchasingApprove)] public async Task<IActionResult> Approve(Guid id, [FromBody] string? comment, CancellationToken ct) { await service.ApproveOrderAsync(id, comment, ct); return NoContent(); }
    [HttpPost("orders/{id:guid}/cancel"), Authorize(Policy = Permissions.PurchasingApprove)] public async Task<IActionResult> CancelOrder(Guid id,RequisitionTransitionCommand x,CancellationToken ct){await service.CancelOrderAsync(id,x,ct);return NoContent();}
    [HttpPost("receipts"), Authorize(Policy = Permissions.PurchasingReceive)] public async Task<IActionResult> Receive(ProcurementReceiptCommand x, CancellationToken ct)
    {
        if (x.OverrideExcess && !(await authorization.AuthorizeAsync(User, Permissions.PurchasingOverrideExcess)).Succeeded) return Forbid();
        return Created("api/procurement/receipts", new { id = await service.ReceiveAsync(x, ct) });
    }
    [HttpGet("receipts"), Authorize(Policy = Permissions.PurchasingRead)] public Task<IReadOnlyList<dynamic>> Receipts([FromQuery] ProcurementQuery q, CancellationToken ct) => service.ReceiptsAsync(q, ct);
    [HttpGet("receipts/{id:guid}"), Authorize(Policy = Permissions.PurchasingRead)] public Task<dynamic> Receipt(Guid id, CancellationToken ct) => service.ReceiptAsync(id, ct);
    [HttpPost("receipt-items/{id:guid}/quality-decisions"), Authorize(Policy = Permissions.ComplianceApprove)] public async Task<IActionResult> DecideQuality(Guid id, ReceiptQualityDecisionCommand x, CancellationToken ct) => Created($"api/procurement/receipt-items/{id}/quality-decisions", new { id = await service.DecideQualityAsync(id, x, ct) });
    [HttpGet("orders/{id:guid}/pending-items"), Authorize(Policy = Permissions.PurchasingRead)] public Task<IReadOnlyList<dynamic>> PendingItems(Guid id, CancellationToken ct) => service.PendingOrderItemsAsync(id, ct);
    [HttpGet("receipt-options"), Authorize(Policy = Permissions.PurchasingRead)] public Task<dynamic> ReceiptOptions(CancellationToken ct) => service.ReceiptOptionsAsync(ct);
    [HttpGet("invoice-matches"), Authorize(Policy = Permissions.PurchasingRead)] public Task<IReadOnlyList<dynamic>> InvoiceMatches([FromQuery] ProcurementQuery q, CancellationToken ct) => service.InvoiceMatchesAsync(q, ct);
    [HttpGet("invoice-matches/{id:guid}"), Authorize(Policy = Permissions.PurchasingRead)] public Task<dynamic> InvoiceMatch(Guid id, CancellationToken ct) => service.InvoiceMatchAsync(id, ct);
    [HttpGet("orders/{id:guid}/match-options"), Authorize(Policy = Permissions.PurchasingRead)] public Task<IReadOnlyList<dynamic>> MatchOptions(Guid id, CancellationToken ct) => service.MatchOptionsAsync(id, ct);
    [HttpPost("invoice-matches"), Authorize(Policy = Permissions.PurchasingReceive)] public async Task<IActionResult> MatchInvoice(PurchaseInvoiceMatchCommand x, CancellationToken ct) => Created("api/procurement/invoice-matches", new { id = await service.MatchInvoiceAsync(x, ct) });
    [HttpPost("match-divergences/{id:guid}/decision"), Authorize(Policy = Permissions.PurchasingApprove)] public async Task<IActionResult> ResolveDivergence(Guid id, MatchDivergenceDecisionCommand x, CancellationToken ct) { await service.ResolveMatchDivergenceAsync(id, x, ct); return NoContent(); }
    [HttpGet("match-tolerance"), Authorize(Policy = Permissions.PurchasingRead)] public Task<dynamic> MatchTolerance(CancellationToken ct) => service.MatchToleranceAsync(ct);
    [HttpPut("match-tolerance"), Authorize(Policy = Permissions.PurchasingApprove)] public async Task<IActionResult> SaveMatchTolerance(MatchToleranceCommand x, CancellationToken ct) => Ok(new { id = await service.SaveMatchToleranceAsync(x, ct) });
    [HttpGet("reports/{report}.csv"), Authorize(Policy = Permissions.PurchasingExport)] public async Task<IActionResult> Export(string report, [FromQuery] ProcurementQuery q, CancellationToken ct) => File(await service.ExportAsync(report, q, ct), "text/csv; charset=utf-8", $"compras-{report}-{DateTime.UtcNow:yyyyMMdd}.csv");
}
