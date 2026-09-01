using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
namespace Agro360.Api.Controllers;
[ApiController,Route("api/procurement"),Authorize]
public sealed class ProcurementController(IProcurementService service) : ControllerBase
{
 [HttpGet("dashboard"),Authorize(Policy=Permissions.PurchasingRead)] public async Task<IActionResult> Dashboard(CancellationToken ct)=>Ok(await service.DashboardAsync(ct));
 [HttpGet("suppliers"),Authorize(Policy=Permissions.PurchasingRead)] public Task<IReadOnlyList<dynamic>> Suppliers([FromQuery]ProcurementQuery q,CancellationToken ct)=>service.SuppliersAsync(q,ct);
 [HttpPost("suppliers"),Authorize(Policy=Permissions.PurchasingWrite)] public async Task<IActionResult> Supplier(ProcurementSupplierCommand x,CancellationToken ct)=>Created("api/procurement/suppliers",new{id=await service.SaveSupplierAsync(null,x,ct)});
 [HttpPut("suppliers/{id:guid}"),Authorize(Policy=Permissions.PurchasingWrite)] public async Task<IActionResult> Supplier(Guid id,ProcurementSupplierCommand x,CancellationToken ct){await service.SaveSupplierAsync(id,x,ct);return NoContent();}
 [HttpPost("suppliers/{id:guid}/homologation"),Authorize(Policy=Permissions.PurchasingHomologate)] public async Task<IActionResult> Homologate(Guid id,[FromQuery]bool approve,HomologationCommand x,CancellationToken ct){await service.HomologateAsync(id,approve,x,ct);return NoContent();}
 [HttpGet("catalog"),Authorize(Policy=Permissions.PurchasingRead)] public Task<IReadOnlyList<dynamic>> Catalog([FromQuery]ProcurementQuery q,CancellationToken ct)=>service.CatalogAsync(q,ct);
 [HttpPost("catalog"),Authorize(Policy=Permissions.PurchasingWrite)] public async Task<IActionResult> Catalog(CatalogItemCommand x,CancellationToken ct)=>Created("api/procurement/catalog",new{id=await service.SaveCatalogItemAsync(null,x,ct)});
 [HttpGet("requisitions"),Authorize(Policy=Permissions.PurchasingRead)] public Task<IReadOnlyList<dynamic>> Requisitions([FromQuery]ProcurementQuery q,CancellationToken ct)=>service.RequisitionsAsync(q,ct);
 [HttpPost("requisitions"),Authorize(Policy=Permissions.PurchasingRequest)] public async Task<IActionResult> Requisition(RequisitionCommand x,CancellationToken ct)=>Created("api/procurement/requisitions",new{id=await service.CreateRequisitionAsync(x,ct)});
 [HttpGet("orders"),Authorize(Policy=Permissions.PurchasingRead)] public Task<IReadOnlyList<dynamic>> Orders([FromQuery]ProcurementQuery q,CancellationToken ct)=>service.OrdersAsync(q,ct);
 [HttpPost("orders"),Authorize(Policy=Permissions.PurchasingWrite)] public async Task<IActionResult> Order(PurchaseOrderCommand x,CancellationToken ct)=>Created("api/procurement/orders",new{id=await service.CreateOrderAsync(x,ct)});
 [HttpPost("orders/{id:guid}/approve"),Authorize(Policy=Permissions.PurchasingApprove)] public async Task<IActionResult> Approve(Guid id,[FromBody]string? comment,CancellationToken ct){await service.ApproveOrderAsync(id,comment,ct);return NoContent();}
 [HttpPost("receipts"),Authorize(Policy=Permissions.PurchasingReceive)] public async Task<IActionResult> Receive(ProcurementReceiptCommand x,CancellationToken ct)=>Created("api/procurement/receipts",new{id=await service.ReceiveAsync(x,ct)});
 [HttpGet("reports/{report}.csv"),Authorize(Policy=Permissions.PurchasingExport)] public async Task<IActionResult> Export(string report,[FromQuery]ProcurementQuery q,CancellationToken ct)=>File(await service.ExportAsync(report,q,ct),"text/csv; charset=utf-8",$"compras-{report}-{DateTime.UtcNow:yyyyMMdd}.csv");
}
