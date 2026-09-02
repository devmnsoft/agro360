using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController]
[Route("api/v1/inventory")]
[Authorize]
public sealed class InventoryController(IInventoryService inventory) : ControllerBase
{
    [HttpPost("products")]
    [Authorize(Policy = Permissions.InventoryMove)]
    public async Task<IActionResult> CreateProduct(CreateProductCommand command, CancellationToken cancellationToken)
    {
        var result = await inventory.CreateProductAsync(command, cancellationToken).ConfigureAwait(false);
        return Created($"/api/v1/inventory/products/{result.Id}", result);
    }

    [HttpPost("warehouses")]
    [Authorize(Policy = Permissions.InventoryMove)]
    public async Task<IActionResult> CreateWarehouse(CreateWarehouseCommand command, CancellationToken cancellationToken)
    {
        var result = await inventory.CreateWarehouseAsync(command, cancellationToken).ConfigureAwait(false);
        return Created($"/api/v1/inventory/warehouses/{result.Id}", result);
    }

    [HttpPost("movements/receipts")]
    [Authorize(Policy = Permissions.InventoryMove)]
    public Task<StockMovementResult> Receive(StockMovementCommand command, CancellationToken cancellationToken) =>
        inventory.ReceiveAsync(command, cancellationToken);

    [HttpPost("movements/consumptions")]
    [Authorize(Policy = Permissions.InventoryMove)]
    public Task<StockMovementResult> Consume(StockMovementCommand command, CancellationToken cancellationToken) =>
        inventory.ConsumeAsync(command, cancellationToken);

    [HttpGet("balances")]
    [Authorize(Policy = Permissions.InventoryRead)]
    public Task<PagedResult<StockBalanceDto>> ListBalances(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        inventory.ListBalancesAsync(page, pageSize, search, cancellationToken);
}
