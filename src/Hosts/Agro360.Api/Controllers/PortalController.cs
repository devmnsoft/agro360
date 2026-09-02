using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController,Route("api/portal/access")]
public sealed class PortalAccessController(IPortalService service):ControllerBase
{
 [AllowAnonymous,HttpPost("accept-invitation")] public Task<PortalAuthentication> Accept(AcceptPortalInvitationCommand command,CancellationToken ct)=>service.AcceptInvitationAsync(command,ct);
 [AllowAnonymous,HttpPost("login")] public Task<PortalAuthentication> Login(PortalLoginCommand command,CancellationToken ct)=>service.LoginAsync(command,ct);
}

[ApiController,Route("api/portal/invitations"),Authorize(Policy=Permissions.PortalManage)]
public sealed class PortalInvitationsController(IPortalService service):ControllerBase
{
 [HttpGet] public Task<IReadOnlyList<PortalInvitationRow>> List([FromQuery]string? status,CancellationToken ct)=>service.InvitationsAsync(status,ct);
 [HttpPost] public async Task<IActionResult> Create(PortalInvitationCommand command,CancellationToken ct){var result=await service.InviteAsync(command,ct);return Created($"api/portal/invitations/{result.Id}",result);}
 [HttpPost("{id:guid}/revoke")] public async Task<IActionResult> Revoke(Guid id,[FromBody]PortalReason command,CancellationToken ct){await service.RevokeInvitationAsync(id,command.Reason,ct);return NoContent();}
}

[ApiController,Route("api/portal"),Authorize(Policy=Permissions.PortalAccess)]
public sealed class PortalController(IPortalService service):ControllerBase
{
 [HttpGet("dashboard")] public Task<PortalDashboard> Dashboard(CancellationToken ct)=>service.DashboardAsync(ct);
 [HttpGet("marketplace")] public Task<IReadOnlyList<MarketplaceListing>> Marketplace([FromQuery]MarketplaceFilter filter,CancellationToken ct)=>service.MarketplaceAsync(filter,ct);
 [HttpPost("marketplace/quotes")] public async Task<IActionResult> Quote(CreateQuoteCommand command,CancellationToken ct){var id=await service.RequestQuoteAsync(command,ct);return Created($"api/portal/marketplace/quotes/{id}",new{id});}
 [HttpGet("requests")] public Task<IReadOnlyList<PortalRequestRow>> Requests(CancellationToken ct)=>service.RequestsAsync(ct);
 [HttpPost("requests")] public async Task<IActionResult> SubmitPortalRequest(PortalRequestCommand command,CancellationToken ct){var id=await service.CreateRequestAsync(command,ct);return Created($"api/portal/requests/{id}",new{id});}
 [HttpPost("announcements/{id:guid}/read")] public async Task<IActionResult> Read(Guid id,CancellationToken ct){await service.MarkAnnouncementReadAsync(id,ct);return NoContent();}
}
public sealed record PortalReason(string Reason);
