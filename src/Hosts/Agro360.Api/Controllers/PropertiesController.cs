using Agro360.Application;
using Agro360.Application.Contracts;
using Agro360.SharedKernel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize]
public sealed class PropertiesController(IPropertyService properties) : ControllerBase
{
    [HttpPost("properties")]
    [Authorize(Policy = Permissions.PropertiesWrite)]
    public async Task<IActionResult> CreateFarm(CreateFarmCommand command, CancellationToken cancellationToken)
    {
        var result = await properties.CreateFarmAsync(command, cancellationToken).ConfigureAwait(false);
        return Created($"/api/v1/properties/{result.Id}", result);
    }

    [HttpGet("properties")]
    [Authorize(Policy = Permissions.PropertiesRead)]
    public Task<PagedResult<FarmDto>> ListFarms(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default) =>
        properties.ListFarmsAsync(page, pageSize, search, cancellationToken);

    [HttpPost("fields")]
    [Authorize(Policy = Permissions.PropertiesWrite)]
    public async Task<IActionResult> CreateField(CreateFieldCommand command, CancellationToken cancellationToken)
    {
        var result = await properties.CreateFieldAsync(command, cancellationToken).ConfigureAwait(false);
        return Created($"/api/v1/fields/{result.Id}", result);
    }

    [HttpGet("properties/{farmId:guid}/fields")]
    [Authorize(Policy = Permissions.PropertiesRead)]
    public Task<PagedResult<FieldDto>> ListFields(
        Guid farmId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default) =>
        properties.ListFieldsAsync(farmId, page, pageSize, cancellationToken);
}
