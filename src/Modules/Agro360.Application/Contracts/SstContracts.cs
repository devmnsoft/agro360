using System.ComponentModel.DataAnnotations;

namespace Agro360.Application.Contracts;

public sealed record SstLookup(Guid Id, string Name);
public sealed record SstDashboard(int ActiveWorkers,int WorkersWithPendingItems,int EpiDelivered,int EpiExpired,int EpiDueSoon,int TrainingExpired,int TrainingDueSoon,int ExamsExpired,int ExamsDueSoon,int CriticalRisks,int OpenIncidents,int SeriousIncidents,int NearMisses,int OverdueActions,int FailedChecklists,decimal CompliancePercent);
public sealed record SstWorker(Guid Id,string Name,string? InternalCode,string RoleName,string AreaName,string Status,DateOnly? AdmissionDate,string EmploymentType);
public sealed record SstWorkerCommand([Required,MaxLength(160)]string Name,[MaxLength(40)]string? Document,[MaxLength(40)]string? InternalCode,[Required]Guid JobRoleId,[Required]Guid WorkAreaId,Guid? PropertyId,[Required]string Status,DateOnly? AdmissionDate,[Required,MaxLength(40)]string EmploymentType,[MaxLength(160)]string? Contact,[MaxLength(2000)]string? Notes);
public sealed record SstRisk(Guid Id,string Name,string Type,string AreaName,string? RoleName,int Severity,int Probability,int Level,string Status,string? PreventiveMeasure);
public sealed record SstRiskCommand([Required,MaxLength(160)]string Name,[Required,MaxLength(2000)]string Description,[Required]string Type,[Range(1,5)]int Severity,[Range(1,5)]int Probability,[Required]Guid WorkAreaId,Guid? JobRoleId,[MaxLength(160)]string? Operation,[MaxLength(2000)]string? PreventiveMeasure,Guid? RecommendedEpiId,Guid? RecommendedTrainingId,[Required]string Status,Guid? ResponsibleId,Guid? EvidenceDocumentId);
public sealed record EpiDeliveryCommand([Required]Guid WorkerId,[Required]Guid EpiId,[Range(typeof(decimal),"0.01","999999")]decimal Quantity,DateOnly DeliveredOn,DateOnly? ReplaceOn,[Required,MaxLength(500)]string Reason,[MaxLength(300)]string? Acceptance,Guid? EvidenceDocumentId);
public sealed record SstIncident(Guid Id,string Code,string Type,DateTimeOffset OccurredAt,string Location,string AreaName,string Description,int Severity,string Status,bool InvestigationRequired);
public sealed record SstIncidentCommand([Required]string Type,DateTimeOffset OccurredAt,[Required,MaxLength(300)]string Location,[Required]Guid WorkAreaId,Guid? WorkerId,[Required,MaxLength(4000)]string Description,[Range(1,5)]int Severity,[MaxLength(2000)]string? ProbableCause,[Required,MaxLength(2000)]string ImmediateAction,Guid ResponsibleId,bool InvestigationRequired,bool CorrectiveActionRequired,bool ExternalCommunicationRequired);
public sealed record SstChecklistRunCommand([Required]Guid TemplateId,Guid? WorkerId,[MaxLength(300)]string? Location,[Required,MinLength(1)]IReadOnlyList<SstChecklistAnswerCommand> Answers);
public sealed record SstChecklistAnswerCommand([Required]Guid ItemId,string? Answer,Guid? EvidenceDocumentId);

public interface ISstService
{
 Task<SstDashboard> DashboardAsync(CancellationToken ct); Task<IReadOnlyList<SstLookup>> LookupsAsync(string kind,string? search,CancellationToken ct);
 Task<IReadOnlyList<SstWorker>> WorkersAsync(string? search,string? status,int page,int pageSize,CancellationToken ct); Task<Guid> CreateWorkerAsync(SstWorkerCommand command,CancellationToken ct);
 Task<IReadOnlyList<SstRisk>> RisksAsync(string? search,string? status,int page,int pageSize,CancellationToken ct); Task<Guid> CreateRiskAsync(SstRiskCommand command,CancellationToken ct);
 Task<Guid> DeliverEpiAsync(EpiDeliveryCommand command,CancellationToken ct); Task<IReadOnlyList<SstIncident>> IncidentsAsync(string? search,string? status,int page,int pageSize,CancellationToken ct); Task<Guid> CreateIncidentAsync(SstIncidentCommand command,CancellationToken ct);
 Task<Guid> ExecuteChecklistAsync(SstChecklistRunCommand command,CancellationToken ct);
}
