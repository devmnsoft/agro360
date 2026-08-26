namespace Agro360.Application.Contracts;

public sealed record PlanSummary(Guid Id, string Name, string Description, decimal MonthlyPrice, decimal AnnualPrice, int UserLimit, int PropertyLimit, long StorageLimitMb, int DeviceLimit, string[] Modules, string[] PremiumFeatures, bool Active);
public sealed record PlanCommand(string Name, string Description, decimal MonthlyPrice, decimal AnnualPrice, int UserLimit, int PropertyLimit, long StorageLimitMb, int DeviceLimit, string[] Modules, string[] PremiumFeatures, bool Active);
public sealed record TenantSummary(Guid Id, string Slug, string Name, string Type, string Document, string ResponsibleName, string ResponsibleEmail, Guid PlanId, string PlanName, string Status, DateTimeOffset? ActivatedAt, DateTimeOffset? BlockedAt, string? BlockReason);
public sealed record TenantCommand(string Slug, string Name, string Type, string Document, string ResponsibleName, string ResponsibleEmail, Guid PlanId);
public sealed record TenantUpdateCommand(string Name, string Type, string ResponsibleName, string ResponsibleEmail, Guid PlanId);
public sealed record ReasonCommand(string Reason);
public sealed record UsageSummary(Guid TenantId, string TenantName, int ActiveUsers, int UserLimit, int Properties, int PropertyLimit, int Devices, int DeviceLimit, long StorageUsedMb, long StorageLimitMb, long TrackedLots, long Certificates, long OfflineRecords, long LedgerEvents, long ExportedReports);
public sealed record PlatformDashboard(long TotalOrganizations, long ActiveOrganizations, long SuspendedOrganizations, long NewThisMonth, long ActiveUsers, long NearLimit, long AboveLimit, long PendingInvitations, long RecentLogins, long SecurityAlerts, long UpgradeRequests, long SupportRequests);
public sealed record UserSummary(Guid Id, string Name, string Email, string Status, DateTimeOffset? LastAccess, string[] Roles);
public sealed record UserCommand(string Name, string Email, Guid[] RoleIds);
public sealed record RoleSummary(Guid Id, string Name, int Level, string[] Permissions, bool SystemRole);
public sealed record RoleCommand(string Name, int Level, string[] Permissions);
public sealed record InvitationSummary(Guid Id, string Email, string RoleName, string Status, DateTimeOffset ExpiresAt);
public sealed record InvitationCommand(string Email, Guid RoleId, int ValidForHours = 72);
public sealed record SessionSummary(Guid Id, string Device, string IpAddress, DateTimeOffset CreatedAt, DateTimeOffset LastSeenAt, DateTimeOffset? RevokedAt);
public sealed record DeviceSummary(Guid Id, string Name, string Platform, DateTimeOffset LastSeenAt, DateTimeOffset? RevokedAt);
public sealed record NotificationSummary(Guid Id, string Type, string Priority, string Title, string Message, string? Route, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt, DateTimeOffset? ArchivedAt, bool RequiresAction);
public sealed record OrganizationSettings(string OrganizationName, string UnitSystem, string Currency, string TimeZone, string MainCulture, string[] MainActivities, string StockParameters, string FinanceParameters, string TraceabilityParameters, string ComplianceParameters, string[] NotificationPreferences);
public sealed record UpgradeRequestCommand(Guid RequestedPlanId, string Reason);

public interface ISaasService
{
    Task<IReadOnlyList<TenantSummary>> GetTenantsAsync(CancellationToken ct); Task<Guid> CreateTenantAsync(TenantCommand command, Guid actorId, CancellationToken ct); Task UpdateTenantAsync(Guid id, TenantUpdateCommand command, Guid actorId, CancellationToken ct); Task SetTenantStatusAsync(Guid id, string status, string? reason, Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<PlanSummary>> GetPlansAsync(CancellationToken ct); Task<Guid> CreatePlanAsync(PlanCommand command, Guid actorId, CancellationToken ct); Task UpdatePlanAsync(Guid id, PlanCommand command, Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<UsageSummary>> GetPlatformUsageAsync(CancellationToken ct); Task<UsageSummary> GetUsageAsync(CancellationToken ct); Task<PlatformDashboard> GetDashboardAsync(CancellationToken ct);
    Task<TenantSummary> GetOrganizationAsync(CancellationToken ct); Task UpdateOrganizationAsync(TenantUpdateCommand command, Guid actorId, CancellationToken ct); Task<PlanSummary> GetCurrentPlanAsync(CancellationToken ct); Task<Guid> RequestUpgradeAsync(UpgradeRequestCommand command, Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<UserSummary>> GetUsersAsync(CancellationToken ct); Task<Guid> SaveUserAsync(Guid? id, UserCommand command, Guid actorId, CancellationToken ct); Task SetUserActiveAsync(Guid id, bool active, Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<RoleSummary>> GetRolesAsync(CancellationToken ct); Task<Guid> SaveRoleAsync(Guid? id, RoleCommand command, Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<InvitationSummary>> GetInvitationsAsync(CancellationToken ct); Task<Guid> InviteAsync(InvitationCommand command, Guid actorId, CancellationToken ct); Task ChangeInvitationAsync(Guid id, string action, Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<SessionSummary>> GetSessionsAsync(CancellationToken ct); Task RevokeSessionAsync(Guid id, Guid actorId, CancellationToken ct); Task<IReadOnlyList<DeviceSummary>> GetDevicesAsync(CancellationToken ct); Task RevokeDeviceAsync(Guid id, Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<NotificationSummary>> GetNotificationsAsync(string? type, string? priority, CancellationToken ct); Task ChangeNotificationAsync(Guid id, string action, CancellationToken ct);
    Task<OrganizationSettings> GetSettingsAsync(CancellationToken ct); Task UpdateSettingsAsync(OrganizationSettings settings, Guid actorId, CancellationToken ct);
}
