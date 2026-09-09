namespace Agro360.Application.Contracts;

public sealed class PlanSummary
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public decimal MonthlyPrice { get; init; }
    public decimal AnnualPrice { get; init; }
    public int UserLimit { get; init; }
    public int PropertyLimit { get; init; }
    public long StorageLimitMb { get; init; }
    public int DeviceLimit { get; init; }
    public string[] Modules { get; init; } = [];
    public string[] PremiumFeatures { get; init; } = [];
    public bool Active { get; init; }
}
public sealed record PlanCommand(string Name, string Description, decimal MonthlyPrice, decimal AnnualPrice, int UserLimit, int PropertyLimit, long StorageLimitMb, int DeviceLimit, string[] Modules, string[] PremiumFeatures, bool Active);
public sealed class TenantSummary
{
    public Guid Id { get; init; }
    public string Slug { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public string Document { get; init; } = string.Empty;
    public string ResponsibleName { get; init; } = string.Empty;
    public string ResponsibleEmail { get; init; } = string.Empty;
    public Guid PlanId { get; init; }
    public string PlanName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime? ActivatedAt { get; init; }
    public DateTime? BlockedAt { get; init; }
    public string? BlockReason { get; init; }
}
public sealed record TenantCommand(string Slug, string Name, string Type, string Document, string ResponsibleName, string ResponsibleEmail, Guid PlanId);
public sealed record TenantCreated(Guid Id, InvitationCreated AdministratorInvitation);
public sealed record TenantUpdateCommand(string Name, string Type, string ResponsibleName, string ResponsibleEmail, Guid PlanId);
public sealed record ReasonCommand(string Reason);
public sealed class UsageSummary
{
    public Guid TenantId { get; init; }
    public string TenantName { get; init; } = string.Empty;
    public int ActiveUsers { get; init; }
    public int UserLimit { get; init; }
    public int Properties { get; init; }
    public int PropertyLimit { get; init; }
    public int Devices { get; init; }
    public int DeviceLimit { get; init; }
    public long StorageUsedMb { get; init; }
    public long StorageLimitMb { get; init; }
    public long TrackedLots { get; init; }
    public long Certificates { get; init; }
    public long OfflineRecords { get; init; }
    public long LedgerEvents { get; init; }
    public long ExportedReports { get; init; }
}
public sealed class PlatformDashboard
{
    public long TotalOrganizations { get; init; }
    public long ActiveOrganizations { get; init; }
    public long SuspendedOrganizations { get; init; }
    public long NewThisMonth { get; init; }
    public long ActiveUsers { get; init; }
    public long NearLimit { get; init; }
    public long AboveLimit { get; init; }
    public long PendingInvitations { get; init; }
    public long RecentLogins { get; init; }
    public long SecurityAlerts { get; init; }
    public long UpgradeRequests { get; init; }
    public long SupportRequests { get; init; }
}
public sealed class UserSummary
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime? LastAccess { get; init; }
    public string[] Roles { get; init; } = [];
}
public sealed record UserCommand(string Name, string Email, Guid[] RoleIds);
public sealed record UserStatusCommand(string Reason);
public sealed class RoleSummary
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public int Level { get; init; }
    public string[] Permissions { get; init; } = [];
    public bool SystemRole { get; init; }
}
public sealed record RoleCommand(string Name, int Level, string[] Permissions);
public sealed class InvitationSummary
{
    public Guid Id { get; init; }
    public string Email { get; init; } = string.Empty;
    public string RoleName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public DateTime ExpiresAt { get; init; }
    public string DeliveryStatus { get; init; } = string.Empty;
}
public sealed record InvitationCommand(string Email, Guid RoleId, int ValidForHours = 72);
public sealed record InvitationCreated(Guid Id, string ActivationToken, string DeliveryStatus);
public sealed record InvitationAcceptanceCommand(string Token, string Name, string Password);
public sealed record InvitationAcceptanceResult(string TenantSlug, string Email);
public sealed class SessionSummary { public Guid Id { get; init; } public string Device { get; init; } = string.Empty; public string IpAddress { get; init; } = string.Empty; public DateTime CreatedAt { get; init; } public DateTime LastSeenAt { get; init; } public DateTime? RevokedAt { get; init; } }
public sealed class DeviceSummary { public Guid Id { get; init; } public string Name { get; init; } = string.Empty; public string Platform { get; init; } = string.Empty; public DateTime LastSeenAt { get; init; } public DateTime? RevokedAt { get; init; } }
public sealed class NotificationSummary { public Guid Id { get; init; } public string Type { get; init; } = string.Empty; public string Priority { get; init; } = string.Empty; public string Title { get; init; } = string.Empty; public string Message { get; init; } = string.Empty; public string? Route { get; init; } public DateTime CreatedAt { get; init; } public DateTime? ReadAt { get; init; } public DateTime? ArchivedAt { get; init; } public bool RequiresAction { get; init; } }
public sealed class OrganizationSettings
{
    public string OrganizationName { get; init; } = string.Empty;
    public string UnitSystem { get; init; } = string.Empty;
    public string Currency { get; init; } = string.Empty;
    public string TimeZone { get; init; } = string.Empty;
    public string MainCulture { get; init; } = string.Empty;
    public string[] MainActivities { get; init; } = [];
    public string StockParameters { get; init; } = "{}";
    public string FinanceParameters { get; init; } = "{}";
    public string TraceabilityParameters { get; init; } = "{}";
    public string ComplianceParameters { get; init; } = "{}";
    public string[] NotificationPreferences { get; init; } = [];
}
public sealed record UpgradeRequestCommand(Guid RequestedPlanId, string Reason);
public sealed class BillingChargeSummary { public Guid Id { get; init; } public Guid TenantId { get; init; } public string TenantName { get; init; } = string.Empty; public string PlanName { get; init; } = string.Empty; public DateOnly Competence { get; init; } public DateOnly DueOn { get; init; } public decimal Amount { get; init; } public string Status { get; init; } = string.Empty; public string? Notes { get; init; } public DateOnly? PaidOn { get; init; } }
public sealed record BillingChargeCommand(Guid TenantId, Guid SubscriptionId, DateOnly Competence, DateOnly DueOn, decimal Amount, string? Notes);
public sealed record BillingStatusCommand(string Status, string Reason);
public sealed class FeatureFlagSummary { public Guid Id { get; init; } public string Code { get; init; } = string.Empty; public string Name { get; init; } = string.Empty; public string Description { get; init; } = string.Empty; public bool PlanEnabled { get; init; } public bool? TenantEnabled { get; init; } public string EffectiveOrigin { get; init; } = string.Empty; public DateTime? ExpiresAt { get; init; } }
public sealed record FeatureOverrideCommand(Guid TenantId, Guid FeatureId, bool Enabled, string Reason, DateTimeOffset? ExpiresAt);
public sealed class SaasAuditSummary { public Guid Id { get; init; } public Guid? TenantId { get; init; } public string? TenantName { get; init; } public Guid ActorId { get; init; } public string Action { get; init; } = string.Empty; public string EntityType { get; init; } = string.Empty; public Guid? EntityId { get; init; } public string? Reason { get; init; } public DateTime CreatedAt { get; init; } }

public interface ISaasService
{
    Task<IReadOnlyList<TenantSummary>> GetTenantsAsync(CancellationToken ct); Task<TenantCreated> CreateTenantAsync(TenantCommand command, Guid actorId, CancellationToken ct); Task UpdateTenantAsync(Guid id, TenantUpdateCommand command, Guid actorId, CancellationToken ct); Task SetTenantStatusAsync(Guid id, string status, string? reason, Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<PlanSummary>> GetPlansAsync(CancellationToken ct); Task<Guid> CreatePlanAsync(PlanCommand command, Guid actorId, CancellationToken ct); Task UpdatePlanAsync(Guid id, PlanCommand command, Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<UsageSummary>> GetPlatformUsageAsync(CancellationToken ct); Task<UsageSummary> GetUsageAsync(CancellationToken ct); Task<PlatformDashboard> GetDashboardAsync(CancellationToken ct);
    Task<TenantSummary> GetOrganizationAsync(CancellationToken ct); Task UpdateOrganizationAsync(TenantUpdateCommand command, Guid actorId, CancellationToken ct); Task<PlanSummary> GetCurrentPlanAsync(CancellationToken ct); Task<Guid> RequestUpgradeAsync(UpgradeRequestCommand command, Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<UserSummary>> GetUsersAsync(CancellationToken ct); Task<Guid> SaveUserAsync(Guid? id, UserCommand command, Guid actorId, CancellationToken ct); Task SetUserActiveAsync(Guid id, bool active, string reason, Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<RoleSummary>> GetRolesAsync(CancellationToken ct); Task<Guid> SaveRoleAsync(Guid? id, RoleCommand command, Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<InvitationSummary>> GetInvitationsAsync(CancellationToken ct); Task<InvitationCreated> InviteAsync(InvitationCommand command, Guid actorId, CancellationToken ct); Task<InvitationCreated?> ChangeInvitationAsync(Guid id, string action, Guid actorId, CancellationToken ct); Task<InvitationAcceptanceResult> AcceptInvitationAsync(InvitationAcceptanceCommand command, CancellationToken ct);
    Task<IReadOnlyList<SessionSummary>> GetSessionsAsync(CancellationToken ct); Task RevokeSessionAsync(Guid id, Guid actorId, CancellationToken ct); Task<IReadOnlyList<DeviceSummary>> GetDevicesAsync(CancellationToken ct); Task RevokeDeviceAsync(Guid id, Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<NotificationSummary>> GetNotificationsAsync(string? type, string? priority, CancellationToken ct); Task ChangeNotificationAsync(Guid id, string action, CancellationToken ct);
    Task<OrganizationSettings> GetSettingsAsync(CancellationToken ct); Task UpdateSettingsAsync(OrganizationSettings settings, Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<BillingChargeSummary>> GetChargesAsync(CancellationToken ct); Task<Guid> CreateChargeAsync(BillingChargeCommand command, Guid actorId, CancellationToken ct); Task ChangeChargeStatusAsync(Guid id, BillingStatusCommand command, Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<FeatureFlagSummary>> GetFeatureFlagsAsync(Guid tenantId, CancellationToken ct); Task SetFeatureOverrideAsync(FeatureOverrideCommand command, Guid actorId, CancellationToken ct);
    Task<IReadOnlyList<SaasAuditSummary>> GetAuditAsync(Guid? tenantId, CancellationToken ct);
}
