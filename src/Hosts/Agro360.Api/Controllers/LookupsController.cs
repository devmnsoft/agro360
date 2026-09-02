using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Route("api/lookups"), Authorize]
public sealed class LookupsController(ILookupService lookups) : ControllerBase
{
    [HttpGet("{resource}"), Authorize(Policy = Permissions.AgricultureRead)]
    public Task<PagedResult<LookupItem>> Search(string resource, [FromQuery] string? search = null,
        [FromQuery] bool includeInactive = false, [FromQuery] int page = 1, [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        lookups.SearchAsync(resource, search, includeInactive, page, pageSize, cancellationToken);
}
