using System.ComponentModel.DataAnnotations;

namespace Agro360.Application.Contracts;

public sealed record SupportLookup(Guid Id, string Name);
public sealed record SupportTicket(Guid Id,string PublicCode,string RequesterName,string RequesterProfile,string Channel,string Category,string? Subcategory,string? Module,string Title,string Description,string Priority,string Severity,string Status,string? AssigneeName,DateTimeOffset FirstResponseDueAt,DateTimeOffset ResolutionDueAt,DateTimeOffset OpenedAt,DateTimeOffset? FirstRespondedAt,DateTimeOffset? ResolvedAt,DateTimeOffset? ClosedAt,string? CancellationReason,int? Rating,bool SlaBreached);
public sealed record TicketCommand([Required,MaxLength(180)]string Title,[Required,MaxLength(4000)]string Description,[Required,MaxLength(60)]string Category,[MaxLength(80)]string? Subcategory,[MaxLength(80)]string? Module,[Required,MaxLength(20)]string Priority,[Required,MaxLength(20)]string Severity,[Required,MaxLength(30)]string Channel);
public sealed record TicketTransition([Required] string Status,[MaxLength(1000)]string? Reason,[MaxLength(4000)]string? Resolution,Guid? AssigneeId,[MaxLength(80)]string? Area);
public sealed record TicketCommentCommand([Required,MaxLength(4000)]string Body,bool Internal);
public sealed record SupportEvent(Guid Id,string EventType,string? Description,string ActorName,DateTimeOffset CreatedAt);
public sealed record SlaPolicy(Guid Id,string Name,string? Plan,string? ContractType,string? Category,string Priority,string Severity,int FirstResponseMinutes,int ResolutionMinutes,bool PauseWaitingCustomer,bool Active);
public sealed record SlaPolicyCommand([Required,MaxLength(120)]string Name,[MaxLength(80)]string? Plan,[MaxLength(80)]string? ContractType,[MaxLength(60)]string? Category,[Required]string Priority,[Required]string Severity,[Range(1,525600)]int FirstResponseMinutes,[Range(1,525600)]int ResolutionMinutes,bool PauseWaitingCustomer,bool Active);
public sealed record KnowledgeArticle(Guid Id,string Slug,string Title,string Type,string? Module,string Audience,string Status,int Views,int Helpful,int NotHelpful,DateTimeOffset? PublishedAt);
public sealed record ArticleCommand([Required,MaxLength(180)]string Title,[Required,MaxLength(20000)]string Content,[Required,MaxLength(40)]string Type,[MaxLength(80)]string? Module,[Required,MaxLength(40)]string Audience,bool Global);
public sealed record ArticleFeedbackCommand(bool Helpful);
public sealed record ImplementationProject(Guid Id,string Name,string TenantName,string Status,DateOnly StartsOn,DateOnly DueOn,int Progress,int OpenRisks);
public sealed record ImplementationCommand([Required]Guid TenantId,[Required,MaxLength(160)]string Name,[Required]DateOnly StartsOn,[Required]DateOnly DueOn,Guid? OwnerId);
public sealed record PhaseCompletionCommand(bool ChecklistCompleted,[MaxLength(2000)]string? Evidence,[MaxLength(1000)]string? Reason);
public sealed record TrainingTrack(Guid Id,string Name,string Profile,bool Mandatory,bool Active,int Assignments,int Completed);
public sealed record TrainingCommand([Required,MaxLength(160)]string Name,[Required,MaxLength(80)]string Profile,bool Mandatory,bool Active,[Range(1,1000)]int ValidityDays);
public sealed record TrainingCompletionCommand([Range(1,5)]int Rating);
public sealed record CustomerFeedback(Guid Id,string Type,string? Module,int? Score,string Message,string Severity,string Status,DateTimeOffset CreatedAt);
public sealed record FeedbackCommand([Required]string Type,[MaxLength(80)]string? Module,[Range(0,10)]int? Score,[Required,MaxLength(4000)]string Message,[Required]string Severity,Guid? TicketId);
public sealed record BacklogItem(Guid Id,string Title,string Origin,string? Module,string Priority,string Impact,string Status,string? DecisionReason);
public sealed record BacklogCommand([Required,MaxLength(180)]string Title,[Required,MaxLength(4000)]string Description,[Required]string Origin,[MaxLength(80)]string? Module,[Required]string Priority,[Required]string Impact,[Range(0,100000)]int? EstimatedHours);
public sealed record ReleaseNote(Guid Id,string Version,string Title,string? Module,string Audience,string Status,DateTimeOffset? PublishedAt,bool Read);
public sealed record ReleaseNoteCommand([Required,MaxLength(40)]string Version,[Required,MaxLength(180)]string Title,[Required,MaxLength(20000)]string Content,[MaxLength(80)]string? Module,[Required]string Audience,DateTimeOffset? PublishAt);
public sealed record SupportDashboard(int OpenTickets,int CriticalTickets,int BreachedTickets,int ReopenedTickets,decimal AverageFirstResponseHours,decimal AverageResolutionHours,decimal SlaCompliancePercent,int FeedbackCount,decimal? NpsAverage,int ActiveImplementations,int LateImplementations,int PendingTrainings,int OpenBacklog,int DeliveredBacklog);

public interface ISupportCustomerSuccessService
{
 Task<SupportDashboard> DashboardAsync(CancellationToken ct); Task<IReadOnlyList<SupportLookup>> TenantsAsync(string? search,CancellationToken ct); Task<IReadOnlyList<SupportLookup>> AgentsAsync(string? search,CancellationToken ct);
 Task<IReadOnlyList<SupportTicket>> TicketsAsync(string? search,string? status,string? priority,int page,int pageSize,CancellationToken ct); Task<Guid> CreateTicketAsync(TicketCommand command,CancellationToken ct); Task<SupportTicket?> TicketAsync(Guid id,CancellationToken ct); Task TransitionTicketAsync(Guid id,TicketTransition command,CancellationToken ct); Task CommentAsync(Guid id,TicketCommentCommand command,CancellationToken ct); Task<IReadOnlyList<SupportEvent>> EventsAsync(Guid id,CancellationToken ct);
 Task<IReadOnlyList<SlaPolicy>> SlaAsync(CancellationToken ct); Task<Guid> SaveSlaAsync(SlaPolicyCommand command,CancellationToken ct);
 Task<IReadOnlyList<KnowledgeArticle>> ArticlesAsync(string? search,string? module,bool publicOnly,CancellationToken ct); Task<Guid> CreateArticleAsync(ArticleCommand command,CancellationToken ct); Task PublishArticleAsync(Guid id,bool archive,CancellationToken ct); Task RateArticleAsync(Guid id,ArticleFeedbackCommand command,CancellationToken ct);
 Task<IReadOnlyList<ImplementationProject>> ImplementationsAsync(CancellationToken ct); Task<Guid> CreateImplementationAsync(ImplementationCommand command,CancellationToken ct); Task CompletePhaseAsync(Guid id,PhaseCompletionCommand command,CancellationToken ct);
 Task<IReadOnlyList<TrainingTrack>> TrainingsAsync(CancellationToken ct); Task<Guid> CreateTrainingAsync(TrainingCommand command,CancellationToken ct); Task CompleteTrainingAsync(Guid assignmentId,TrainingCompletionCommand command,CancellationToken ct);
 Task<IReadOnlyList<CustomerFeedback>> FeedbackAsync(CancellationToken ct); Task<Guid> CreateFeedbackAsync(FeedbackCommand command,CancellationToken ct); Task<Guid> ConvertFeedbackAsync(Guid id,CancellationToken ct);
 Task<IReadOnlyList<BacklogItem>> BacklogAsync(CancellationToken ct); Task<Guid> CreateBacklogAsync(BacklogCommand command,CancellationToken ct);
 Task<IReadOnlyList<ReleaseNote>> ReleasesAsync(CancellationToken ct); Task<Guid> CreateReleaseAsync(ReleaseNoteCommand command,CancellationToken ct); Task PublishReleaseAsync(Guid id,CancellationToken ct); Task ReadReleaseAsync(Guid id,CancellationToken ct);
}
