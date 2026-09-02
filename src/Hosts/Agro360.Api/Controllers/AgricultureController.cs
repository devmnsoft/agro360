using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController]
[Route("api/v1/agriculture")]
[Authorize]
public sealed class AgricultureController(IAgricultureService agriculture) : ControllerBase
{
    [HttpPost("seasons")]
    [Authorize(Policy = Permissions.AgricultureWrite)]
    public async Task<IActionResult> CreateSeason(CreateSeasonCommand command, CancellationToken cancellationToken)
    {
        var result = await agriculture.CreateSeasonAsync(command, cancellationToken).ConfigureAwait(false);
        return Created($"/api/v1/agriculture/seasons/{result.Id}", result);
    }

    [HttpGet("seasons")]
    [Authorize(Policy = Permissions.AgricultureRead)]
    public Task<PagedResult<SeasonDto>> ListSeasons(
        [FromQuery] Guid? farmId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default) =>
        agriculture.ListSeasonsAsync(farmId, page, pageSize, cancellationToken);

    [HttpPost("operations/planting")]
    [Authorize(Policy = Permissions.AgricultureWrite)]
    public Task<FieldOperationResult> Plant(RegisterPlantingCommand command, CancellationToken cancellationToken) =>
        agriculture.RegisterPlantingAsync(command, cancellationToken);

    [HttpPost("operations/harvest")]
    [Authorize(Policy = Permissions.AgricultureWrite)]
    public Task<FieldOperationResult> Harvest(RegisterHarvestCommand command, CancellationToken cancellationToken) =>
        agriculture.RegisterHarvestAsync(command, cancellationToken);
}
