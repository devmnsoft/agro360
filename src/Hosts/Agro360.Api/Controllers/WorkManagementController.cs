using Agro360.Application;
using Agro360.Application.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Agro360.Api.Controllers;

[ApiController,Authorize]
public sealed class WorkManagementController(IWorkManagementService service):ControllerBase
{
 [HttpGet("api/work/users"),Authorize(Policy=Permissions.WorkRead)] public Task<IReadOnlyList<WorkLookup>> Users(string? search,CancellationToken ct)=>service.ActiveUsersAsync(search,ct);
 [HttpGet("api/tasks"),Authorize(Policy=Permissions.WorkRead)] public Task<Agro360.SharedKernel.PagedResult<OperationalTaskRow>> Tasks(string? search,string? status,string? priority,string? module,Guid? responsibleId,DateTimeOffset? from,DateTimeOffset? to,int page=1,int pageSize=25,CancellationToken ct=default)=>service.TasksAsync(search,status,priority,module,responsibleId,from,to,page,pageSize,ct);
 [HttpPost("api/tasks"),Authorize(Policy=Permissions.WorkWrite)] public async Task<IActionResult> CreateTask(TaskCommand x,CancellationToken ct){var id=await service.CreateTaskAsync(x,ct);return Created($"/api/tasks/{id}",new{id});}
 [HttpPost("api/tasks/{id:guid}/status"),Authorize(Policy=Permissions.WorkWrite)] public async Task<IActionResult> TaskStatus(Guid id,TaskTransition x,CancellationToken ct){await service.ChangeTaskAsync(id,x,ct);return NoContent();}
 [HttpGet("api/tasks/{id:guid}/history"),Authorize(Policy=Permissions.WorkRead)] public Task<IReadOnlyList<TaskEventRow>> TaskHistory(Guid id,CancellationToken ct)=>service.TaskHistoryAsync(id,ct);
 [HttpGet("api/alerts"),Authorize(Policy=Permissions.WorkRead)] public Task<IReadOnlyList<AlertRow>> Alerts(string? severity,string? status,string? module,CancellationToken ct)=>service.AlertsAsync(severity,status,module,ct);
 [HttpPost("api/alerts/{id:guid}/{action}"),Authorize(Policy=Permissions.WorkWrite)] public async Task<IActionResult> Alert(Guid id,string action,CancellationToken ct){await service.ChangeAlertAsync(id,action,ct);return NoContent();}
 [HttpPost("api/rules/evaluate"),Authorize(Policy=Permissions.WorkWrite)] public async Task<object> Evaluate(CancellationToken ct)=>new{generated=await service.EvaluateRulesAsync(ct)};
 [HttpGet("api/rules"),Authorize(Policy=Permissions.WorkRead)] public Task<IReadOnlyList<RuleRow>> Rules(CancellationToken ct)=>service.RulesAsync(ct);
 [HttpPost("api/rules"),Authorize(Policy=Permissions.WorkWrite)] public async Task<object> CreateRule(RuleCommand x,CancellationToken ct)=>new{id=await service.SaveRuleAsync(null,x,ct)};
 [HttpPut("api/rules/{id:guid}"),Authorize(Policy=Permissions.WorkWrite)] public async Task<object> UpdateRule(Guid id,RuleCommand x,CancellationToken ct)=>new{id=await service.SaveRuleAsync(id,x,ct)};
 [HttpGet("api/workflows"),Authorize(Policy=Permissions.WorkRead)] public Task<IReadOnlyList<WorkflowRow>> Workflows(string? status,string? module,CancellationToken ct)=>service.WorkflowsAsync(status,module,ct);
 [HttpPost("api/workflows"),Authorize(Policy=Permissions.WorkWrite)] public async Task<object> Workflow(WorkflowCommand x,CancellationToken ct)=>new{id=await service.CreateWorkflowAsync(x,ct)};
 [HttpPost("api/workflows/{id:guid}/decision"),Authorize(Policy=Permissions.WorkApprove)] public async Task<IActionResult> Decide(Guid id,WorkflowDecisionCommand x,CancellationToken ct){await service.DecideWorkflowAsync(id,x,true,ct);return NoContent();}
 [HttpGet("api/work-notifications"),Authorize(Policy=Permissions.WorkRead)] public Task<IReadOnlyList<NotificationRow>> Notifications(string? type,string? module,string? severity,DateTimeOffset? from,DateTimeOffset? to,CancellationToken ct)=>service.NotificationsAsync(type,module,severity,from,to,ct);
 [HttpPost("api/work-notifications/read"),Authorize(Policy=Permissions.WorkRead)] public async Task<IActionResult> ReadAll(CancellationToken ct){await service.ReadNotificationAsync(null,ct);return NoContent();}
 [HttpPost("api/work-notifications/{id:guid}/read"),Authorize(Policy=Permissions.WorkRead)] public async Task<IActionResult> Read(Guid id,CancellationToken ct){await service.ReadNotificationAsync(id,ct);return NoContent();}
 [HttpGet("api/operational-calendar"),Authorize(Policy=Permissions.WorkRead)] public Task<IReadOnlyList<CalendarRow>> Calendar(DateTimeOffset from,DateTimeOffset to,string? module,Guid? responsibleId,string? priority,CancellationToken ct)=>service.CalendarAsync(from,to,module,responsibleId,priority,ct);
 [HttpGet("api/communication-outbox"),Authorize(Policy=Permissions.WorkRead)] public Task<IReadOnlyList<OutboxRow>> Outbox(string? status,string? channel,CancellationToken ct)=>service.OutboxAsync(status,channel,ct);
 [HttpGet("api/work-dashboard"),Authorize(Policy=Permissions.WorkRead)] public Task<WorkDashboard> Dashboard(CancellationToken ct)=>service.DashboardAsync(ct);
}
