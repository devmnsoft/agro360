using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Agro360.Application;
using Agro360.Application.Abstractions;
using Agro360.Application.Contracts;
using Agro360.Domain.Tenancy;
using Agro360.Infrastructure.Persistence;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Agro360.Infrastructure.Services;

public sealed partial class SaasService(DatabaseExecutor db, ITenantContext tenant, IPasswordHasher passwordHasher, ILogger<SaasService> logger) : ISaasService
{
    public Task<IReadOnlyList<TenantSummary>> GetTenantsAsync(CancellationToken ct) => System("list-tenants", async (c, t) => (IReadOnlyList<TenantSummary>)(await c.QueryAsync<TenantSummary>("select x.id,x.slug,x.name,s.organization_type type,s.document,s.responsible_name ResponsibleName,s.responsible_email ResponsibleEmail,s.plan_id PlanId,p.name PlanName,s.status,s.activated_at ActivatedAt,s.blocked_at BlockedAt,s.block_reason BlockReason from agro360.tenancy_tenants x join agro360.saas_organizations s on s.tenant_id=x.id join agro360.saas_plans p on p.id=s.plan_id where x.deleted_at is null order by x.name", transaction: t)).ToArray(), ct);
    public Task<TenantCreated> CreateTenantAsync(TenantCommand command, Guid actorId, CancellationToken ct) => System("create-tenant", async (c, t) =>
    {
        ValidateTenant(command);
        var plan = await c.QuerySingleOrDefaultAsync<PlanProvisioningLookup>("select id,name,monthly_price MonthlyPrice,modules from agro360.saas_plans where id=@PlanId and active", new { command.PlanId }, t)
            ?? throw new InvalidOperationException("Plano inativo ou inexistente não pode ser atribuído.");
        var id = Guid.CreateVersion7();
        var roleId = Guid.CreateVersion7();
        var invitationId = Guid.CreateVersion7();
        var activationToken = CreateInvitationToken(id);
        await c.ExecuteAsync(
            """
            insert into agro360.tenancy_tenants(id,slug,name,status,plan_code,created_at) values(@Id,@Slug,@Name,2,upper(regexp_replace(@PlanName,'[^a-zA-Z0-9]+','_','g')),now());
            select set_config('app.tenant_id',@TenantContext,true);
            insert into agro360.saas_organizations(tenant_id,organization_type,document,responsible_name,responsible_email,plan_id,status,onboarding_status) values(@Id,@Type,@Document,@ResponsibleName,@ResponsibleEmail,@PlanId,'IMPLEMENTING','ADMINISTRATOR');
            insert into agro360.saas_usage_metrics(tenant_id) values(@Id);
            insert into agro360.saas_organization_settings(tenant_id,main_culture,main_activities,notification_preferences) values(@Id,'pt-BR',array[@Type],array['SYSTEM','SECURITY']);
            insert into agro360.saas_subscriptions(tenant_id,plan_id,status,cycle,starts_on,contracted_value,discount,auto_renew,created_by) values(@Id,@PlanId,'ACTIVE','MONTHLY',current_date,@MonthlyPrice,0,false,@Actor);
            insert into agro360.platform_tenants(id,legal_name,trade_name,normalized_document,customer_type,primary_segment,primary_email,legal_contact,status) values(@Id,@Name,@Name,@Document,@Type,@Type,@ResponsibleEmail,@ResponsibleName,'IMPLEMENTING');
            insert into agro360.platform_tenant_settings(tenant_id,language,currency,time_zone,preferences) values(@Id,'pt-BR','BRL','America/Sao_Paulo','{"sourceOfTruth":"saas_organizations"}');
            insert into agro360.identity_roles(id,tenant_id,code,name,is_system) values(@RoleId,@Id,'tenant-administrator','Administrador do Cliente',true);
            insert into agro360.saas_role_metadata(tenant_id,role_id,level) values(@Id,@RoleId,100);
            insert into agro360.identity_role_permissions(tenant_id,role_id,permission_id) select @Id,@RoleId,p.id from agro360.identity_permissions p where p.code=any(@AdministratorPermissions);
            insert into agro360.saas_invitations(id,tenant_id,email,role_id,token_hash,expires_at,invited_by,delivery_status) values(@InvitationId,@Id,@ResponsibleEmail,@RoleId,@TokenHash,now()+interval '72 hours',@Actor,'PENDING_PROVIDER');
            insert into agro360.audit_saas_events(id,tenant_id,actor_id,event_type,details) values(gen_random_uuid(),@Id,@Actor,'TENANT_CREATED',jsonb_build_object('planId',@PlanId,'administratorInvitationId',@InvitationId,'communication','PENDING_PROVIDER'));
            """,
            new
            {
                Id = id,
                Slug = command.Slug.Trim().ToLowerInvariant(),
                Name = command.Name.Trim(),
                command.Type,
                Document = SaasGovernanceRules.NormalizeAndValidateDocument(command.Document),
                ResponsibleName = command.ResponsibleName.Trim(),
                ResponsibleEmail = command.ResponsibleEmail.Trim().ToLowerInvariant(),
                command.PlanId,
                PlanName = plan.Name,
                plan.MonthlyPrice,
                Actor = actorId,
                TenantContext = id.ToString(),
                RoleId = roleId,
                AdministratorPermissions = Permissions.Administrator.ToArray(),
                InvitationId = invitationId,
                TokenHash = HashInvitationToken(activationToken)
            }, t);
        return new TenantCreated(id, new InvitationCreated(invitationId, activationToken, "PENDING_PROVIDER"));
    }, ct);
    public Task UpdateTenantAsync(Guid id, TenantUpdateCommand command, Guid actorId, CancellationToken ct) => System("update-tenant", async (c, t) =>
    {
        ValidateUpdate(command);
        if (!await c.ExecuteScalarAsync<bool>("select exists(select 1 from agro360.saas_plans where id=@PlanId and active)", new { command.PlanId }, t))
            throw new InvalidOperationException("Plano inativo ou inexistente não pode ser atribuído.");
        var changed = await c.ExecuteScalarAsync<int>(
            """
            with previous as (
                select o.plan_id
                from agro360.saas_organizations o
                where o.tenant_id = @Id
                for update
            ), tenant_changed as (
                update agro360.tenancy_tenants set name = @Name, updated_at = now()
                where id = @Id and exists(select 1 from previous)
                returning id
            ), organization_changed as (
                update agro360.saas_organizations
                set organization_type = @Type, responsible_name = @ResponsibleName,
                    responsible_email = @ResponsibleEmail, plan_id = @PlanId, updated_at = now()
                where tenant_id = @Id and exists(select 1 from tenant_changed)
                returning tenant_id
            ), audited as (
                insert into agro360.audit_saas_events(id, tenant_id, actor_id, event_type, details)
                select gen_random_uuid(), @Id, @Actor, 'TENANT_UPDATED',
                       jsonb_build_object('previousPlanId', previous.plan_id, 'planId', @PlanId)
                from previous join organization_changed on true
                returning 1
            )
            select count(*)::int from audited;
            """,
            new { Id = id, Name = command.Name.Trim(), command.Type, command.ResponsibleName, command.ResponsibleEmail, command.PlanId, Actor = actorId }, t);
        if (changed != 1) throw new KeyNotFoundException("Organização não encontrada.");
    }, ct);
    public Task SetTenantStatusAsync(Guid id, string status, string? reason, Guid actorId, CancellationToken ct) => System("tenant-status", async (c, t) => { var current = await c.QuerySingleOrDefaultAsync<string>("select status from agro360.saas_organizations where tenant_id=@Id", new { id }, t) ?? throw new KeyNotFoundException("Organização não encontrada."); SaasGovernanceRules.EnsureStatusTransition(current, status, reason); var n = await c.ExecuteAsync("update agro360.saas_organizations set status=@Status,activated_at=case when @Status='ACTIVE' then coalesce(activated_at,now()) else activated_at end,blocked_at=case when @Status in ('SUSPENDED','BLOCKED') then now() else null end,block_reason=case when @Status='ACTIVE' then null else @Reason end,updated_at=now() where tenant_id=@Id; update agro360.tenancy_tenants set status=case when @Status='ACTIVE' then 1 when @Status in ('IMPLEMENTING','TRIAL') then 2 else 3 end where id=@Id; insert into agro360.saas_tenant_status_events(id,tenant_id,previous_status,new_status,reason,created_by) values(gen_random_uuid(),@Id,@Current,@Status,@Reason,@Actor); insert into agro360.audit_saas_events(id,tenant_id,actor_id,event_type,details) values(gen_random_uuid(),@Id,@Actor,'TENANT_STATUS_CHANGED',jsonb_build_object('previousStatus',@Current,'status',@Status,'reason',@Reason))", new { id, status, Current = current, Reason = reason?.Trim(), Actor = actorId }, t); if (n == 0) throw new KeyNotFoundException("Organização não encontrada."); }, ct);
    public Task<IReadOnlyList<PlanSummary>> GetPlansAsync(CancellationToken ct) => System("plans", async (c, t) => (IReadOnlyList<PlanSummary>)(await c.QueryAsync<PlanSummary>("select id,name,description,monthly_price MonthlyPrice,annual_price AnnualPrice,user_limit UserLimit,property_limit PropertyLimit,storage_limit_mb StorageLimitMb,device_limit DeviceLimit,modules,premium_features PremiumFeatures,active from agro360.saas_plans order by monthly_price", transaction: t)).ToArray(), ct);
    public Task<Guid> CreatePlanAsync(PlanCommand command, Guid actorId, CancellationToken ct) => System("create-plan", async (c, t) => { ValidatePlan(command); var id = Guid.NewGuid(); await c.ExecuteAsync("insert into agro360.saas_plans(id,name,description,monthly_price,annual_price,user_limit,property_limit,storage_limit_mb,device_limit,modules,premium_features,active) values(@Id,@Name,@Description,@MonthlyPrice,@AnnualPrice,@UserLimit,@PropertyLimit,@StorageLimitMb,@DeviceLimit,@Modules,@PremiumFeatures,@Active); insert into agro360.audit_saas_events(id,actor_id,event_type,details) values(gen_random_uuid(),@Actor,'PLAN_CREATED',jsonb_build_object('planId',@Id))", new { Id = id, command.Name, command.Description, command.MonthlyPrice, command.AnnualPrice, command.UserLimit, command.PropertyLimit, command.StorageLimitMb, command.DeviceLimit, command.Modules, command.PremiumFeatures, command.Active, Actor = actorId }, t); return id; }, ct);
    public Task UpdatePlanAsync(Guid id, PlanCommand command, Guid actorId, CancellationToken ct) => System("update-plan", async (c, t) =>
    {
        ValidatePlan(command);
        var usage = await c.QuerySingleAsync<(long Users, long Properties)>(
            """
            select coalesce(max(active_users), 0) as Users, coalesce(max(properties), 0) as Properties
            from (
                select o.tenant_id,
                       (select count(*) from agro360.identity_users u where u.tenant_id=o.tenant_id and u.status='ACTIVE' and u.deleted_at is null) active_users,
                       (select count(*) from agro360.geo_farms f where f.tenant_id=o.tenant_id and f.deleted_at is null) properties
                from agro360.saas_organizations o where o.plan_id=@Id
            ) per_tenant;
            """, new { Id = id }, t);
        if (command.UserLimit < usage.Users || command.PropertyLimit < usage.Properties)
            throw new ConflictException($"A redução solicitada é inferior ao uso de um cliente (usuários: {usage.Users}; propriedades: {usage.Properties}).", "plan_limit_below_tenant_usage");
        var n = await c.ExecuteAsync(
            """
            with changed as (
                update agro360.saas_plans
                set name=@Name,description=@Description,monthly_price=@MonthlyPrice,annual_price=@AnnualPrice,
                    user_limit=@UserLimit,property_limit=@PropertyLimit,storage_limit_mb=@StorageLimitMb,
                    device_limit=@DeviceLimit,modules=@Modules,premium_features=@PremiumFeatures,
                    active=@Active,updated_at=now()
                where id=@Id returning id
            )
            insert into agro360.audit_saas_events(id,actor_id,event_type,details)
            select gen_random_uuid(),@Actor,'PLAN_UPDATED',jsonb_build_object(
                'planId',@Id,'userLimit',@UserLimit,'propertyLimit',@PropertyLimit,
                'maximumTenantUsers',@MaximumTenantUsers,'maximumTenantProperties',@MaximumTenantProperties)
            from changed;
            """,
            new { Id = id, command.Name, command.Description, command.MonthlyPrice, command.AnnualPrice, command.UserLimit, command.PropertyLimit, command.StorageLimitMb, command.DeviceLimit, command.Modules, command.PremiumFeatures, command.Active, Actor = actorId, MaximumTenantUsers = usage.Users, MaximumTenantProperties = usage.Properties }, t);
        if (n != 1) throw new KeyNotFoundException("Plano não encontrado.");
    }, ct);
    public Task<IReadOnlyList<UsageSummary>> GetPlatformUsageAsync(CancellationToken ct) => System("usage", async (c, t) => (IReadOnlyList<UsageSummary>)(await c.QueryAsync<UsageSummary>(UsageSql, transaction: t)).ToArray(), ct);
    public async Task<UsageSummary> GetUsageAsync(CancellationToken ct) => (await Tenant("usage", async (c, t) => (await c.QueryAsync<UsageSummary>(UsageSql + " where o.tenant_id=@TenantId", new { tenant.TenantId }, t)).Single(), ct));
    public Task<PlatformDashboard> GetDashboardAsync(CancellationToken ct) => System("dashboard", async (c, t) => await c.QuerySingleAsync<PlatformDashboard>(
        """
        with usage as (
            select o.tenant_id, p.user_limit, p.property_limit,
                   (select count(*) from agro360.identity_users u where u.tenant_id=o.tenant_id and u.status='ACTIVE' and u.deleted_at is null) active_users,
                   (select count(*) from agro360.geo_farms f where f.tenant_id=o.tenant_id and f.deleted_at is null) properties
            from agro360.saas_organizations o join agro360.saas_plans p on p.id=o.plan_id
            where o.status='ACTIVE'
        )
        select count(*) TotalOrganizations,
               count(*) filter(where status='ACTIVE') ActiveOrganizations,
               count(*) filter(where status in ('SUSPENDED','BLOCKED')) SuspendedOrganizations,
               count(*) filter(where created_at>=date_trunc('month',now())) NewThisMonth,
               (select count(*) from agro360.identity_users where status='ACTIVE' and deleted_at is null) ActiveUsers,
               (select count(*) from usage where (active_users::numeric/user_limit)>=0.8 or (properties::numeric/property_limit)>=0.8) NearLimit,
               (select count(*) from usage where active_users>user_limit or properties>property_limit) AboveLimit,
               (select count(*) from agro360.saas_invitations where status='PENDING' and expires_at>now()) PendingInvitations,
               (select count(*) from agro360.saas_login_history where occurred_at>=now()-interval '24 hours' and success) RecentLogins,
               (select count(*) from agro360.saas_login_history where occurred_at>=now()-interval '24 hours' and not success) SecurityAlerts,
               (select count(*) from agro360.saas_requests where type='UPGRADE' and status='OPEN') UpgradeRequests,
               (select count(*) from agro360.saas_requests where type='SUPPORT' and status='OPEN') SupportRequests
        from agro360.saas_organizations;
        """, transaction: t), ct);
    public Task<TenantSummary> GetOrganizationAsync(CancellationToken ct) => Tenant("organization", async (c, t) => await c.QuerySingleAsync<TenantSummary>("select x.id,x.slug,x.name,s.organization_type type,s.document,s.responsible_name ResponsibleName,s.responsible_email ResponsibleEmail,s.plan_id PlanId,p.name PlanName,s.status,s.activated_at ActivatedAt,s.blocked_at BlockedAt,s.block_reason BlockReason from agro360.tenancy_tenants x join agro360.saas_organizations s on s.tenant_id=x.id join agro360.saas_plans p on p.id=s.plan_id where x.id=@TenantId", new { tenant.TenantId }, t), ct);
    public Task UpdateOrganizationAsync(TenantUpdateCommand command, Guid actorId, CancellationToken ct) => Tenant("organization-update", async (c, t) => { ValidateUpdate(command); await c.ExecuteAsync("update agro360.tenancy_tenants set name=@Name where id=@TenantId; update agro360.saas_organizations set organization_type=@Type,responsible_name=@ResponsibleName,responsible_email=@ResponsibleEmail,updated_at=now() where tenant_id=@TenantId; insert into agro360.audit_saas_events(id,tenant_id,actor_id,event_type) values(gen_random_uuid(),@TenantId,@Actor,'ORGANIZATION_UPDATED')", new { tenant.TenantId, command.Name, command.Type, command.ResponsibleName, command.ResponsibleEmail, Actor = actorId }, t); }, ct);
    public Task<PlanSummary> GetCurrentPlanAsync(CancellationToken ct) => Tenant("current-plan", async (c, t) => await c.QuerySingleAsync<PlanSummary>("select p.id,p.name,p.description,p.monthly_price MonthlyPrice,p.annual_price AnnualPrice,p.user_limit UserLimit,p.property_limit PropertyLimit,p.storage_limit_mb StorageLimitMb,p.device_limit DeviceLimit,p.modules,p.premium_features PremiumFeatures,p.active from agro360.saas_plans p join agro360.saas_organizations o on o.plan_id=p.id where o.tenant_id=@TenantId", new { tenant.TenantId }, t), ct);
    public Task<Guid> RequestUpgradeAsync(UpgradeRequestCommand command, Guid actorId, CancellationToken ct) => Tenant("upgrade", async (c, t) =>
    {
        if (command.RequestedPlanId == Guid.Empty || command.Reason.Trim().Length is < 5 or > 1000)
            throw new ArgumentException("Plano e justificativa entre 5 e 1000 caracteres são obrigatórios.");
        var valid = await c.ExecuteScalarAsync<bool>(
            """
            select exists(
                select 1 from agro360.saas_plans requested
                join agro360.saas_organizations current on current.tenant_id=@TenantId
                where requested.id=@RequestedPlanId and requested.active and requested.id<>current.plan_id)
            """, new { tenant.TenantId, command.RequestedPlanId }, t);
        if (!valid) throw InvalidReferences(nameof(UpgradeRequestCommand.RequestedPlanId), "O plano solicitado não existe, está inativo ou já é o plano atual.");
        var id = Guid.CreateVersion7();
        await c.ExecuteAsync(
            """
            insert into agro360.saas_requests(id,tenant_id,type,requested_plan_id,reason,requested_by,status)
            values(@Id,@TenantId,'UPGRADE',@RequestedPlanId,@Reason,@Actor,'OPEN');
            insert into agro360.audit_saas_events(id,tenant_id,actor_id,event_type,details)
            values(gen_random_uuid(),@TenantId,@Actor,'UPGRADE_REQUESTED',jsonb_build_object('requestId',@Id,'requestedPlanId',@RequestedPlanId,'communication','PENDING_PROVIDER'));
            """, new { Id = id, tenant.TenantId, command.RequestedPlanId, Reason = command.Reason.Trim(), Actor = actorId }, t);
        return id;
    }, ct);
    public Task<IReadOnlyList<UserSummary>> GetUsersAsync(CancellationToken ct) => Tenant("users", async (c, t) => (IReadOnlyList<UserSummary>)(await c.QueryAsync<UserSummary>("select u.id,u.name,u.email,u.status,u.last_login_at LastAccess,coalesce(array_agg(r.name) filter(where r.id is not null),array[]::varchar[]) roles from agro360.identity_users u left join agro360.identity_user_roles ur on ur.tenant_id=u.tenant_id and ur.user_id=u.id left join agro360.identity_roles r on r.id=ur.role_id where u.tenant_id=@TenantId and u.deleted_at is null group by u.id order by u.name", new { tenant.TenantId }, t)).ToArray(), ct);
    public Task<Guid> SaveUserAsync(Guid? id, UserCommand command, Guid actorId, CancellationToken ct) => Tenant("save-user", async (c, t) =>
    {
        if (string.IsNullOrWhiteSpace(command.Name) || !EmailRegex().IsMatch(command.Email) || command.RoleIds.Length == 0)
            throw new ArgumentException("Nome, e-mail válido e perfil são obrigatórios.");
        await LockTenantGovernanceAsync(c, t);
        var roleIds = command.RoleIds.Distinct().ToArray();
        var actorLevel = await GetActorLevelAsync(c, t, actorId);
        var roles = (await c.QueryAsync<RoleGrantLookup>(
            """
            select r.id, r.code, r.is_system IsSystem,
                   case when lower(r.code)='tenant-administrator' then 100 else coalesce(m.level,10) end Level
            from agro360.identity_roles r
            left join agro360.saas_role_metadata m on m.tenant_id=r.tenant_id and m.role_id=r.id
            where r.tenant_id=@TenantId and r.id=any(@RoleIds)
            """, new { tenant.TenantId, RoleIds = roleIds }, t)).ToArray();
        if (roles.Length != roleIds.Length)
            throw InvalidReferences(nameof(UserCommand.RoleIds), "Um ou mais perfis não pertencem a esta organização.");
        if (roles.Any(role => role.Code.Equals("SUPER_ADMIN", StringComparison.OrdinalIgnoreCase) || role.Code.StartsWith("PLATFORM_", StringComparison.OrdinalIgnoreCase)))
            throw new ForbiddenException("Papéis globais não podem ser atribuídos por uma organização cliente.");
        if (roles.Any(role => role.Level > actorLevel))
            throw new ForbiddenException("Não é permitido atribuir perfil superior ao do administrador atual.");
        await EnsureActorCanGrantRolesAsync(c, t, actorId, roleIds);

        var key = id ?? Guid.CreateVersion7();
        var target = id is null ? null : await c.QuerySingleOrDefaultAsync<UserRoleState>(
            """
            select u.status,
                   exists(select 1 from agro360.identity_user_roles ur join agro360.identity_roles r on r.tenant_id=ur.tenant_id and r.id=ur.role_id where ur.tenant_id=u.tenant_id and ur.user_id=u.id and lower(r.code)='tenant-administrator') IsAdministrator
            from agro360.identity_users u where u.tenant_id=@TenantId and u.id=@Id and u.deleted_at is null for update
            """, new { tenant.TenantId, Id = key }, t);
        if (id is not null && target is null) throw new NotFoundException("Usuario", key);
        var grantsAdministrator = roles.Any(role => role.Code.Equals("tenant-administrator", StringComparison.OrdinalIgnoreCase));
        if (target?.IsAdministrator == true && !grantsAdministrator)
        {
            if (key == actorId) throw new ForbiddenException("O administrador não pode remover o próprio papel administrativo.");
            var activeAdministrators = await CountActiveAdministratorsAsync(c, t);
            if (activeAdministrators <= 1) throw new ConflictException("O último administrador ativo não pode perder o perfil administrativo.", "last_tenant_administrator");
        }
        if (await c.ExecuteScalarAsync<bool>("select exists(select 1 from agro360.identity_users where tenant_id=@TenantId and lower(email)=lower(@Email) and id<>@Id and deleted_at is null)", new { tenant.TenantId, Email = command.Email.Trim(), Id = key }, t))
            throw new ConflictException("Já existe um usuário com este e-mail na organização.", "user_email_duplicate");

        if (id is null)
        {
            await EnsureUserCapacityAsync(c, t);
            var unreachablePassword = passwordHasher.Hash($"Aa1!{Convert.ToHexString(RandomNumberGenerator.GetBytes(24))}");
            await c.ExecuteAsync("insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status,created_by) values(@Id,@TenantId,@Name,@Email,@PasswordHash,'INVITED',@Actor)", new { Id = key, tenant.TenantId, Name = command.Name.Trim(), Email = command.Email.Trim().ToLowerInvariant(), PasswordHash = unreachablePassword, Actor = actorId }, t);
        }
        else
        {
            var changed = await c.ExecuteAsync("update agro360.identity_users set name=@Name,email=@Email,updated_at=now(),updated_by=@Actor,version=version+1 where tenant_id=@TenantId and id=@Id and deleted_at is null", new { Id = key, tenant.TenantId, Name = command.Name.Trim(), Email = command.Email.Trim().ToLowerInvariant(), Actor = actorId }, t);
            if (changed != 1) throw new NotFoundException("Usuario", key);
        }
        await c.ExecuteAsync(
            """
            delete from agro360.identity_user_roles where tenant_id=@TenantId and user_id=@Id;
            insert into agro360.identity_user_roles(tenant_id,user_id,role_id) select @TenantId,@Id,id from agro360.identity_roles where tenant_id=@TenantId and id=any(@RoleIds);
            update agro360.identity_refresh_tokens set revoked_at=coalesce(revoked_at,now()) where tenant_id=@TenantId and user_id=@Id and revoked_at is null;
            update agro360.saas_sessions set revoked_at=coalesce(revoked_at,now()),revoked_by=@Actor where tenant_id=@TenantId and user_id=@Id and revoked_at is null;
            insert into agro360.audit_saas_events(id,tenant_id,actor_id,event_type,details) values(gen_random_uuid(),@TenantId,@Actor,'USER_ROLE_CHANGED',jsonb_build_object('userId',@Id,'roleIds',@RoleIds,'sessionsRevoked',true));
            """, new { Id = key, tenant.TenantId, RoleIds = roleIds, Actor = actorId }, t);
        return key;
    }, ct);
    public Task SetUserActiveAsync(Guid id, bool active, string reason, Guid actorId, CancellationToken ct) =>
        Tenant("user-status", async (connection, transaction) =>
        {
            await LockTenantGovernanceAsync(connection, transaction);
            var target = await connection.QuerySingleOrDefaultAsync<UserAccessLookup>(
                """
                select u.status,
                       exists (
                           select 1
                           from agro360.identity_user_roles ur
                           join agro360.identity_roles r
                             on r.tenant_id = ur.tenant_id and r.id = ur.role_id
                           where ur.tenant_id = u.tenant_id and ur.user_id = u.id
                             and lower(r.code) = 'tenant-administrator'
                       ) as IsAdministrator
                from agro360.identity_users u
                where u.id = @Id and u.tenant_id = @TenantId and u.deleted_at is null
                for update;
                """,
                new { Id = id, tenant.TenantId },
                transaction).ConfigureAwait(false);
            if (target is null)
            {
                throw new NotFoundException("Usuario", id);
            }
            if (active && !target.Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase))
            {
                await EnsureUserCapacityAsync(connection, transaction);
            }

            var activeAdministrators = target.IsAdministrator
                ? await connection.ExecuteScalarAsync<long>(
                    """
                    select count(distinct u.id)
                    from agro360.identity_users u
                    join agro360.identity_user_roles ur
                      on ur.tenant_id = u.tenant_id and ur.user_id = u.id
                    join agro360.identity_roles r
                      on r.tenant_id = ur.tenant_id and r.id = ur.role_id
                    where u.tenant_id = @TenantId and u.status = 'ACTIVE' and u.deleted_at is null
                      and lower(r.code) = 'tenant-administrator';
                    """,
                    new { tenant.TenantId },
                    transaction).ConfigureAwait(false)
                : 0;
            string status;
            try
            {
                status = SaasGovernanceRules.EnsureUserAccessTransition(
                    target.Status,
                    active,
                    id == actorId,
                    target.IsAdministrator && activeAdministrators <= 1,
                    reason);
            }
            catch (ArgumentException exception)
            {
                throw new ValidationException(new Dictionary<string, string[]>
                {
                    [nameof(UserStatusCommand.Reason)] = [exception.Message]
                });
            }
            catch (InvalidOperationException exception)
            {
                throw new ConflictException(exception.Message, "user_status_transition_rejected");
            }

            var normalizedReason = reason.Trim();
            await connection.ExecuteAsync(
                """
                update agro360.identity_users
                set status = @Status, updated_at = now(), updated_by = @Actor, version = version + 1
                where id = @Id and tenant_id = @TenantId;

                update agro360.identity_refresh_tokens
                set revoked_at = coalesce(revoked_at, now())
                where tenant_id = @TenantId and user_id = @Id and revoked_at is null;

                insert into agro360.identity_user_status_events
                    (tenant_id, user_id, previous_status, new_status, reason, created_by)
                values
                    (@TenantId, @Id, @PreviousStatus, @Status, @Reason, @Actor);

                insert into agro360.audit_saas_events
                    (id, tenant_id, actor_id, event_type, details)
                values
                    (gen_random_uuid(), @TenantId, @Actor, 'USER_STATUS_CHANGED',
                     jsonb_build_object('userId', @Id, 'previousStatus', @PreviousStatus,
                                        'status', @Status, 'reason', @Reason,
                                        'sessionsRevoked', true));
                """,
                new
                {
                    Id = id,
                    tenant.TenantId,
                    PreviousStatus = target.Status,
                    Status = status,
                    Reason = normalizedReason,
                    Actor = actorId
                },
                transaction).ConfigureAwait(false);
        }, ct);
    public Task<IReadOnlyList<RoleSummary>> GetRolesAsync(CancellationToken ct) => Tenant("roles", async (c, t) => (IReadOnlyList<RoleSummary>)(await c.QueryAsync<RoleSummary>("select r.id,r.name,coalesce(m.level,10) level,coalesce(array_agg(p.code) filter(where p.id is not null),array[]::varchar[]) permissions,r.is_system SystemRole from agro360.identity_roles r left join agro360.saas_role_metadata m on m.tenant_id=r.tenant_id and m.role_id=r.id left join agro360.identity_role_permissions rp on rp.tenant_id=r.tenant_id and rp.role_id=r.id left join agro360.identity_permissions p on p.id=rp.permission_id where r.tenant_id=@TenantId and r.code<>'PLATFORM_SUPER_ADMIN' group by r.id,m.level order by m.level desc,r.name", new { tenant.TenantId }, t)).ToArray(), ct);
    public Task<Guid> SaveRoleAsync(Guid? id, RoleCommand command, Guid actorId, CancellationToken ct) => Tenant("save-role", async (c, t) =>
    {
        if (string.IsNullOrWhiteSpace(command.Name) || command.Level is < 1 or > 90 || command.Permissions.Length == 0)
            throw new ArgumentException("Nome, nível entre 1 e 90 e permissões são obrigatórios.");
        var actorLevel = await GetActorLevelAsync(c, t, actorId);
        if (command.Level > actorLevel) throw new ForbiddenException("Não é permitido criar perfil superior ao do administrador atual.");
        var requestedPermissions = command.Permissions.Select(value => value.Trim().ToLowerInvariant()).Distinct().ToArray();
        var allowedPermissions = (await c.QueryAsync<string>(
            """
            select distinct p.code from agro360.identity_user_roles ur
            join agro360.identity_role_permissions rp on rp.tenant_id=ur.tenant_id and rp.role_id=ur.role_id
            join agro360.identity_permissions p on p.id=rp.permission_id
            where ur.tenant_id=@TenantId and ur.user_id=@Actor
            """, new { tenant.TenantId, Actor = actorId }, t)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (requestedPermissions.Any(permission => permission.Equals(Permissions.PlatformAdmin, StringComparison.OrdinalIgnoreCase) || !allowedPermissions.Contains(permission)))
            throw new ForbiddenException("O perfil solicita permissão que o administrador atual não possui.");
        var existingPermissions = await c.ExecuteScalarAsync<int>("select count(*)::int from agro360.identity_permissions where code=any(@Permissions)", new { Permissions = requestedPermissions }, t);
        if (existingPermissions != requestedPermissions.Length)
            throw InvalidReferences(nameof(RoleCommand.Permissions), "Uma ou mais permissões não existem.");

        var key = id ?? Guid.CreateVersion7();
        if (id is not null)
        {
            var editable = await c.QuerySingleOrDefaultAsync<bool?>("select not is_system from agro360.identity_roles where tenant_id=@TenantId and id=@Id for update", new { tenant.TenantId, Id = key }, t);
            if (editable is null) throw new NotFoundException("Perfil", key);
            if (editable != true) throw new ForbiddenException("Perfis protegidos do sistema não podem ser alterados.");
            await c.ExecuteAsync("update agro360.identity_roles set name=@Name where tenant_id=@TenantId and id=@Id", new { tenant.TenantId, Id = key, Name = command.Name.Trim() }, t);
        }
        else
        {
            await c.ExecuteAsync("insert into agro360.identity_roles(id,tenant_id,code,name,is_system) values(@Id,@TenantId,@Code,@Name,false)", new { Id = key, tenant.TenantId, Code = "CUSTOM_" + key.ToString("N"), Name = command.Name.Trim() }, t);
        }
        await c.ExecuteAsync(
            """
            insert into agro360.saas_role_metadata(tenant_id,role_id,level) values(@TenantId,@Id,@Level) on conflict(tenant_id,role_id) do update set level=excluded.level;
            delete from agro360.identity_role_permissions where tenant_id=@TenantId and role_id=@Id;
            insert into agro360.identity_role_permissions(tenant_id,role_id,permission_id) select @TenantId,@Id,id from agro360.identity_permissions where code=any(@Permissions);
            update agro360.identity_refresh_tokens rt set revoked_at=coalesce(rt.revoked_at,now()) where rt.tenant_id=@TenantId and rt.revoked_at is null and exists(select 1 from agro360.identity_user_roles ur where ur.tenant_id=rt.tenant_id and ur.user_id=rt.user_id and ur.role_id=@Id);
            insert into agro360.audit_saas_events(id,tenant_id,actor_id,event_type,details) values(gen_random_uuid(),@TenantId,@Actor,'ROLE_CHANGED',jsonb_build_object('roleId',@Id,'permissions',@Permissions,'sessionsRevoked',true));
            """, new { Id = key, tenant.TenantId, command.Level, Permissions = requestedPermissions, Actor = actorId }, t);
        return key;
    }, ct);
    public Task<IReadOnlyList<InvitationSummary>> GetInvitationsAsync(CancellationToken ct) => Tenant("invitations", async (c, t) => (IReadOnlyList<InvitationSummary>)(await c.QueryAsync<InvitationSummary>("select i.id,i.email,r.name RoleName,case when i.status='PENDING' and i.expires_at<=now() then 'EXPIRED' else i.status end status,i.expires_at ExpiresAt,i.delivery_status DeliveryStatus from agro360.saas_invitations i join agro360.identity_roles r on r.tenant_id=i.tenant_id and r.id=i.role_id where i.tenant_id=@TenantId order by i.created_at desc", new { tenant.TenantId }, t)).ToArray(), ct);
    public Task<InvitationCreated> InviteAsync(InvitationCommand command, Guid actorId, CancellationToken ct) => Tenant("invite", async (c, t) =>
    {
        if (!EmailRegex().IsMatch(command.Email) || command.RoleId == Guid.Empty || command.ValidForHours is < 1 or > 168)
            throw new ArgumentException("E-mail, perfil e validade de até 168 horas são obrigatórios.");
        var actorLevel = await GetActorLevelAsync(c, t, actorId);
        var role = await c.QuerySingleOrDefaultAsync<RoleGrantLookup>(
            """
            select r.id,r.code,r.is_system IsSystem,case when lower(r.code)='tenant-administrator' then 100 else coalesce(m.level,10) end Level
            from agro360.identity_roles r left join agro360.saas_role_metadata m on m.tenant_id=r.tenant_id and m.role_id=r.id
            where r.tenant_id=@TenantId and r.id=@RoleId
            """, new { tenant.TenantId, command.RoleId }, t);
        if (role is null) throw InvalidReferences(nameof(InvitationCommand.RoleId), "O perfil informado não pertence a esta organização.");
        if (role.Code.Equals("SUPER_ADMIN", StringComparison.OrdinalIgnoreCase) || role.Code.StartsWith("PLATFORM_", StringComparison.OrdinalIgnoreCase) || role.Level > actorLevel)
            throw new ForbiddenException("O convite não pode conceder autoridade global ou superior à do administrador atual.");
        await EnsureActorCanGrantRolesAsync(c, t, actorId, [command.RoleId]);
        if (await c.ExecuteScalarAsync<bool>("select exists(select 1 from agro360.identity_users where tenant_id=@TenantId and lower(email)=lower(@Email) and deleted_at is null and status='ACTIVE')", new { tenant.TenantId, Email = command.Email.Trim() }, t))
            throw new ConflictException("O e-mail já possui acesso ativo nesta organização.", "invitation_user_active");

        var id = Guid.CreateVersion7();
        var activationToken = CreateInvitationToken(tenant.TenantId);
        var tokenHash = HashInvitationToken(activationToken);
        try
        {
            await c.ExecuteAsync(
                """
                insert into agro360.saas_invitations(id,tenant_id,email,role_id,token_hash,expires_at,invited_by,delivery_status)
                values(@Id,@TenantId,@Email,@RoleId,@TokenHash,now()+make_interval(hours=>@Hours),@Actor,'PENDING_PROVIDER');
                insert into agro360.audit_saas_events(id,tenant_id,actor_id,event_type,details)
                values(gen_random_uuid(),@TenantId,@Actor,'INVITATION_CREATED',jsonb_build_object('invitationId',@Id,'deliveryStatus','PENDING_PROVIDER'));
                """, new { Id = id, tenant.TenantId, Email = command.Email.Trim().ToLowerInvariant(), command.RoleId, TokenHash = tokenHash, Hours = command.ValidForHours, Actor = actorId }, t);
        }
        catch (Npgsql.PostgresException exception) when (exception.SqlState == Npgsql.PostgresErrorCodes.UniqueViolation)
        {
            throw new ConflictException("Já existe convite pendente para este e-mail.", "invitation_pending_duplicate");
        }
        return new InvitationCreated(id, activationToken, "PENDING_PROVIDER");
    }, ct);
    public Task<InvitationCreated?> ChangeInvitationAsync(Guid id, string action, Guid actorId, CancellationToken ct) => Tenant("invitation-action", async (c, t) =>
    {
        if (action is not ("resend" or "cancel")) throw new ArgumentException("Ação inválida.");
        string? activationToken = action == "resend" ? CreateInvitationToken(tenant.TenantId) : null;
        var changed = await c.ExecuteAsync(
            """
            update agro360.saas_invitations
            set status=case when @Action='cancel' then 'CANCELLED' else 'PENDING' end,
                expires_at=case when @Action='resend' then now()+interval '72 hours' else expires_at end,
                token_hash=case when @Action='resend' then @TokenHash else token_hash end,
                delivery_status=case when @Action='resend' then 'PENDING_PROVIDER' else delivery_status end
            where id=@Id and tenant_id=@TenantId and status='PENDING';
            """, new { Id = id, tenant.TenantId, Action = action, TokenHash = activationToken is null ? null : HashInvitationToken(activationToken) }, t);
        if (changed != 1) throw new KeyNotFoundException("Convite pendente não encontrado.");
        await c.ExecuteAsync("insert into agro360.audit_saas_events(id,tenant_id,actor_id,event_type,details) values(gen_random_uuid(),@TenantId,@Actor,'INVITATION_'||upper(@Action),jsonb_build_object('invitationId',@Id))", new { Id = id, tenant.TenantId, Action = action, Actor = actorId }, t);
        return activationToken is null ? null : new InvitationCreated(id, activationToken, "PENDING_PROVIDER");
    }, ct);
    public Task<InvitationAcceptanceResult> AcceptInvitationAsync(InvitationAcceptanceCommand command, CancellationToken ct)
    {
        if (!TryReadInvitationTenant(command.Token, out var tenantId)) throw new AuthenticationException("Convite inválido ou expirado.", "invitation_invalid");
        var passwordHash = passwordHasher.Hash(command.Password);
        var name = Agro360.SharedKernel.Guard.Required(command.Name, nameof(command.Name), 160);
        return db.InTenantTransactionAsync(tenantId, async (c, t) =>
        {
            await LockTenantGovernanceAsync(c, t, tenantId);
            var invitation = await c.QuerySingleOrDefaultAsync<InvitationAcceptanceLookup>(
                """
                select i.id,i.email,i.role_id RoleId,i.invited_by InvitedBy,i.expires_at ExpiresAt,i.status,t.slug TenantSlug
                from agro360.saas_invitations i join agro360.tenancy_tenants t on t.id=i.tenant_id
                where i.tenant_id=@TenantId and i.token_hash=@TokenHash for update
                """, new { TenantId = tenantId, TokenHash = HashInvitationToken(command.Token) }, t);
            if (invitation is null || invitation.Status != "PENDING" || invitation.ExpiresAt <= DateTimeOffset.UtcNow)
                throw new AuthenticationException("Convite inválido, expirado ou já utilizado.", "invitation_invalid");
            await EnsureActorCanGrantRolesAsync(c, t, invitation.InvitedBy, [invitation.RoleId], tenantId);
            var existingUser = await c.QuerySingleOrDefaultAsync<InvitationUserLookup>("select id,status from agro360.identity_users where tenant_id=@TenantId and lower(email)=lower(@Email) and deleted_at is null for update", new { TenantId = tenantId, invitation.Email }, t);
            if (existingUser is not null && existingUser.Status != "INVITED")
                throw new ConflictException("O usuário associado ao convite já foi ativado ou bloqueado.", "invitation_user_state_changed");
            await EnsureUserCapacityAsync(c, t, tenantId);
            var userId = existingUser?.Id ?? Guid.CreateVersion7();
            await c.ExecuteAsync(
                """
                insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status,must_change_password,created_by)
                values(@UserId,@TenantId,@Name,@Email,@PasswordHash,'ACTIVE',false,@UserId)
                on conflict(id) do update set name=excluded.name,password_hash=excluded.password_hash,status='ACTIVE',must_change_password=false,updated_at=now(),updated_by=excluded.id
                where agro360.identity_users.tenant_id=@TenantId and agro360.identity_users.status='INVITED';
                delete from agro360.identity_user_roles where tenant_id=@TenantId and user_id=@UserId;
                insert into agro360.identity_user_roles(tenant_id,user_id,role_id) values(@TenantId,@UserId,@RoleId);
                update agro360.saas_invitations set status='ACCEPTED',accepted_at=now(),accepted_by_user_id=@UserId,delivery_status='TOKEN_REDEEMED' where id=@InvitationId and tenant_id=@TenantId and status='PENDING';
                insert into agro360.audit_saas_events(id,tenant_id,actor_id,event_type,details) values(gen_random_uuid(),@TenantId,@UserId,'INVITATION_ACCEPTED',jsonb_build_object('invitationId',@InvitationId));
                """, new { UserId = userId, TenantId = tenantId, Name = name, invitation.Email, PasswordHash = passwordHash, invitation.RoleId, InvitationId = invitation.Id }, t);
            return new InvitationAcceptanceResult(invitation.TenantSlug, invitation.Email);
        }, ct);
    }
    public Task<IReadOnlyList<SessionSummary>> GetSessionsAsync(CancellationToken ct) => Tenant("sessions", async (c, t) => (IReadOnlyList<SessionSummary>)(await c.QueryAsync<SessionSummary>("select id,device,ip_address IpAddress,created_at CreatedAt,last_seen_at LastSeenAt,revoked_at RevokedAt from agro360.saas_sessions where tenant_id=@TenantId order by last_seen_at desc", new { tenant.TenantId }, t)).ToArray(), ct);
    public Task RevokeSessionAsync(Guid id, Guid actorId, CancellationToken ct) => Revoke("sessions", id, actorId, ct);
    public Task<IReadOnlyList<DeviceSummary>> GetDevicesAsync(CancellationToken ct) => Tenant("devices", async (c, t) => (IReadOnlyList<DeviceSummary>)(await c.QueryAsync<DeviceSummary>("select id,name,platform,last_seen_at LastSeenAt,revoked_at RevokedAt from agro360.saas_devices where tenant_id=@TenantId order by last_seen_at desc", new { tenant.TenantId }, t)).ToArray(), ct);
    public Task RevokeDeviceAsync(Guid id, Guid actorId, CancellationToken ct) => Revoke("devices", id, actorId, ct);
    private Task Revoke(string table, Guid id, Guid actorId, CancellationToken ct) => Tenant("revoke-" + table, async (c, t) => { var sql = $"update agro360.saas_{table} set revoked_at=coalesce(revoked_at,now()),revoked_by=@Actor where id=@Id and tenant_id=@TenantId and revoked_at is null; insert into agro360.audit_saas_events(id,tenant_id,actor_id,event_type,details) values(gen_random_uuid(),@TenantId,@Actor,@Event,jsonb_build_object('id',@Id))"; var n = await c.ExecuteAsync(sql, new { id, tenant.TenantId, Actor = actorId, Event = table == "sessions" ? "SESSION_REVOKED" : "DEVICE_REVOKED" }, t); if (n == 0) throw new KeyNotFoundException("Registro ativo não encontrado."); }, ct);
    public Task<IReadOnlyList<NotificationSummary>> GetNotificationsAsync(string? type, string? priority, CancellationToken ct) => Tenant("notifications", async (c, t) => (IReadOnlyList<NotificationSummary>)(await c.QueryAsync<NotificationSummary>("select id,type,priority,title,message,route,created_at CreatedAt,read_at ReadAt,archived_at ArchivedAt,requires_action RequiresAction from agro360.saas_notifications where tenant_id=@TenantId and (user_id is null or user_id=current_setting('app.user_id',true)::uuid) and (@Type is null or type=@Type) and (@Priority is null or priority=@Priority) order by created_at desc limit 200", new { tenant.TenantId, Type = type, Priority = priority }, t)).ToArray(), ct);
    public Task ChangeNotificationAsync(Guid id, string action, CancellationToken ct) => Tenant("notification-action", async (c, t) => { if (action is not ("read" or "archive")) throw new ArgumentException("Ação inválida."); var n = await c.ExecuteAsync("update agro360.saas_notifications set read_at=case when @Action='read' then coalesce(read_at,now()) else read_at end,archived_at=case when @Action='archive' and not requires_action then now() else archived_at end where id=@Id and tenant_id=@TenantId", new { id, tenant.TenantId, action }, t); if (n == 0) throw new KeyNotFoundException("Notificação não encontrada."); }, ct);
    public Task<OrganizationSettings> GetSettingsAsync(CancellationToken ct) => Tenant("settings", async (c, t) => await c.QuerySingleAsync<OrganizationSettings>("select x.name OrganizationName,s.unit_system UnitSystem,s.currency,s.time_zone TimeZone,s.main_culture MainCulture,s.main_activities MainActivities,s.stock_parameters::text StockParameters,s.finance_parameters::text FinanceParameters,s.traceability_parameters::text TraceabilityParameters,s.compliance_parameters::text ComplianceParameters,s.notification_preferences NotificationPreferences from agro360.saas_organization_settings s join agro360.tenancy_tenants x on x.id=s.tenant_id where s.tenant_id=@TenantId", new { tenant.TenantId }, t), ct);
    public Task UpdateSettingsAsync(OrganizationSettings settings, Guid actorId, CancellationToken ct) => Tenant("settings-update", async (c, t) => { if (string.IsNullOrWhiteSpace(settings.OrganizationName) || settings.Currency.Length != 3 || string.IsNullOrWhiteSpace(settings.TimeZone) || settings.MainActivities.Length == 0) throw new ArgumentException("Organização, moeda, fuso e atividades são obrigatórios."); await c.ExecuteAsync("update agro360.tenancy_tenants set name=@OrganizationName where id=@TenantId; update agro360.saas_organization_settings set unit_system=@UnitSystem,currency=upper(@Currency),time_zone=@TimeZone,main_culture=@MainCulture,main_activities=@MainActivities,stock_parameters=@StockParameters::jsonb,finance_parameters=@FinanceParameters::jsonb,traceability_parameters=@TraceabilityParameters::jsonb,compliance_parameters=@ComplianceParameters::jsonb,notification_preferences=@NotificationPreferences,updated_at=now(),updated_by=@Actor where tenant_id=@TenantId; insert into agro360.audit_saas_events(id,tenant_id,actor_id,event_type) values(gen_random_uuid(),@TenantId,@Actor,'SETTINGS_CHANGED')", new { tenant.TenantId, settings.OrganizationName, settings.UnitSystem, settings.Currency, settings.TimeZone, settings.MainCulture, settings.MainActivities, settings.StockParameters, settings.FinanceParameters, settings.TraceabilityParameters, settings.ComplianceParameters, settings.NotificationPreferences, Actor = actorId }, t); }, ct);
    public Task<IReadOnlyList<BillingChargeSummary>> GetChargesAsync(CancellationToken ct) => System("billing-list", async (c, t) => (IReadOnlyList<BillingChargeSummary>)(await c.QueryAsync<BillingChargeSummary>("select c.id,c.tenant_id TenantId,x.name TenantName,p.name PlanName,c.competence,c.due_on DueOn,c.amount,c.status,c.notes,c.paid_on PaidOn from agro360.saas_billing_charges c join agro360.tenancy_tenants x on x.id=c.tenant_id join agro360.saas_subscriptions s on s.id=c.subscription_id join agro360.saas_plans p on p.id=s.plan_id where c.deleted_at is null order by c.due_on desc", transaction: t)).ToArray(), ct);
    public Task<Guid> CreateChargeAsync(BillingChargeCommand command, Guid actorId, CancellationToken ct) => System("billing-create", async (c, t) => { if (command.Amount <= 0 || command.DueOn < command.Competence || command.Competence.Day != 1) throw new ArgumentException("Competência, vencimento e valor da cobrança são inválidos."); var id = Guid.CreateVersion7(); var n = await c.ExecuteAsync("insert into agro360.saas_billing_charges(id,tenant_id,subscription_id,competence,due_on,amount,status,notes,created_by) select @Id,@TenantId,@SubscriptionId,@Competence,@DueOn,@Amount,'OPEN',@Notes,@Actor from agro360.saas_subscriptions where id=@SubscriptionId and tenant_id=@TenantId and deleted_at is null; insert into agro360.saas_billing_charge_events(tenant_id,charge_id,event_type,reason,created_by) select @TenantId,@Id,'CREATED','Emissão manual',@Actor where exists(select 1 from agro360.saas_billing_charges where id=@Id)", new { Id = id, command.TenantId, command.SubscriptionId, command.Competence, command.DueOn, command.Amount, command.Notes, Actor = actorId }, t); if (n == 0) throw new KeyNotFoundException("Assinatura não encontrada para o cliente."); return id; }, ct);
    public Task ChangeChargeStatusAsync(Guid id, BillingStatusCommand command, Guid actorId, CancellationToken ct) => System("billing-status", async (c, t) => { var status = command.Status.ToUpperInvariant(); if (status is not ("PAID" or "CANCELLED" or "NEGOTIATING") || command.Reason.Trim().Length < 5) throw new ArgumentException("Status e justificativa são obrigatórios."); var n = await c.ExecuteAsync("with changed as (update agro360.saas_billing_charges set status=@Status,paid_on=case when @Status='PAID' then current_date else null end,payment_method=case when @Status='PAID' then 'MANUAL' else payment_method end,cancellation_reason=case when @Status='CANCELLED' then @Reason else cancellation_reason end,updated_at=now(),updated_by=@Actor where id=@Id and deleted_at is null and status not in('PAID','CANCELLED') returning tenant_id) insert into agro360.saas_billing_charge_events(tenant_id,charge_id,event_type,reason,created_by) select tenant_id,@Id,@Status,@Reason,@Actor from changed", new { id, Status = status, Reason = command.Reason.Trim(), Actor = actorId }, t); if (n == 0) throw new KeyNotFoundException("Cobrança não encontrada ou já encerrada."); }, ct);
    public Task<IReadOnlyList<FeatureFlagSummary>> GetFeatureFlagsAsync(Guid tenantId, CancellationToken ct) => System("features-list", async (c, t) => (IReadOnlyList<FeatureFlagSummary>)(await c.QueryAsync<FeatureFlagSummary>("select f.id,f.code,f.name,f.description,coalesce(pf.enabled,false) PlanEnabled,tf.enabled TenantEnabled,case when tf.feature_id is not null and (tf.expires_at is null or tf.expires_at>now()) then tf.origin when pf.enabled then 'PLAN' else 'NOT_CONTRACTED' end EffectiveOrigin,tf.expires_at ExpiresAt from agro360.saas_feature_flags f join agro360.saas_organizations o on o.tenant_id=@TenantId left join agro360.saas_plan_features pf on pf.plan_id=o.plan_id and pf.feature_id=f.id left join agro360.saas_tenant_feature_flags tf on tf.tenant_id=o.tenant_id and tf.feature_id=f.id where f.active and f.deleted_at is null order by f.name", new { TenantId = tenantId }, t)).ToArray(), ct);
    public Task SetFeatureOverrideAsync(FeatureOverrideCommand command, Guid actorId, CancellationToken ct) => System("feature-override", async (c, t) => { if (command.Reason.Trim().Length < 5 || command.ExpiresAt <= DateTimeOffset.UtcNow) throw new ArgumentException("Justificativa e validade futura são obrigatórias."); await c.ExecuteAsync("insert into agro360.saas_tenant_feature_flags(tenant_id,feature_id,enabled,origin,reason,expires_at,created_by) values(@TenantId,@FeatureId,@Enabled,'MANUAL_OVERRIDE',@Reason,@ExpiresAt,@Actor) on conflict(tenant_id,feature_id) do update set enabled=excluded.enabled,origin='MANUAL_OVERRIDE',reason=excluded.reason,expires_at=excluded.expires_at,updated_at=now(),updated_by=@Actor; insert into agro360.saas_admin_audit_events(tenant_id,actor_id,action,entity_type,entity_id,reason,safe_details) values(@TenantId,@Actor,'FEATURE_OVERRIDE','FEATURE',@FeatureId,@Reason,jsonb_build_object('enabled',@Enabled,'expiresAt',@ExpiresAt))", new { command.TenantId, command.FeatureId, command.Enabled, Reason = command.Reason.Trim(), command.ExpiresAt, Actor = actorId }, t); }, ct);
    public Task<IReadOnlyList<SaasAuditSummary>> GetAuditAsync(Guid? tenantId, CancellationToken ct) => System("audit-list", async (c, t) => (IReadOnlyList<SaasAuditSummary>)(await c.QueryAsync<SaasAuditSummary>("select a.id,a.tenant_id TenantId,x.name TenantName,a.actor_id ActorId,a.action,a.entity_type EntityType,a.entity_id EntityId,a.reason,a.created_at CreatedAt from agro360.saas_admin_audit_events a left join agro360.tenancy_tenants x on x.id=a.tenant_id where (@TenantId is null or a.tenant_id=@TenantId) order by a.created_at desc limit 500", new { TenantId = tenantId }, t)).ToArray(), ct);
    private const string UsageSql = "select o.tenant_id TenantId,t.name TenantName,(select count(*) from agro360.identity_users u where u.tenant_id=o.tenant_id and u.status='ACTIVE') ActiveUsers,p.user_limit UserLimit,(select count(*) from agro360.geo_farms f where f.tenant_id=o.tenant_id and f.deleted_at is null) Properties,p.property_limit PropertyLimit,(select count(*) from agro360.saas_devices d where d.tenant_id=o.tenant_id and d.revoked_at is null) Devices,p.device_limit DeviceLimit,coalesce(m.storage_used_mb,0) StorageUsedMb,p.storage_limit_mb StorageLimitMb,coalesce(m.tracked_lots,0) TrackedLots,coalesce(m.certificates,0) Certificates,coalesce(m.offline_records,0) OfflineRecords,coalesce(m.ledger_events,0) LedgerEvents,coalesce(m.exported_reports,0) ExportedReports from agro360.saas_organizations o join agro360.tenancy_tenants t on t.id=o.tenant_id join agro360.saas_plans p on p.id=o.plan_id left join agro360.saas_usage_metrics m on m.tenant_id=o.tenant_id";
    private static void ValidateTenant(TenantCommand settings) { if (string.IsNullOrWhiteSpace(settings.Name) || !SlugRegex().IsMatch(settings.Slug) || !EmailRegex().IsMatch(settings.ResponsibleEmail) || settings.PlanId == Guid.Empty) throw new ArgumentException("Organização, slug, CPF/CNPJ, responsável e plano válidos são obrigatórios."); SaasGovernanceRules.NormalizeAndValidateDocument(settings.Document); }
    private static void ValidateUpdate(TenantUpdateCommand settings) { if (string.IsNullOrWhiteSpace(settings.Name) || string.IsNullOrWhiteSpace(settings.ResponsibleName) || !EmailRegex().IsMatch(settings.ResponsibleEmail) || settings.PlanId == Guid.Empty) throw new ArgumentException("Dados da organização e plano são obrigatórios."); }
    private static void ValidatePlan(PlanCommand settings) { if (string.IsNullOrWhiteSpace(settings.Name) || settings.MonthlyPrice < 0 || settings.AnnualPrice < 0 || settings.UserLimit < 1 || settings.PropertyLimit < 1 || settings.StorageLimitMb < 1 || settings.DeviceLimit < 1 || settings.Modules.Length == 0) throw new ArgumentException("Plano e limites positivos são obrigatórios."); }
    private async Task<int> GetActorLevelAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, Guid actorId)
    {
        var level = await connection.ExecuteScalarAsync<int?>(
            """
            select max(case when lower(r.code)='tenant-administrator' then 100 else coalesce(m.level,10) end)
            from agro360.identity_user_roles ur
            join agro360.identity_roles r on r.tenant_id=ur.tenant_id and r.id=ur.role_id
            left join agro360.saas_role_metadata m on m.tenant_id=r.tenant_id and m.role_id=r.id
            where ur.tenant_id=@TenantId and ur.user_id=@Actor
            """, new { tenant.TenantId, Actor = actorId }, transaction);
        return level ?? throw new ForbiddenException("O ator não possui perfil administrativo válido nesta organização.");
    }
    private Task<long> CountActiveAdministratorsAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction) =>
        connection.ExecuteScalarAsync<long>(
            """
            select count(distinct u.id) from agro360.identity_users u
            join agro360.identity_user_roles ur on ur.tenant_id=u.tenant_id and ur.user_id=u.id
            join agro360.identity_roles r on r.tenant_id=ur.tenant_id and r.id=ur.role_id
            where u.tenant_id=@TenantId and u.status='ACTIVE' and u.deleted_at is null and lower(r.code)='tenant-administrator'
            """, new { tenant.TenantId }, transaction);
    private Task<int> LockTenantGovernanceAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, Guid? tenantId = null) =>
        connection.ExecuteAsync(
            "select pg_advisory_xact_lock(hashtextextended(@TenantKey,0));",
            new { TenantKey = (tenantId ?? tenant.TenantId).ToString("N") },
            transaction);
    private async Task EnsureUserCapacityAsync(System.Data.IDbConnection connection, System.Data.IDbTransaction transaction, Guid? tenantId = null)
    {
        var authorizedTenant = tenantId ?? tenant.TenantId;
        var limit = await connection.QuerySingleAsync<(long Used, int Limit)>(
            """
            select count(*) filter(where u.status='ACTIVE') Used,p.user_limit Limit
            from agro360.saas_organizations o
            join agro360.saas_plans p on p.id=o.plan_id
            left join agro360.identity_users u on u.tenant_id=o.tenant_id and u.deleted_at is null
            where o.tenant_id=@TenantId
            group by p.user_limit
            """, new { TenantId = authorizedTenant }, transaction);
        if (limit.Used < limit.Limit) return;
        InfrastructureLogMessages.UserLimitExceeded(logger, authorizedTenant);
        throw new ConflictException($"Limite do plano atingido: {limit.Used} de {limit.Limit} usuários.", "plan_user_limit");
    }
    private async Task EnsureActorCanGrantRolesAsync(
        System.Data.IDbConnection connection,
        System.Data.IDbTransaction transaction,
        Guid actorId,
        Guid[] roleIds,
        Guid? tenantId = null)
    {
        var authorizedTenant = tenantId ?? tenant.TenantId;
        var authorized = await connection.ExecuteScalarAsync<bool>(
            """
            select exists(
                select 1 from agro360.identity_users actor
                where actor.tenant_id=@TenantId and actor.id=@Actor and actor.status='ACTIVE' and actor.deleted_at is null)
            and (select count(*) from agro360.identity_roles target where target.tenant_id=@TenantId and target.id=any(@RoleIds))=@RoleCount
            and not exists(
                select 1 from agro360.identity_roles target
                where target.tenant_id=@TenantId and target.id=any(@RoleIds)
                  and (lower(target.code)='super_admin' or lower(target.code) like 'platform_%'))
            and not exists(
                select 1
                from agro360.identity_role_permissions requested
                where requested.tenant_id=@TenantId and requested.role_id=any(@RoleIds)
                  and not exists(
                      select 1
                      from agro360.identity_user_roles actor_role
                      join agro360.identity_role_permissions actor_permission
                        on actor_permission.tenant_id=actor_role.tenant_id and actor_permission.role_id=actor_role.role_id
                      where actor_role.tenant_id=@TenantId and actor_role.user_id=@Actor
                        and actor_permission.permission_id=requested.permission_id))
            """,
            new { TenantId = authorizedTenant, Actor = actorId, RoleIds = roleIds, RoleCount = roleIds.Length },
            transaction);
        if (!authorized)
            throw new ForbiddenException("O ator não possui autoridade atual para conceder todas as permissões dos perfis solicitados.");
    }
    private static ValidationException InvalidReferences(string field, string message) => new(new Dictionary<string, string[]> { [field] = [message] });
    private static string CreateInvitationToken(Guid tenantId) => $"{tenantId:N}.{Microsoft.IdentityModel.Tokens.Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32))}";
    private static string HashInvitationToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
    private static bool TryReadInvitationTenant(string token, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token)) return false;
        var separator = token.IndexOf('.');
        return separator == 32 && Guid.TryParseExact(token[..separator], "N", out tenantId);
    }
    private Task<T> System<T>(string op, Func<Npgsql.NpgsqlConnection, Npgsql.NpgsqlTransaction, Task<T>> work, CancellationToken ct) => Guard(op, () => db.InSystemTransactionAsync(work, ct));
    private Task<bool> System(string op, Func<Npgsql.NpgsqlConnection, Npgsql.NpgsqlTransaction, Task> work, CancellationToken ct) => Guard(op, () => db.InSystemTransactionAsync(async (c, t) => { await work(c, t); return true; }, ct));
    private Task<T> Tenant<T>(string op, Func<Npgsql.NpgsqlConnection, Npgsql.NpgsqlTransaction, Task<T>> work, CancellationToken ct) => Guard(op, () => db.InTenantTransactionAsync(work, ct));
    private Task Tenant(string op, Func<Npgsql.NpgsqlConnection, Npgsql.NpgsqlTransaction, Task> work, CancellationToken ct) => Guard(op, () => db.InTenantTransactionAsync(work, ct));
    private async Task<T> Guard<T>(string op, Func<Task<T>> work) { try { return await work(); } catch (Exception ex) when (ex is not PersistenceException) { InfrastructureLogMessages.SaasFailed(logger, op, tenant.IsAvailable ? tenant.TenantId : Guid.Empty, ex); throw; } }
    private async Task Guard(string op, Func<Task> work) { try { await work(); } catch (Exception ex) when (ex is not PersistenceException) { InfrastructureLogMessages.SaasFailed(logger, op, tenant.IsAvailable ? tenant.TenantId : Guid.Empty, ex); throw; } }
    private static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());
    private sealed class UserAccessLookup
    {
        public string Status { get; init; } = string.Empty;
        public bool IsAdministrator { get; init; }
    }
    private sealed class RoleGrantLookup { public Guid Id { get; init; } public string Code { get; init; } = string.Empty; public bool IsSystem { get; init; } public int Level { get; init; } }
    private sealed class UserRoleState { public string Status { get; init; } = string.Empty; public bool IsAdministrator { get; init; } }
    private sealed class InvitationAcceptanceLookup { public Guid Id { get; init; } public string Email { get; init; } = string.Empty; public Guid RoleId { get; init; } public Guid InvitedBy { get; init; } public DateTimeOffset ExpiresAt { get; init; } public string Status { get; init; } = string.Empty; public string TenantSlug { get; init; } = string.Empty; }
    private sealed class InvitationUserLookup { public Guid Id { get; init; } public string Status { get; init; } = string.Empty; }
    private sealed class PlanProvisioningLookup { public Guid Id { get; init; } public string Name { get; init; } = string.Empty; public decimal MonthlyPrice { get; init; } public string[] Modules { get; init; } = []; }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")] private static partial Regex SlugRegex();
    [GeneratedRegex("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$")] private static partial Regex EmailRegex();
}
