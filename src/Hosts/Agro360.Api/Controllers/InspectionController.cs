using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController, Route("api/inspections"), Authorize(Policy = Permissions.ComplianceRead)]
public sealed class InspectionController(IInspectionService service, ILogger<InspectionController> logger) : ControllerBase
{
    [HttpGet("models")]
    public Task<IReadOnlyList<InspectionModelListItem>> ListModels(string? process, string? status, CancellationToken ct) =>
        service.ListModelsAsync(process, status, ct);

    [HttpGet("models/{id:guid}")]
    public Task<InspectionModelDetail> GetModel(Guid id, CancellationToken ct) =>
        service.GetModelAsync(id, ct);

    [HttpPost("models"), Authorize(Policy = Permissions.ComplianceInspectionModelsWrite)]
    public Task<IActionResult> CreateModel(InspectionModelCreateCommand command, CancellationToken ct) =>
        Create(() => service.CreateModelAsync(command, ct), "models");

    [HttpPut("models/{id:guid}"), Authorize(Policy = Permissions.ComplianceInspectionModelsWrite)]
    public async Task<IActionResult> UpdateModel(Guid id, InspectionModelUpdateCommand command, CancellationToken ct)
    {
        await Boundary("update-model", () => service.UpdateModelAsync(id, command, ct));
        return NoContent();
    }

    [HttpPost("models/{id:guid}/versions"), Authorize(Policy = Permissions.ComplianceInspectionModelsWrite)]
    public Task<IActionResult> CreateDraftVersion(Guid id, [FromQuery] Guid? fromVersionId, [FromQuery] string? changeReason, [FromBody] CreateDraftVersionBody? body, CancellationToken ct) =>
        Create(() => service.CreateDraftVersionAsync(id, body?.FromVersionId ?? fromVersionId, body?.ChangeReason ?? changeReason, ct), $"models/{id}/versions");

    [HttpGet("versions/{versionId:guid}")]
    public Task<InspectionVersionDetail> GetVersion(Guid versionId, CancellationToken ct) =>
        service.GetVersionAsync(versionId, ct);

    [HttpPut("versions/{versionId:guid}"), Authorize(Policy = Permissions.ComplianceInspectionModelsWrite)]
    public async Task<IActionResult> UpdateDraftVersion(Guid versionId, InspectionDraftVersionUpdateCommand command, CancellationToken ct)
    {
        await Boundary("update-draft-version", () => service.UpdateDraftVersionAsync(versionId, command, ct));
        return NoContent();
    }

    public sealed record CreateDraftVersionBody(Guid? FromVersionId, string? ChangeReason);

    [HttpPost("versions/{versionId:guid}/submit-review"), Authorize(Policy = Permissions.ComplianceInspectionModelsWrite)]
    public async Task<IActionResult> SubmitForReview(Guid versionId, InspectionVersionActionCommand command, CancellationToken ct)
    {
        await Boundary("submit-review", () => service.SubmitForReviewAsync(versionId, command.ExpectedRowVersion, ct));
        return NoContent();
    }

    [HttpPost("versions/{versionId:guid}/publish"), Authorize(Policy = Permissions.ComplianceInspectionModelsPublish)]
    public async Task<IActionResult> PublishVersion(Guid versionId, InspectionPublishVersionCommand command, CancellationToken ct)
    {
        await Boundary("publish-version", () => service.PublishVersionAsync(versionId, command, ct));
        return NoContent();
    }

    [HttpPost("models/{id:guid}/inactivate"), Authorize(Policy = Permissions.ComplianceInspectionModelsPublish)]
    public async Task<IActionResult> InactivateModel(Guid id, InspectionInactivateCommand command, CancellationToken ct)
    {
        await Boundary("inactivate-model", () => service.InactivateModelAsync(id, command, ct));
        return NoContent();
    }

    [HttpPost("versions/{versionId:guid}/inactivate"), Authorize(Policy = Permissions.ComplianceInspectionModelsPublish)]
    public async Task<IActionResult> InactivateVersion(Guid versionId, InspectionInactivateCommand command, CancellationToken ct)
    {
        await Boundary("inactivate-version", () => service.InactivateVersionAsync(versionId, command, ct));
        return NoContent();
    }

    [HttpPost("resolve")]
    public Task<InspectionModelResolution> Resolve(InspectionSelectionContext context, CancellationToken ct) =>
        service.ResolveApplicableModelsAsync(context, ct);

    [HttpGet("runs")]
    public Task<PagedResult<InspectionRunListItem>> ListRuns([FromQuery] InspectionRunFilter filters, CancellationToken ct) =>
        service.ListRunsAsync(filters, ct);

    [HttpGet("runs/{id:guid}")]
    public Task<InspectionRunDetail> GetRun(Guid id, CancellationToken ct) =>
        service.GetRunAsync(id, ct);

    [HttpPost("runs"), Authorize(Policy = Permissions.ComplianceInspectionsExecute)]
    public Task<IActionResult> StartRun(InspectionStartRunCommand command, CancellationToken ct) =>
        Create(() => service.StartRunAsync(command, ct), "runs");

    [HttpPut("runs/{id:guid}/answers"), Authorize(Policy = Permissions.ComplianceInspectionsExecute)]
    public Task<InspectionSaveAnswersResult> SaveAnswers(Guid id, InspectionSaveAnswersCommand command, CancellationToken ct) =>
        Boundary("save-answers", () => service.SaveAnswersAsync(id, command, ct));

    [HttpPost("runs/{id:guid}/complete"), Authorize(Policy = Permissions.ComplianceInspectionsExecute)]
    public Task<InspectionCompleteRunResult> CompleteRun(Guid id, InspectionCompleteRunCommand command, CancellationToken ct) =>
        Boundary("complete-run", () => service.CompleteRunAsync(id, command, ct));

    [HttpPost("runs/{id:guid}/cancel"), Authorize(Policy = Permissions.ComplianceInspectionsExecute)]
    public async Task<IActionResult> CancelRun(Guid id, InspectionCancelRunCommand command, CancellationToken ct)
    {
        await Boundary("cancel-run", () => service.CancelRunAsync(id, command, ct));
        return NoContent();
    }

    [HttpPost("runs/{id:guid}/reinspections"), Authorize(Policy = Permissions.ComplianceInspectionsExecute)]
    public Task<IActionResult> CreateReinspection(Guid id, InspectionReinspectionCommand command, CancellationToken ct) =>
        Create(() => service.CreateReinspectionAsync(id, command, ct), $"runs/{id}/reinspections");

    [HttpGet("runs/{id:guid}/compare/{otherId:guid}")]
    public Task<InspectionRunCompareResult> CompareRuns(Guid id, Guid otherId, CancellationToken ct) =>
        service.CompareRunsAsync(id, otherId, ct);

    [HttpGet("schedules")]
    public Task<IReadOnlyList<InspectionScheduleListItem>> ListSchedules(CancellationToken ct) =>
        service.ListSchedulesAsync(ct);

    [HttpPost("schedules"), Authorize(Policy = Permissions.ComplianceInspectionModelsWrite)]
    public Task<IActionResult> CreateSchedule(InspectionScheduleCommand command, CancellationToken ct) =>
        Create(() => service.SaveScheduleAsync(null, command, ct), "schedules");

    [HttpPut("schedules/{id:guid}"), Authorize(Policy = Permissions.ComplianceInspectionModelsWrite)]
    public Task<IActionResult> UpdateSchedule(Guid id, InspectionScheduleCommand command, CancellationToken ct) =>
        Update(() => service.SaveScheduleAsync(id, command, ct));

    [HttpPost("schedules/{id:guid}/inactivate"), Authorize(Policy = Permissions.ComplianceInspectionModelsPublish)]
    public async Task<IActionResult> InactivateSchedule(Guid id, InspectionScheduleInactivateCommand command, CancellationToken ct)
    {
        await Boundary("inactivate-schedule", () => service.InactivateScheduleAsync(id, command, ct));
        return NoContent();
    }

    private async Task<IActionResult> Create(Func<Task<Guid>> operation, string route)
    {
        var id = await Boundary("create", operation);
        return Created($"api/inspections/{route}/{id}", new { id });
    }

    private async Task<IActionResult> Update(Func<Task<Guid>> operation)
    {
        await Boundary("update", operation);
        return NoContent();
    }

    private async Task<T> Boundary<T>(string operation, Func<Task<T>> action)
    {
        try { return await action(); }
        catch (Exception exception)
        {
            ApiLogMessages.ComplianceBoundaryFailed(logger, operation, exception);
            throw;
        }
    }

    private async Task Boundary(string operation, Func<Task> action)
    {
        try { await action(); }
        catch (Exception exception)
        {
            ApiLogMessages.ComplianceBoundaryFailed(logger, operation, exception);
            throw;
        }
    }
}
