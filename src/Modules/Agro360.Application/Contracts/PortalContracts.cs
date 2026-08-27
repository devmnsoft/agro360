namespace Agro360.Application.Contracts;

public sealed record PortalInvitationCommand(string Name,string Email,string Profile,string EntityType,Guid EntityId,DateTimeOffset ExpiresAt);
public sealed record PortalInvitationCreated(Guid Id,string AcceptanceToken,DateTimeOffset ExpiresAt);
public sealed record PortalInvitationRow(Guid Id,string Name,string Email,string Profile,string EntityType,string EntityLabel,string Status,DateTimeOffset ExpiresAt,DateTimeOffset CreatedAt);
public sealed record AcceptPortalInvitationCommand(string Token,string Password,bool AcceptTerms);
public sealed record PortalLoginCommand(string TenantSlug,string Email,string Password);
public sealed record PortalAuthentication(Guid TenantId,Guid UserId,string Name,string Profile,string AccessToken,string RefreshToken,DateTimeOffset ExpiresAt);
public sealed record PortalDashboard(string Name,string Profile,IReadOnlyList<PortalMetric> Metrics,IReadOnlyList<PortalAnnouncement> Announcements,IReadOnlyList<PortalActivity> Activities);
public sealed record PortalMetric(string Code,string Label,long Value,string? Target);
public sealed record PortalActivity(string Type,string Description,DateTimeOffset OccurredAt);
public sealed record PortalAnnouncement(Guid Id,string Title,string Summary,string Severity,DateTimeOffset PublishedAt,bool Read);
public sealed record MarketplaceFilter(string? Search,string? Crop,string? Region,string? Certification,string? Unit,decimal? MaximumPrice);
public sealed record MarketplaceListing(Guid Id,string Product,string? Crop,string? Harvest,string? Region,string Unit,decimal AvailableQuantity,decimal? UnitPrice,string CommercialTerms,IReadOnlyList<string> Certifications);
public sealed record QuoteItemCommand(Guid ListingId,decimal Quantity);
public sealed record CreateQuoteCommand(string ContactName,string ContactEmail,string? Notes,IReadOnlyList<QuoteItemCommand> Items);
public sealed record PortalRequestCommand(string Type,string Subject,string Description,string Priority);
public sealed record PortalRequestRow(Guid Id,string Protocol,string Type,string Subject,string Status,string Priority,DateTimeOffset CreatedAt,DateTimeOffset UpdatedAt);
public interface IPortalService
{
 Task<IReadOnlyList<PortalInvitationRow>> InvitationsAsync(string? status,CancellationToken ct); Task<PortalInvitationCreated> InviteAsync(PortalInvitationCommand command,CancellationToken ct); Task RevokeInvitationAsync(Guid id,string reason,CancellationToken ct); Task<PortalAuthentication> AcceptInvitationAsync(AcceptPortalInvitationCommand command,CancellationToken ct); Task<PortalAuthentication> LoginAsync(PortalLoginCommand command,CancellationToken ct);
 Task<PortalDashboard> DashboardAsync(CancellationToken ct); Task<IReadOnlyList<MarketplaceListing>> MarketplaceAsync(MarketplaceFilter filter,CancellationToken ct); Task<Guid> RequestQuoteAsync(CreateQuoteCommand command,CancellationToken ct); Task<IReadOnlyList<PortalRequestRow>> RequestsAsync(CancellationToken ct); Task<Guid> CreateRequestAsync(PortalRequestCommand command,CancellationToken ct); Task MarkAnnouncementReadAsync(Guid id,CancellationToken ct);
}
