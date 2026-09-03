using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Authorize]
public sealed class OperationsController(IOperationsService service) : ControllerBase
{
    [HttpGet("api/suppliers"), Authorize(Policy=Permissions.PurchasingRead)] public Task<PagedResult<SupplierDto>> Suppliers(string? search,string? status,int page=1,int pageSize=25,CancellationToken ct=default)=>service.ListSuppliersAsync(search,status,page,pageSize,ct);
    [HttpGet("api/suppliers/{id:guid}"), Authorize(Policy=Permissions.PurchasingRead)] public async Task<ActionResult<SupplierDto>> Supplier(Guid id,CancellationToken ct)=>await service.GetSupplierAsync(id,ct) is {} x?Ok(x):NotFound();
    [HttpPost("api/suppliers"), Authorize(Policy=Permissions.PurchasingWrite)] public async Task<IActionResult> CreateSupplier(SupplierCommand x,CancellationToken ct){var r=await service.CreateSupplierAsync(x,ct);return Created($"/api/suppliers/{r.Id}",r);}
    [HttpPut("api/suppliers/{id:guid}"), Authorize(Policy=Permissions.PurchasingWrite)] public Task<SupplierDto> UpdateSupplier(Guid id,SupplierCommand x,CancellationToken ct)=>service.UpdateSupplierAsync(id,x,ct);
    [HttpDelete("api/suppliers/{id:guid}"), Authorize(Policy=Permissions.PurchasingWrite)] public async Task<IActionResult> DeleteSupplier(Guid id,CancellationToken ct){await service.DeleteSupplierAsync(id,ct);return NoContent();}
    [HttpGet("api/purchases"), Authorize(Policy=Permissions.PurchasingRead)] public Task<IReadOnlyList<PurchaseDto>> Purchases(string? status,CancellationToken ct)=>service.ListPurchasesAsync(status,ct);
    [HttpPost("api/purchases"), Authorize(Policy=Permissions.PurchasingWrite)] public async Task<IActionResult> CreatePurchase(PurchaseCommand x,CancellationToken ct){var r=await service.CreatePurchaseAsync(x,ct);return Created($"/api/purchases/{r.Id}",r);}
    [HttpPost("api/purchases/{id:guid}/submit"), Authorize(Policy=Permissions.PurchasingApprove)] public async Task<IActionResult> SubmitPurchase(Guid id,CancellationToken ct){await service.ChangePurchaseStatusAsync(id,"submit",ct);return NoContent();}
    [HttpPost("api/purchases/{id:guid}/approve"), Authorize(Policy=Permissions.PurchasingApprove)] public async Task<IActionResult> ApprovePurchase(Guid id,CancellationToken ct){await service.ChangePurchaseStatusAsync(id,"approve",ct);return NoContent();}
    [HttpPost("api/purchases/{id:guid}/cancel"), Authorize(Policy=Permissions.PurchasingApprove)] public async Task<IActionResult> CancelPurchase(Guid id,CancellationToken ct){await service.ChangePurchaseStatusAsync(id,"cancel",ct);return NoContent();}
    [HttpPost("api/purchases/{id:guid}/receive"), Authorize(Policy=Permissions.InventoryMove)] public async Task<IActionResult> Receive(Guid id,ReceiptCommand x,CancellationToken ct){await service.ReceivePurchaseAsync(id,x,ct);return NoContent();}
    [HttpPost("api/inventory/items"), Authorize(Policy=Permissions.InventoryMove)] public async Task<IActionResult> Item(InventoryItemCommand x,CancellationToken ct){var id=await service.CreateInventoryItemAsync(x,ct);return Created($"/api/inventory/items/{id}",new{id});}
    [HttpGet("api/inventory/movements"), Authorize(Policy=Permissions.InventoryRead)] public Task<IReadOnlyList<dynamic>> Movements(CancellationToken ct)=>service.ListMovementsAsync(ct);
    [HttpPost("api/inventory/movements/{type:regex(^(entry|exit|adjust)$)}"), Authorize(Policy=Permissions.InventoryMove)] public async Task<IActionResult> Movement(string type,InventoryMovementCommand x,CancellationToken ct){var id=await service.MoveInventoryAsync(type,x,ct);return Created("/api/inventory/movements/"+id,new{id});}
    [HttpPost("api/inventory/movements/transfer"), Authorize(Policy=Permissions.InventoryMove)] public async Task<IActionResult> Transfer(TransferCommand x,CancellationToken ct){await service.TransferInventoryAsync(x,ct);return NoContent();}
    [HttpGet("api/operations/fleet/assets"), Authorize(Policy=Permissions.FleetRead)] public Task<IReadOnlyList<dynamic>> Assets(CancellationToken ct)=>service.ListAssetsAsync(ct);
    [HttpPost("api/operations/fleet/assets"), Authorize(Policy=Permissions.FleetWrite)] public async Task<IActionResult> Asset(OperationalFleetAssetCommand x,CancellationToken ct){var id=await service.CreateAssetAsync(x,ct);return Created("/api/operations/fleet/assets/"+id,new{id});}
    [HttpPut("api/operations/fleet/assets/{id:guid}"), Authorize(Policy=Permissions.FleetWrite)] public async Task<IActionResult> Asset(Guid id,OperationalFleetAssetCommand x,CancellationToken ct){await service.UpdateAssetAsync(id,x,ct);return NoContent();}
    [HttpGet("api/maintenance/orders"), Authorize(Policy=Permissions.MaintenanceRead)] public Task<IReadOnlyList<dynamic>> Maintenance(CancellationToken ct)=>service.ListMaintenanceAsync(ct);
    [HttpPost("api/maintenance/orders"), Authorize(Policy=Permissions.MaintenanceWrite)] public async Task<IActionResult> Maintenance(MaintenanceOrderCommand x,CancellationToken ct){var id=await service.CreateMaintenanceAsync(x,ct);return Created("/api/maintenance/orders/"+id,new{id});}
    [HttpPost("api/maintenance/orders/{id:guid}/complete"), Authorize(Policy=Permissions.MaintenanceWrite)] public async Task<IActionResult> Complete(Guid id,CompleteMaintenanceCommand x,CancellationToken ct){await service.CompleteMaintenanceAsync(id,x,ct);return NoContent();}
    [HttpGet("api/fuel/fill-ups"), Authorize(Policy=Permissions.FleetRead)] public Task<IReadOnlyList<dynamic>> FillUps(CancellationToken ct)=>service.ListFillUpsAsync(ct);
    [HttpPost("api/fuel/fill-ups"), Authorize(Policy=Permissions.FleetWrite)] public async Task<IActionResult> FillUp(FuelFillUpCommand x,CancellationToken ct){var id=await service.CreateFillUpAsync(x,ct);return Created("/api/fuel/fill-ups/"+id,new{id});}
    [HttpGet("api/operations/dashboard"), Authorize(Policy=Permissions.DashboardRead)] public Task<OperationalDashboardDto> Dashboard(CancellationToken ct)=>service.DashboardAsync(ct);
}
