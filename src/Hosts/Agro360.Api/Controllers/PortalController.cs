using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Route("api/portal/access")]
public sealed class PortalAccessController(IPortalService service) : ControllerBase
{
    [AllowAnonymous, HttpPost("accept-invitation")]
    public Task<PortalAuthentication> Accept(AcceptPortalInvitationCommand command, CancellationToken ct) =>
        service.AcceptInvitationAsync(command, ct);

    [AllowAnonymous, HttpPost("login")]
    public Task<PortalAuthentication> Login(PortalLoginCommand command, CancellationToken ct) =>
        service.LoginAsync(command, ct);

    [Authorize(Policy = Permissions.PortalAccess), HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] PortalChangePasswordCommand command, CancellationToken ct)
    {
        await service.ChangePasswordAsync(command, ct);
        return NoContent();
    }
}

[ApiController, Route("api/portal/invitations"), Authorize(Policy = Permissions.PortalManage)]
public sealed class PortalInvitationsController(IPortalService service) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<PortalInvitationRow>> List([FromQuery] string? status, CancellationToken ct) =>
        service.InvitationsAsync(status, ct);

    [HttpPost]
    public async Task<IActionResult> Create(PortalInvitationCommand command, CancellationToken ct)
    {
        var result = await service.InviteAsync(command, ct);
        return Created($"api/portal/invitations/{result.Id}", result);
    }

    [HttpPost("{id:guid}/revoke")]
    public async Task<IActionResult> Revoke(Guid id, [FromBody] PortalReason command, CancellationToken ct)
    {
        await service.RevokeInvitationAsync(id, command.Reason, ct);
        return NoContent();
    }
}

[ApiController, Route("api/portal"), Authorize(Policy = Permissions.PortalAccess)]
public sealed class PortalController(IPortalService service) : ControllerBase
{
    [HttpGet("dashboard")]
    public Task<PortalDashboard> Dashboard(CancellationToken ct) =>
        service.DashboardAsync(ct);

    [HttpGet("marketplace")]
    public Task<IReadOnlyList<MarketplaceListing>> Marketplace([FromQuery] MarketplaceFilter filter, CancellationToken ct) =>
        service.MarketplaceAsync(filter, ct);

    [HttpPost("marketplace/quotes")]
    public async Task<IActionResult> Quote(CreateQuoteCommand command, CancellationToken ct)
    {
        var id = await service.RequestQuoteAsync(command, ct);
        return Created($"api/portal/marketplace/quotes/{id}", new { id });
    }

    [HttpGet("marketplace/my-quotes")]
    public Task<IReadOnlyList<PortalQuoteDetailDto>> MyQuotes(CancellationToken ct) =>
        service.MyQuotesAsync(ct);

    [HttpGet("requests")]
    public Task<IReadOnlyList<PortalRequestRow>> Requests(CancellationToken ct) =>
        service.RequestsAsync(ct);

    [HttpGet("requests/{id:guid}")]
    public async Task<ActionResult<PortalRequestDetailDto>> RequestDetail(Guid id, CancellationToken ct) =>
        await service.RequestDetailAsync(id, ct);

    [HttpPost("requests")]
    public async Task<IActionResult> SubmitPortalRequest(PortalRequestCommand command, CancellationToken ct)
    {
        var id = await service.CreateRequestAsync(command, ct);
        return Created($"api/portal/requests/{id}", new { id });
    }

    [HttpPost("requests/{id:guid}/cancel")]
    public async Task<IActionResult> CancelRequest(Guid id, [FromBody] PortalReason command, CancellationToken ct)
    {
        await service.CancelRequestAsync(id, new CancelPortalRequestCommand(command.Reason), ct);
        return NoContent();
    }

    [HttpPost("announcements/{id:guid}/read")]
    public async Task<IActionResult> Read(Guid id, CancellationToken ct)
    {
        await service.MarkAnnouncementReadAsync(id, ct);
        return NoContent();
    }

    [HttpGet("traceability/{publicCode}")]
    public async Task<ActionResult<PublicTraceDto>> PublicTrace(string publicCode, CancellationToken ct)
    {
        var trace = await service.PublicTraceAsync(publicCode, ct);
        return trace is not null ? Ok(trace) : NotFound();
    }

    [HttpGet("documents")]
    public Task<IReadOnlyList<PortalDocumentItemDto>> Documents(CancellationToken ct) =>
        service.DocumentsAsync(ct);

    [HttpGet("documents/{id:guid}/download")]
    public async Task<IActionResult> DownloadDocument(Guid id, CancellationToken ct)
    {
        var download = await service.DownloadDocumentAsync(id, ct);
        return File(download.Content, download.MimeType, download.FileName);
    }

    [HttpGet("support/articles")]
    public Task<IReadOnlyList<PortalSupportArticleDto>> SupportArticles([FromQuery] string? search, CancellationToken ct) =>
        service.SupportArticlesAsync(search, ct);
}

public sealed record PortalReason(string Reason);
