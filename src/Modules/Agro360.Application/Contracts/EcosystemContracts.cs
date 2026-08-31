namespace Agro360.Application.Contracts;

public sealed record MarketplaceModule(Guid Id,string Code,string Name,string Description,string[] Segments,string[] Plans,string[] Dependencies,bool Official,bool Featured,string Status);
public sealed record MarketplaceModuleCommand(string Code,string Name,string Description,string[] Segments,string[] Plans,string[] Dependencies,bool Official,bool Featured);
public sealed record ModuleActivationCommand(Guid ModuleId,string? Justification,DateTimeOffset? TrialEndsAt);
public sealed record PartnerCommand(string Name,string Type,string Document,string Email,string Coverage,string[] Segments);
public sealed record PartnerAccessCommand(Guid PartnerId,Guid TenantId,string[] Scopes,string[] Modules,DateTimeOffset ExpiresAt);
public sealed record ExternalAppCommand(string Name,string Description,int RequestsPerMinute);
public sealed record PlatformApiKeyCreated(Guid Id,string Key,string Prefix);
public sealed record PlatformApiKeyCommand(Guid AppId,string Name,string[] Scopes,DateTimeOffset? ExpiresAt);
public sealed record WebhookSubscriptionCommand(Guid AppId,string Url,string[] Events,int MaximumAttempts=5);
public sealed record EcosystemDashboard(long Modules,long PendingRequests,long ActivePartners,long ActiveApps,long ActiveWebhooks,long FailedDeliveries);

public interface IEcosystemService
{
 Task<EcosystemDashboard> DashboardAsync(CancellationToken ct); Task<IReadOnlyList<MarketplaceModule>> ModulesAsync(CancellationToken ct); Task<Guid> CreateModuleAsync(MarketplaceModuleCommand x,Guid actor,CancellationToken ct); Task<Guid> RequestModuleAsync(ModuleActivationCommand x,Guid actor,CancellationToken ct); Task DecideModuleAsync(Guid id,bool approve,string reason,Guid actor,CancellationToken ct);
 Task<object> PartnersAsync(CancellationToken ct); Task<Guid> CreatePartnerAsync(PartnerCommand x,Guid actor,CancellationToken ct); Task SetPartnerBlockedAsync(Guid id,bool blocked,string reason,Guid actor,CancellationToken ct); Task<Guid> GrantPartnerAsync(PartnerAccessCommand x,Guid actor,CancellationToken ct); Task RevokePartnerAsync(Guid id,string reason,Guid actor,CancellationToken ct);
 Task<object> AppsAsync(CancellationToken ct); Task<Guid> CreateAppAsync(ExternalAppCommand x,Guid actor,CancellationToken ct); Task<PlatformApiKeyCreated> CreateKeyAsync(PlatformApiKeyCommand x,Guid actor,CancellationToken ct); Task RevokeKeyAsync(Guid id,Guid actor,CancellationToken ct);
 Task<object> WebhooksAsync(CancellationToken ct); Task<Guid> CreateWebhookAsync(WebhookSubscriptionCommand x,Guid actor,CancellationToken ct); Task<object> LogsAsync(CancellationToken ct); Task<object> DeveloperDocsAsync(CancellationToken ct); Task<object> CommercialCatalogAsync(CancellationToken ct);
}
