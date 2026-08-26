using System.Security.Claims;
using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController,Route("api/deployment"),Authorize(Policy=Permissions.DeploymentRead)]
public sealed class DeploymentController(IDeploymentService service):ControllerBase
{
 [HttpGet("templates")] public Task<IReadOnlyList<DeploymentTemplate>> Templates(CancellationToken ct)=>service.TemplatesAsync(ct);
 [HttpPost("onboarding"),Authorize(Policy=Permissions.DeploymentWrite)] public async Task<ActionResult<OnboardingResult>> Onboard(OnboardingCommand command,CancellationToken ct){var result=await service.OnboardAsync(command,Actor(),ct);return CreatedAtAction(nameof(Checklist),new{tenantId=result.TenantId},result);}
 [HttpGet("checklist")] public Task<DeploymentChecklist> Checklist(CancellationToken ct)=>service.ChecklistAsync(ct);
 [HttpPut("checklist/{code}"),Authorize(Policy=Permissions.DeploymentWrite)] public async Task<IActionResult> Checklist(string code,ChecklistUpdate command,CancellationToken ct){await service.SetChecklistAsync(code,command.Completed,command.Notes,Actor(),ct);return NoContent();}
 [HttpPost("imports/preview"),RequestSizeLimit(10_000_000),Authorize(Policy=Permissions.DeploymentWrite)] public Task<ImportPreview> Preview(ImportPreviewCommand command,CancellationToken ct)=>service.PreviewAsync(command,ct);
 [HttpPost("imports/{token:guid}/confirm"),Authorize(Policy=Permissions.DeploymentWrite)] public async Task<IActionResult> Confirm(Guid token,CancellationToken ct){var id=await service.ConfirmImportAsync(token,Actor(),ct);return Created($"api/deployment/imports/{id}",new{id});}
 [HttpGet("imports")] public Task<IReadOnlyList<ImportHistory>> Imports(CancellationToken ct)=>service.ImportsAsync(ct);
 [HttpPost("imports/{id:guid}/rollback"),Authorize(Policy=Permissions.DeploymentWrite)] public async Task<IActionResult> Rollback(Guid id,CancellationToken ct){await service.RollbackImportAsync(id,Actor(),ct);return NoContent();}
 [HttpGet("dashboard")] public Task<DeploymentDashboard> Dashboard(CancellationToken ct)=>service.DashboardAsync(ct);
 private Guid Actor()=>Guid.TryParse(User.FindFirstValue("sub"),out var id)?id:throw new UnauthorizedAccessException("Usuário autenticado inválido.");
}
public sealed record ChecklistUpdate(bool Completed,string? Notes);
