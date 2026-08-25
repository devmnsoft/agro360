using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController]
[Route("api/v1/commercial")]
[Authorize]
public sealed class CommercialController(ICommercialService commercial) : ControllerBase
{
    [HttpPost("sales")]
    [Authorize(Policy = Permissions.CommercialWrite)]
    public async Task<IActionResult> CreateSale(CreateSaleCommand command, CancellationToken cancellationToken)
    {
        var result = await commercial.CreateAndConfirmSaleAsync(command, cancellationToken).ConfigureAwait(false);
        return Created($"/api/v1/commercial/sales/{result.SaleId}", result);
    }
}
