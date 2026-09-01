using System.ComponentModel.DataAnnotations;
namespace Agro360.Application.Contracts;

public sealed record CrmLead(Guid Id,string Name,string? Company,string? Document,string? Email,string Segment,string Source,string Status,decimal RevenuePotential,Guid? OwnerId,DateTimeOffset CreatedAt);
public sealed record LeadCommand([Required,MaxLength(160)]string Name,[MaxLength(160)]string? Company,[MaxLength(20)]string? Document,[EmailAddress,MaxLength(254)]string? Email,[Required,MaxLength(60)]string Segment,[Required,MaxLength(60)]string Source,[Range(0,double.MaxValue)]decimal RevenuePotential,Guid? OwnerId);
public sealed record DuplicateLead(Guid Id,string Name,string? Document,string? Email);
public sealed record CrmOpportunity(Guid Id,Guid LeadId,string Title,string Status,decimal RevenuePotential,decimal Probability,string? LossReason,Guid? OwnerId,DateOnly? ExpectedCloseOn);
public sealed record CrmOpportunityCommand([Required]Guid LeadId,[Required,MaxLength(180)]string Title,[Range(0,double.MaxValue)]decimal RevenuePotential,[Range(0,100)]decimal Probability,Guid? OwnerId,DateOnly? ExpectedCloseOn);
public sealed record OpportunityTransition([Required]string Status,[MaxLength(1000)]string? Reason,bool CreateTenant);
public sealed record CommercialProposal(Guid Id,string PublicCode,Guid? LeadId,Guid? CustomerTenantId,Guid? PlanId,string Status,decimal MonthlyValue,decimal AnnualValue,decimal Discount,decimal ImplementationValue,decimal SupportValue,decimal Total,DateOnly ValidUntil,DateTimeOffset? AcceptedAt);
public sealed record ProposalCommand(Guid? LeadId,Guid? CustomerTenantId,Guid? PlanId,[Range(1,100000)]int Users,[Range(1,100000)]int Units,[Range(0,double.MaxValue)]decimal MonthlyValue,[Range(0,double.MaxValue)]decimal Discount,[Range(0,double.MaxValue)]decimal ImplementationValue,[Range(0,double.MaxValue)]decimal SupportValue,[Required]DateOnly ValidUntil,[MaxLength(4000)]string? Notes);
public sealed record ProposalTransition([Required]string Status,[MaxLength(1000)]string? Reason);
public sealed record SaasContract(Guid Id,string PublicCode,Guid TenantId,Guid ProposalId,Guid PlanId,string Status,decimal MonthlyValue,decimal AnnualValue,DateOnly StartsOn,DateOnly EndsOn,string Sla);
public sealed record CustomerHealth(Guid TenantId,string TenantName,decimal AdoptionPercent,int CriticalTickets,int OverdueInvoices,int? Nps,int Score,string Risk,string Recommendation);

public interface ICrmSaasService
{
 Task<IReadOnlyList<SupportLookup>> PlansAsync(CancellationToken ct); Task<IReadOnlyList<CrmLead>> LeadsAsync(string? search,CancellationToken ct); Task<IReadOnlyList<DuplicateLead>> DuplicatesAsync(string? document,string? email,CancellationToken ct); Task<Guid> CreateLeadAsync(LeadCommand command,CancellationToken ct);
 Task<IReadOnlyList<CrmOpportunity>> OpportunitiesAsync(CancellationToken ct); Task<Guid> CreateOpportunityAsync(CrmOpportunityCommand command,CancellationToken ct); Task<Guid?> TransitionOpportunityAsync(Guid id,OpportunityTransition command,CancellationToken ct);
 Task<IReadOnlyList<CommercialProposal>> ProposalsAsync(CancellationToken ct); Task<Guid> CreateProposalAsync(ProposalCommand command,CancellationToken ct); Task TransitionProposalAsync(Guid id,ProposalTransition command,CancellationToken ct); Task<Guid> CreateContractAsync(Guid proposalId,DateOnly startsOn,DateOnly endsOn,CancellationToken ct);
 Task<IReadOnlyList<SaasContract>> ContractsAsync(CancellationToken ct); Task SuspendContractAsync(Guid id,string reason,CancellationToken ct); Task<IReadOnlyList<CustomerHealth>> HealthAsync(CancellationToken ct);
}
