using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController,Route("api/compliance"),Authorize(Policy=Permissions.ComplianceRead)]
public sealed class ComplianceController(IComplianceService service,ILogger<ComplianceController> logger):ControllerBase
{
 [HttpGet("documents")] public Task<IReadOnlyList<ComplianceDocument>> Documents(string? status,CancellationToken ct)=>service.DocumentsAsync(status,ct);
 [HttpPost("documents"),Authorize(Policy=Permissions.ComplianceWrite)] public Task<IActionResult> CreateDocument(DocumentCommand x,CancellationToken ct)=>Create(()=>service.SaveDocumentAsync(null,x,ct),"documents");
 [HttpPut("documents/{id:guid}"),Authorize(Policy=Permissions.ComplianceWrite)] public Task<IActionResult> UpdateDocument(Guid id,DocumentCommand x,CancellationToken ct)=>Update(()=>service.SaveDocumentAsync(id,x,ct));
 [HttpGet("product-rules")] public Task<IReadOnlyList<ProductRule>> Rules(Guid? productId,string? market,CancellationToken ct)=>service.RulesAsync(productId,market,ct);
 [HttpPost("product-rules"),Authorize(Policy=Permissions.ComplianceWrite)] public Task<IActionResult> CreateRule(ProductRuleCommand x,CancellationToken ct)=>Create(()=>service.SaveRuleAsync(null,x,ct),"product-rules");
 [HttpPut("product-rules/{id:guid}"),Authorize(Policy=Permissions.ComplianceWrite)] public Task<IActionResult> UpdateRule(Guid id,ProductRuleCommand x,CancellationToken ct)=>Update(()=>service.SaveRuleAsync(id,x,ct));
 [HttpPost("lots/{id:guid}/block"),Authorize(Policy=Permissions.ComplianceApprove)] public async Task<IActionResult> BlockLot(Guid id,ActionCommand x,CancellationToken ct){await Boundary("lot-block",()=>service.SetLotBlockAsync(id,true,x.Reason,ct));return NoContent();}
 [HttpPost("lots/{id:guid}/unblock"),Authorize(Policy=Permissions.ComplianceApprove)] public async Task<IActionResult> UnblockLot(Guid id,ActionCommand x,CancellationToken ct){await Boundary("lot-unblock",()=>service.SetLotBlockAsync(id,false,x.Reason,ct));return NoContent();}
 [HttpGet("certifications")] public Task<IReadOnlyList<Certification>> Certifications(string? status,CancellationToken ct)=>service.CertificationsAsync(status,ct);
 [HttpPost("certifications"),Authorize(Policy=Permissions.ComplianceWrite)] public Task<IActionResult> CreateCertification(CertificationCommand x,CancellationToken ct)=>Create(()=>service.SaveCertificationAsync(null,x,ct),"certifications");
 [HttpPut("certifications/{id:guid}"),Authorize(Policy=Permissions.ComplianceWrite)] public Task<IActionResult> UpdateCertification(Guid id,CertificationCommand x,CancellationToken ct)=>Update(()=>service.SaveCertificationAsync(id,x,ct));
 [HttpPost("certifications/{id:guid}/approve"),Authorize(Policy=Permissions.ComplianceApprove)] public async Task<IActionResult> ApproveCertification(Guid id,ActionCommand x,CancellationToken ct){await Boundary("certification-approve",()=>service.DecideCertificationAsync(id,true,x.Reason,ct));return NoContent();}
 [HttpPost("certifications/{id:guid}/reject"),Authorize(Policy=Permissions.ComplianceApprove)] public async Task<IActionResult> RejectCertification(Guid id,ActionCommand x,CancellationToken ct){await Boundary("certification-reject",()=>service.DecideCertificationAsync(id,false,x.Reason,ct));return NoContent();}
 [HttpGet("audits")] public Task<IReadOnlyList<ChainAudit>> Audits(string? status,CancellationToken ct)=>service.AuditsAsync(status,ct);
 [HttpPost("audits"),Authorize(Policy=Permissions.ComplianceWrite)] public Task<IActionResult> CreateAudit(AuditCommand x,CancellationToken ct)=>Create(()=>service.SaveAuditAsync(null,x,ct),"audits");
 [HttpPut("audits/{id:guid}"),Authorize(Policy=Permissions.ComplianceWrite)] public Task<IActionResult> UpdateAudit(Guid id,AuditCommand x,CancellationToken ct)=>Update(()=>service.SaveAuditAsync(id,x,ct));
 [HttpPost("audits/{id:guid}/complete"),Authorize(Policy=Permissions.ComplianceApprove)] public async Task<IActionResult> CompleteAudit(Guid id,CancellationToken ct){await Boundary("complete-audit",()=>service.CompleteAuditAsync(id,ct));return NoContent();}
 [HttpGet("non-conformities")] public Task<IReadOnlyList<NonConformity>> NonConformities(string? status,CancellationToken ct)=>service.NonConformitiesAsync(status,ct);
 [HttpPost("non-conformities"),Authorize(Policy=Permissions.ComplianceWrite)] public Task<IActionResult> CreateNc(NonConformityCommand x,CancellationToken ct)=>Create(()=>service.SaveNonConformityAsync(null,x,ct),"non-conformities");
 [HttpPut("non-conformities/{id:guid}"),Authorize(Policy=Permissions.ComplianceWrite)] public Task<IActionResult> UpdateNc(Guid id,NonConformityCommand x,CancellationToken ct)=>Update(()=>service.SaveNonConformityAsync(id,x,ct));
 [HttpPost("non-conformities/{id:guid}/close"),Authorize(Policy=Permissions.ComplianceApprove)] public async Task<IActionResult> CloseNc(Guid id,ActionCommand x,CancellationToken ct){await Boundary("close-nc",()=>service.CloseNonConformityAsync(id,x.Reason,ct));return NoContent();}
 [HttpPost("{entityType:regex(^documents|certifications|audits|non-conformities$)}/{id:guid}/evidence"),Authorize(Policy=Permissions.ComplianceWrite)] public async Task<IActionResult> Evidence(string entityType,Guid id,EvidenceCommand x,CancellationToken ct){await Boundary("evidence",()=>service.AttachEvidenceAsync(entityType.Replace("-","").ToUpperInvariant(),id,x,ct));return NoContent();}
 [HttpGet("export-dossiers")] public Task<IReadOnlyList<ExportDossier>> Dossiers(CancellationToken ct)=>service.DossiersAsync(ct);
 [HttpPost("export-dossiers"),Authorize(Policy=Permissions.ComplianceWrite)] public Task<IActionResult> CreateDossier(DossierCommand x,CancellationToken ct)=>Create(()=>service.CreateDossierAsync(x,ct),"export-dossiers");
 [HttpGet("export-dossiers/{id:guid}/report")] public async Task<IActionResult> Report(Guid id,CancellationToken ct)=>File(await service.ExportDossierAsync(id,ct),"text/plain; charset=utf-8",$"dossie-{id:N}.txt");
 [HttpGet("dashboard")] public Task<ComplianceDashboard> Dashboard(CancellationToken ct)=>service.DashboardAsync(ct);
 private async Task<IActionResult>Create(Func<Task<Guid>> operation,string route){var id=await Boundary("create",operation);return Created($"api/compliance/{route}/{id}",new{id});}
 private async Task<IActionResult>Update(Func<Task<Guid>> operation){await Boundary("update",operation);return NoContent();}
 private async Task<T> Boundary<T>(string operation,Func<Task<T>> action){try{return await action();}catch(Exception exception){ApiLogMessages.ComplianceBoundaryFailed(logger,operation,exception);throw;}}
 private async Task Boundary(string operation,Func<Task> action){try{await action();}catch(Exception exception){ApiLogMessages.ComplianceBoundaryFailed(logger,operation,exception);throw;}}
}

[ApiController,Route("api/esg"),Authorize(Policy=Permissions.EsgRead)]
public sealed class EsgController(IEsgService service):ControllerBase
{
 [HttpGet("indicators")]public Task<IReadOnlyList<EsgIndicator>> Indicators(CancellationToken ct)=>service.IndicatorsAsync(ct);
 [HttpPost("indicators"),Authorize(Policy=Permissions.EsgWrite)]public async Task<IActionResult>Indicator(EsgIndicatorCommand x,CancellationToken ct){var id=await service.AddIndicatorAsync(x,ct);return Created($"api/esg/indicators/{id}",new{id});}
 [HttpGet("carbon")]public Task<IReadOnlyList<CarbonEntry>> Carbon(CancellationToken ct)=>service.CarbonAsync(ct);
 [HttpPost("carbon"),Authorize(Policy=Permissions.EsgWrite)]public async Task<IActionResult>Carbon(CarbonCommand x,CancellationToken ct){var id=await service.AddCarbonAsync(x,ct);return Created($"api/esg/carbon/{id}",new{id});}
}

[ApiController, Route("api/public/compliance"), AllowAnonymous]
public sealed class PublicComplianceController(IComplianceService service) : ControllerBase
{
    [HttpGet("certificates/{certificate:regex(^[[A-Fa-f0-9]]{{20}}$)}")]
    public async Task<IActionResult> Verify(string certificate, CancellationToken ct)
    {
        var result = await service.PublicDossierAsync(certificate, ct);
        return result is null ? NotFound() : Ok(result);
    }
}
