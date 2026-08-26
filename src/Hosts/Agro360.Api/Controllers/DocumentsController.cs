using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Route("api/documents"), Authorize]
public sealed class DocumentsController(IDocumentService service) : ControllerBase
{
    [HttpGet("dashboard"), Authorize(Policy=Permissions.DocumentsRead)] public Task<DocumentDashboard> Dashboard(CancellationToken ct)=>service.DashboardAsync(ct);
    [HttpGet, Authorize(Policy=Permissions.DocumentsRead)] public Task<IReadOnlyList<DocumentRow>> List([FromQuery]string? search,[FromQuery]Guid? typeId,[FromQuery]string? status,CancellationToken ct)=>service.DocumentsAsync(search,typeId,status,ct);
    [HttpGet("types"), Authorize(Policy=Permissions.DocumentsRead)] public Task<IReadOnlyList<LookupOption>> Types(CancellationToken ct)=>service.DocumentTypesAsync(ct);
    [HttpGet("lookups/{entityType}"), Authorize(Policy=Permissions.DocumentsRead)] public Task<IReadOnlyList<LookupOption>> Lookups(string entityType,[FromQuery]string? search,CancellationToken ct)=>service.EntityLookupAsync(entityType,search,ct);
    [HttpGet("{id:guid}"), Authorize(Policy=Permissions.DocumentsRead)] public async Task<ActionResult<DocumentDetails>> Details(Guid id,CancellationToken ct)=>await service.DocumentAsync(id,ct) is {} item?Ok(item):NotFound();
    [HttpPost, RequestSizeLimit(26_214_400), Authorize(Policy=Permissions.DocumentsUpload)] public async Task<IActionResult> Upload([FromForm]UploadForm form,CancellationToken ct){if(form.File is null)return ValidationProblem("Arquivo obrigatório.");var command=new UploadDocumentCommand(form.Name,form.Description,form.DocumentTypeId,form.Tags,form.EntityType,form.EntityId);await using var stream=form.File.OpenReadStream();var id=await service.UploadAsync(command,stream,form.File.FileName,form.File.ContentType,form.File.Length,ct);return Created($"/api/documents/{id}",new{id});}
    [HttpPost("{id:guid}/versions"), RequestSizeLimit(26_214_400), Authorize(Policy=Permissions.DocumentsUpload)] public async Task<IActionResult> Version(Guid id,[FromForm]VersionForm form,CancellationToken ct){if(form.File is null)return ValidationProblem("Arquivo obrigatório.");await using var stream=form.File.OpenReadStream();await service.AddVersionAsync(id,stream,form.File.FileName,form.File.ContentType,form.File.Length,form.Reason,ct);return NoContent();}
    [HttpGet("{id:guid}/download"), Authorize(Policy=Permissions.DocumentsDownload)] public async Task<IActionResult> Download(Guid id,[FromQuery]Guid? versionId,CancellationToken ct){var file=await service.DownloadAsync(id,versionId,ct);return File(file.Content,file.MimeType,file.FileName,enableRangeProcessing:true);}
    [HttpPost("{id:guid}/archive"), Authorize(Policy=Permissions.DocumentsUpload)] public async Task<IActionResult> Archive(Guid id,CancellationToken ct){await service.ArchiveAsync(id,ct);return NoContent();}
    [HttpGet("export/{resource}"), Authorize(Policy=Permissions.DocumentsRead)] public async Task<IActionResult> Export(string resource,CancellationToken ct)=>File(await service.ExportCsvAsync(resource,ct),"text/csv",$"agro360-{resource}.csv");
}
public sealed class UploadForm { public string Name {get;set;}="";public string? Description{get;set;}public Guid DocumentTypeId{get;set;}public string? Tags{get;set;}public string? EntityType{get;set;}public Guid? EntityId{get;set;}public IFormFile? File{get;set;} }
public sealed class VersionForm { public string Reason{get;set;}="";public IFormFile? File{get;set;} }

[ApiController,Route("api/evidences"),Authorize]
public sealed class EvidencesController(IDocumentService service):ControllerBase
{
 [HttpGet,Authorize(Policy=Permissions.DocumentsRead)]public Task<IReadOnlyList<EvidenceRow>> List([FromQuery]string? status,CancellationToken ct)=>service.EvidencesAsync(status,ct);
 [HttpPost,Authorize(Policy=Permissions.DocumentsUpload)]public async Task<IActionResult>Create(CreateEvidenceCommand x,CancellationToken ct){var id=await service.CreateEvidenceAsync(x,ct);return Created($"/api/evidences/{id}",new{id});}
 [HttpPost("{id:guid}/decision"),Authorize(Policy=Permissions.EvidencesValidate)]public async Task<IActionResult>Decide(Guid id,ValidateEvidenceCommand x,CancellationToken ct){await service.ValidateEvidenceAsync(id,x,ct);return NoContent();}
}
[ApiController,Route("api/dossiers"),Authorize]
public sealed class DossiersController(IDocumentService service):ControllerBase
{
 [HttpGet,Authorize(Policy=Permissions.DocumentsRead)]public Task<IReadOnlyList<DossierRow>> List(CancellationToken ct)=>service.DossiersAsync(ct);
 [HttpPost,Authorize(Policy=Permissions.DossiersCreate)]public async Task<IActionResult>Create(CreateDossierCommand x,CancellationToken ct){var id=await service.CreateDossierAsync(x,ct);return Created($"/api/dossiers/{id}",new{id});}
 [HttpPost("{id:guid}/decision"),Authorize(Policy=Permissions.DossiersApprove)]public async Task<IActionResult>Decide(Guid id,DossierDecisionCommand x,CancellationToken ct){await service.DecideDossierAsync(id,x,ct);return NoContent();}
}
[ApiController,Route("api/certificates")]
public sealed class CertificatesController(IDocumentService service):ControllerBase
{
 [HttpGet,Authorize(Policy=Permissions.DocumentsRead)]public Task<IReadOnlyList<CertificateRow>> List(CancellationToken ct)=>service.CertificatesAsync(ct);
 [HttpPost,Authorize(Policy=Permissions.CertificatesIssue)]public async Task<IActionResult>Issue(IssueCertificateCommand x,CancellationToken ct){var item=await service.IssueCertificateAsync(x,ct);return Created($"/api/certificates/{item.Id}",item);}
 [HttpPost("{id:guid}/revoke"),Authorize(Policy=Permissions.CertificatesRevoke)]public async Task<IActionResult>Revoke(Guid id,[FromBody]ReasonForm x,CancellationToken ct){await service.RevokeCertificateAsync(id,x.Reason,ct);return NoContent();}
 [AllowAnonymous,HttpGet("public/{code}")]public async Task<ActionResult<PublicCertificate>>Public(string code,CancellationToken ct)=>await service.PublicCertificateAsync(code,HttpContext.Connection.RemoteIpAddress?.ToString(),ct) is{} item?Ok(item):NotFound();
}
public sealed record ReasonForm(string Reason);
