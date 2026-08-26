using System.ComponentModel.DataAnnotations;

namespace Agro360.Application.Contracts;

public sealed record RuralHrRecord(Guid Id,string Kind,string Name,string Status,Guid? PersonId,Guid? TeamId,DateTimeOffset? StartsAt,DateTimeOffset? EndsAt,decimal Amount,DateTimeOffset UpdatedAt);
public sealed record RuralHrCommand([Required,MaxLength(40)] string Kind,[Required,MaxLength(180)] string Name,Guid? PersonId,Guid? TeamId,Guid? PropertyId,Guid? ResourceId,DateTimeOffset? StartsAt,DateTimeOffset? EndsAt,[Range(0,double.MaxValue)] decimal Amount=0,[MaxLength(2000)] string? Notes=null);
public sealed record PersonCommand([Required,MaxLength(180)] string Name,[Required,RegularExpression("^[0-9]{11,14}$")] string Document,[Required] Guid RoleId,[Required] Guid PropertyId,[EmailAddress] string? Email,[Phone] string? Phone,[MaxLength(1000)] string? Skills);
public sealed record TimeEntryCommand([Required] Guid PersonId,Guid? TeamId,[Required] Guid PropertyId,Guid? ResourceId,[Required] DateTimeOffset StartedAt,DateTimeOffset? EndedAt,[Range(0,1440)] int BreakMinutes,[Required,MaxLength(40)] string ActivityType,[MaxLength(1000)] string? Notes=null,string? OfflineId=null);
public sealed record TransportCommand([Required,MaxLength(180)] string Name,[Required] Guid TeamId,[Required] Guid VehicleId,[Required] Guid DriverId,[Range(1,500)] int Capacity,[Range(1,500)] int PassengerCount,DateTimeOffset StartsAt,DateTimeOffset EndsAt,[MaxLength(500)] string? Route);
public sealed record RuralHrDashboard(int ActivePeople,int ActiveTeams,decimal WorkedHours,decimal LaborCost,int ExpiredTrainings,int ExpiredPpe,int OpenIncidents,int OverdueActions,int TeamsInField,int CriticalAlerts);

public interface IRuralHrService
{
 Task<IReadOnlyList<RuralHrRecord>> ListAsync(string kind,string? status,CancellationToken ct);
 Task<Guid> SaveAsync(Guid? id,RuralHrCommand command,CancellationToken ct);
 Task<Guid> AddPersonAsync(PersonCommand command,CancellationToken ct);
 Task<Guid> RegisterTimeAsync(TimeEntryCommand command,CancellationToken ct);
 Task EndTimeAsync(Guid id,DateTimeOffset endedAt,CancellationToken ct);
 Task<Guid> AddTransportAsync(TransportCommand command,CancellationToken ct);
 Task ChangeStatusAsync(Guid id,string status,CancellationToken ct);
 Task<RuralHrDashboard> DashboardAsync(CancellationToken ct);
 Task<byte[]> ExportAsync(string kind,CancellationToken ct);
}
