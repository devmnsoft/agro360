using Agro360.Application.Contracts;
using Agro360.Application;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Route("api/mobile"), Authorize(Policy=Permissions.MobileRead)]
public sealed class MobileController(IMobileService service) : ControllerBase
{
    [HttpGet("bootstrap")] public Task<dynamic> Bootstrap([FromQuery] Guid deviceId,CancellationToken ct)=>service.BootstrapAsync(deviceId,ct);
    [HttpPost("sync"), Authorize(Policy=Permissions.MobileSync)] public Task<dynamic> Sync(SyncCommand command,CancellationToken ct)=>service.SyncAsync(command,ct);
    [HttpGet("sync/status")] public Task<dynamic> Status([FromQuery] Guid deviceId,CancellationToken ct)=>service.SyncStatusAsync(deviceId,ct);
    [HttpGet("dashboard")] public Task<dynamic> Dashboard(CancellationToken ct)=>service.DashboardAsync(ct);
    [HttpPost("quick-records/{area:regex(^(agriculture|livestock|inventory|logistics)$)}")] public async Task<IActionResult> Quick(string area,QuickRecordCommand command,CancellationToken ct)=>Created("api/mobile/dashboard",new{id=await service.QuickRecordAsync(area,command,ct)});
    [HttpPost("occurrences"), Authorize(Policy=Permissions.MobileWrite)] public async Task<IActionResult> Occurrence(FieldOccurrenceCommand command,CancellationToken ct)=>Created("api/mobile/dashboard",new{id=await service.AddOccurrenceAsync(command,ct)});
    [HttpPost("checkins"), Authorize(Policy=Permissions.MobileWrite)] public async Task<IActionResult> Checkin(FieldCheckinCommand command,CancellationToken ct)=>Created("api/mobile/dashboard",new{id=await service.AddCheckinAsync(command,ct)});
}

[ApiController, Route("api/evidences"), Authorize(Policy=Permissions.MobileRead)]
public sealed class EvidencesController(IMobileService service):ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<dynamic>> Get(CancellationToken ct)=>service.EvidencesAsync(ct);
    [HttpPost, Authorize(Policy=Permissions.MobileWrite), RequestSizeLimit(10_500_000)] public async Task<IActionResult> Post(MobileEvidenceCommand command,CancellationToken ct)=>Created("api/evidences",new{id=await service.AddEvidenceAsync(command,ct)});
}

[ApiController, Route("api/geolocation/events"), Authorize(Policy=Permissions.MobileRead)]
public sealed class GeolocationController(IMobileService service):ControllerBase
{
    [HttpGet] public Task<IReadOnlyList<dynamic>> Get(CancellationToken ct)=>service.LocationsAsync(ct);
    [HttpPost, Authorize(Policy=Permissions.MobileWrite)] public async Task<IActionResult> Post(GeolocationCommand command,CancellationToken ct)=>Created("api/geolocation/events",new{id=await service.AddLocationAsync(command,ct)});
}

[ApiController, Route("api/qrcode")]
public sealed class QrCodeController(IMobileService service):ControllerBase
{
    [HttpPost("generate"),Authorize(Policy=Permissions.MobileWrite)] public Task<dynamic> Generate(QrGenerateCommand command,CancellationToken ct)=>service.GenerateQrAsync(command,ct);
    [HttpGet("resolve/{code}")] public async Task<IActionResult> Resolve(string code,CancellationToken ct){var value=await service.ResolveQrAsync(code,User.Identity?.IsAuthenticated==true,ct);return value is null?NotFound(new{message="QR Code inválido ou não encontrado."}):Ok(value);}
}

[ApiController,Route("api/checklists"),Authorize(Policy=Permissions.MobileRead)]
public sealed class ChecklistsController(IMobileService service):ControllerBase
{
    [HttpGet("templates")] public Task<IReadOnlyList<dynamic>> Templates(CancellationToken ct)=>service.TemplatesAsync(ct);
    [HttpPost("templates"), Authorize(Policy=Permissions.FieldChecklistsManage)] public async Task<IActionResult> Template(ChecklistTemplateCommand command,CancellationToken ct)=>Created("api/checklists/templates",new{id=await service.AddTemplateAsync(command,ct)});
    [HttpPost("apply"), Authorize(Policy=Permissions.MobileWrite)] public async Task<IActionResult> Apply(ChecklistApplyCommand command,CancellationToken ct)=>Created("api/checklists",new{id=await service.ApplyChecklistAsync(command,ct)});
    [HttpPost("{id:guid}/complete"), Authorize(Policy=Permissions.MobileWrite)] public async Task<IActionResult> Complete(Guid id,ChecklistCompleteCommand command,CancellationToken ct){await service.CompleteChecklistAsync(id,command,ct);return NoContent();}
}
