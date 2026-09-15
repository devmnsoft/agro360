using Agro360.Application;
using Agro360.Application.Contracts;
using Agro360.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Route("api/v1/inventory/stock-control"), Authorize]
public sealed class StockControlController(IStockControlService service) : ControllerBase
{
    [HttpGet("transfers"), Authorize(Policy=Permissions.InventoryRead)] public Task<PagedResult<StockTransferListDto>> Transfers(int page=1,int pageSize=25,string? status=null,CancellationToken ct=default)=>service.ListTransfersAsync(page,pageSize,status,ct);
    [HttpGet("transfers/{id:guid}"), Authorize(Policy=Permissions.InventoryRead)] public Task<StockTransferDetailDto> Transfer(Guid id,CancellationToken ct)=>service.GetTransferAsync(id,ct);
    [HttpPost("transfers"), Authorize(Policy=Permissions.InventoryMove)] public async Task<IActionResult> CreateTransfer(CreateStockTransferCommand command,CancellationToken ct){var id=await service.CreateTransferAsync(command,ct);return Created($"transfers/{id}",new{id});}
    [HttpPost("transfers/{id:guid}/ship"), Authorize(Policy=Permissions.InventoryMove)] public async Task<IActionResult> Ship(Guid id,TransferActionCommand command,CancellationToken ct){await service.ShipAsync(id,command,ct);return NoContent();}
    [HttpPost("transfers/{id:guid}/receive"), Authorize(Policy=Permissions.InventoryMove)] public async Task<IActionResult> Receive(Guid id,TransferReceiptCommand command,CancellationToken ct){await service.ReceiveAsync(id,command,ct);return NoContent();}
    [HttpPost("transfers/{id:guid}/cancel"), Authorize(Policy=Permissions.InventoryAdjust)] public async Task<IActionResult> Cancel(Guid id,TransferActionCommand command,CancellationToken ct){await service.CancelTransferAsync(id,command,ct);return NoContent();}
    [HttpGet("counts"), Authorize(Policy=Permissions.InventoryRead)] public Task<PagedResult<PhysicalCountListDto>> Counts(int page=1,int pageSize=25,string? status=null,CancellationToken ct=default)=>service.ListCountsAsync(page,pageSize,status,ct);
    [HttpGet("counts/{id:guid}"), Authorize(Policy=Permissions.InventoryRead)] public Task<PhysicalCountDetailDto> Count(Guid id,CancellationToken ct)=>service.GetCountAsync(id,ct);
    [HttpPost("counts"), Authorize(Policy=Permissions.InventoryAdjust)] public async Task<IActionResult> CreateCount(CreatePhysicalCountCommand command,CancellationToken ct){var id=await service.CreateCountAsync(command,ct);return Created($"counts/{id}",new{id});}
    [HttpPost("counts/{id:guid}/open"), Authorize(Policy=Permissions.InventoryAdjust)] public async Task<IActionResult> Open(Guid id,CountActionCommand command,CancellationToken ct){await service.OpenCountAsync(id,command,ct);return NoContent();}
    [HttpPost("counts/{id:guid}/entries"), Authorize(Policy=Permissions.InventoryMove)] public async Task<IActionResult> Record(Guid id,RecordCountCommand command,CancellationToken ct){await service.RecordCountAsync(id,command,ct);return NoContent();}
    [HttpPost("counts/{id:guid}/reconcile"), Authorize(Policy=Permissions.InventoryAdjust)] public async Task<IActionResult> Reconcile(Guid id,ReconcileCountCommand command,CancellationToken ct){await service.ReconcileAsync(id,command,ct);return NoContent();}
    [HttpPost("counts/{id:guid}/approve"), Authorize(Policy=Permissions.InventoryAdjust)] public async Task<IActionResult> Approve(Guid id,CountActionCommand command,CancellationToken ct){await service.ApproveAdjustmentsAsync(id,command,ct);return NoContent();}
}
