using System.ComponentModel.DataAnnotations;

namespace Agro360.Application.Contracts;

public sealed record CooperativeRecord(Guid Id,string Kind,string Name,string Status,decimal Amount,DateTimeOffset UpdatedAt);
public sealed record CooperativeCommand(string Kind,[Required,MaxLength(180)] string Name,[Required] Guid PartyId,Guid? PropertyId,Guid? TraceabilityLotId,[Range(0,double.MaxValue)] decimal Amount=0,[MaxLength(2000)] string? Details=null);
public sealed record MemberCommand([Required,MaxLength(180)] string Name,[Required,RegularExpression("^[0-9]{11,14}$")] string Document,[Required] Guid OrganizationId,[Required] Guid ClassificationId,[Required] Guid PropertyId,[MaxLength(2000)] string? ProductiveProfile);
public sealed record CollectiveAllocation(Guid MemberId,[Range(0.01,double.MaxValue)] decimal Quantity);
public sealed record CollectivePurchaseCommand([Required,MaxLength(180)] string Name,[Required] Guid ProductId,[Range(0.01,double.MaxValue)] decimal Quantity,[MinLength(1)] IReadOnlyList<CollectiveAllocation> Allocations);
public sealed record CooperativeDashboard(int ActiveMembers,int ActivePrograms,decimal ContractedVolume,decimal DeliveredVolume,int OpenOffers,int OpenDemands,int CollectivePurchases,int PendingVisits,int ExpiringContracts,decimal PendingSettlements,int CreditPreAnalyses);

public interface ICooperativeService
{
 Task<IReadOnlyList<CooperativeRecord>> ListAsync(string kind,string? status,CancellationToken ct);
 Task<Guid> SaveAsync(Guid? id,CooperativeCommand command,CancellationToken ct);
 Task<Guid> AddMemberAsync(MemberCommand command,CancellationToken ct);
 Task<Guid> AddCollectivePurchaseAsync(CollectivePurchaseCommand command,CancellationToken ct);
 Task ChangeStatusAsync(Guid id,string status,CancellationToken ct);
 Task<CooperativeDashboard> DashboardAsync(CancellationToken ct);
 Task<object> ProducerDashboardAsync(CancellationToken ct);
}
