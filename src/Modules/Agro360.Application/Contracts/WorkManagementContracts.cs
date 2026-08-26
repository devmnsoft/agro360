using Agro360.SharedKernel;

namespace Agro360.Application.Contracts;

public sealed record WorkLookup(Guid Id,string Name);
public sealed record TaskCommand(string Title,string Description,Guid ResponsibleId,string Priority,DateTimeOffset DueAt,string Module,string? EntityType,Guid? EntityId);
public sealed record TaskTransition(string Status,string? Reason);
public sealed record OperationalTaskRow(Guid Id,string Title,string Description,string ResponsibleName,string Priority,string Status,DateTimeOffset DueAt,string Module,string? EntityType,Guid? EntityId,DateTimeOffset CreatedAt);
public sealed record TaskEventRow(string EventType,string? Notes,string ActorName,DateTimeOffset CreatedAt);
public sealed record AlertRow(Guid Id,string Title,string Description,string Severity,string Status,string Module,string OriginType,Guid? OriginId,DateTimeOffset CreatedAt,DateTimeOffset? ResolvedAt);
public sealed record RuleCommand(string Name,string Description,string Module,string Type,string ConditionJson,string Severity,string Action,bool Active);
public sealed record RuleRow(Guid Id,string Name,string Description,string Module,string Type,string Severity,string Action,bool Active,DateTimeOffset? LastExecutedAt);
public sealed record WorkflowCommand(string Name,string Module,string EntityType,Guid EntityId,Guid ApproverId,bool SegregationRequired);
public sealed record WorkflowRow(Guid Id,string Name,string Module,string EntityType,Guid EntityId,string Status,string RequesterName,string ApproverName,DateTimeOffset OpenedAt,DateTimeOffset? DecidedAt);
public sealed record WorkflowDecisionCommand(string Decision,string? Comment);
public sealed record NotificationRow(Guid Id,string Type,string Module,string Severity,string Title,string Message,string? SafeLink,DateTimeOffset CreatedAt,DateTimeOffset? ReadAt);
public sealed record CalendarRow(Guid Id,string Type,string Title,DateTimeOffset StartsAt,DateTimeOffset? EndsAt,string Module,string? Priority,string? SafeLink);
public sealed record WorkDashboard(long OpenTasks,long OverdueTasks,long CriticalTasks,long CriticalAlerts,long PendingApprovals,long UnreadNotifications,long CriticalStocks,int HealthScore);
public sealed record OutboxRow(Guid Id,string Channel,string Status,int Attempts,string? LastError,DateTimeOffset CreatedAt,DateTimeOffset? ProcessedAt);

public interface IWorkManagementService
{
 Task<IReadOnlyList<WorkLookup>> ActiveUsersAsync(string? search,CancellationToken ct);
 Task<PagedResult<OperationalTaskRow>> TasksAsync(string? search,string? status,string? priority,string? module,Guid? responsibleId,DateTimeOffset? from,DateTimeOffset? to,int page,int pageSize,CancellationToken ct);
 Task<Guid> CreateTaskAsync(TaskCommand command,CancellationToken ct); Task ChangeTaskAsync(Guid id,TaskTransition command,CancellationToken ct); Task<IReadOnlyList<TaskEventRow>> TaskHistoryAsync(Guid id,CancellationToken ct);
 Task<IReadOnlyList<AlertRow>> AlertsAsync(string? severity,string? status,string? module,CancellationToken ct); Task ChangeAlertAsync(Guid id,string action,CancellationToken ct); Task<int> EvaluateRulesAsync(CancellationToken ct);
 Task<IReadOnlyList<RuleRow>> RulesAsync(CancellationToken ct); Task<Guid> SaveRuleAsync(Guid? id,RuleCommand command,CancellationToken ct);
 Task<IReadOnlyList<WorkflowRow>> WorkflowsAsync(string? status,string? module,CancellationToken ct); Task<Guid> CreateWorkflowAsync(WorkflowCommand command,CancellationToken ct); Task DecideWorkflowAsync(Guid id,WorkflowDecisionCommand command,bool canApprove,CancellationToken ct);
 Task<IReadOnlyList<NotificationRow>> NotificationsAsync(string? type,string? module,string? severity,DateTimeOffset? from,DateTimeOffset? to,CancellationToken ct); Task ReadNotificationAsync(Guid? id,CancellationToken ct);
 Task<IReadOnlyList<CalendarRow>> CalendarAsync(DateTimeOffset from,DateTimeOffset to,string? module,Guid? responsibleId,string? priority,CancellationToken ct);
 Task<IReadOnlyList<OutboxRow>> OutboxAsync(string? status,string? channel,CancellationToken ct); Task<WorkDashboard> DashboardAsync(CancellationToken ct);
}
