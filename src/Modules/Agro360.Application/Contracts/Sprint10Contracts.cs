namespace Agro360.Application.Contracts;

public sealed record TraceableLotCommand(string Code,Guid ProductId,Guid? PropertyId,Guid? PlotId,string Producer,string? Cooperative,DateTimeOffset HarvestedAt,decimal Quantity,string Unit,string? Notes);
public sealed record LotEventCommand(string EventType,string Payload,Guid? SourceLotId,decimal? CompositionQuantity);
public sealed record ComplianceEventCommand(Guid LotId,string Step,string Responsible,DateTimeOffset StartedAt,DateTimeOffset EndedAt,decimal? Temperature,string? Equipment,string? Evidence,string? Notes);
public sealed record CertificateCommand(Guid LotId);
public sealed record RouteCommand(string Code,string Name,string Modal,Guid OriginPointId,Guid DestinationPointId,decimal EstimatedCost,int EstimatedMinutes,string? Notes);
public sealed record TripRegionalCommand(Guid RouteId,string Number,Guid? VehicleId,DateTimeOffset PlannedStart,bool InterdictionAuthorized);
public sealed record RegionalOccurrenceCommand(string Type,string Description,DateTimeOffset OccurredAt,decimal? Latitude,decimal? Longitude);
public sealed record SalesNetworkPartnerCommand(string Name,string Type,string? Document,string? Contact,bool Active);
public sealed record CommissionRuleCommand(Guid PartnerId,string Role,string CalculationType,decimal Value,DateOnly EffectiveFrom,DateOnly? EffectiveTo);
public sealed record SalesNetworkSplitParticipant(Guid PartnerId,string Role,decimal Value,bool Percentage);
public sealed record SplitCalculationCommand(Guid SaleId,decimal GrossAmount,IReadOnlyList<SalesNetworkSplitParticipant> Participants);
public interface IImmutableLedgerService { Task<Guid> AppendAsync(string entity,Guid entityId,string eventType,object payload,string status,CancellationToken ct); Task<dynamic> ValidateAsync(CancellationToken ct); }
public interface IPaymentSplitProvider { Task<string> RegisterAsync(Guid splitId,decimal amount,CancellationToken ct); }
public interface ISprint10Service
{
 Task<IReadOnlyList<dynamic>> LotsAsync(CancellationToken ct); Task<Guid> SaveLotAsync(Guid? id,TraceableLotCommand command,CancellationToken ct); Task AddLotEventAsync(Guid id,LotEventCommand command,CancellationToken ct); Task<IReadOnlyList<dynamic>> TimelineAsync(Guid id,CancellationToken ct);
 Task<Guid> AddComplianceAsync(ComplianceEventCommand command,CancellationToken ct); Task<dynamic?> CertificateAsync(string code,CancellationToken ct); Task<(Guid Id,string Code)> CreateCertificateAsync(CertificateCommand command,CancellationToken ct);
 Task<IReadOnlyList<dynamic>> RoutesAsync(CancellationToken ct); Task<Guid> AddRouteAsync(RouteCommand command,CancellationToken ct); Task<Guid> AddTripAsync(TripRegionalCommand command,CancellationToken ct); Task AddOccurrenceAsync(Guid tripId,RegionalOccurrenceCommand command,CancellationToken ct);
 Task<IReadOnlyList<dynamic>> PartnersAsync(CancellationToken ct); Task<Guid> AddPartnerAsync(SalesNetworkPartnerCommand command,CancellationToken ct); Task<Guid> AddCommissionRuleAsync(CommissionRuleCommand command,CancellationToken ct); Task<dynamic> CalculateSplitAsync(SplitCalculationCommand command,CancellationToken ct); Task ApproveSplitAsync(Guid id,CancellationToken ct); Task<dynamic> DashboardAsync(CancellationToken ct);
}
