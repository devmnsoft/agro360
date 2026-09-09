using System.Data.Common;
using Agro360.Application;
using Dapper;

namespace Agro360.Infrastructure.Security;

internal static class AccessDecision
{
    private const string EffectiveModulesSql = """
        with subscription_state as (
            select
                exists(select 1 from agro360.saas_subscriptions s where s.tenant_id=@TenantId and s.deleted_at is null) has_history,
                exists(
                    select 1 from agro360.saas_subscriptions s
                    where s.tenant_id=@TenantId and s.deleted_at is null
                      and s.status in ('ACTIVE','TRIAL','COURTESY')
                      and s.starts_on<=current_date and (s.ends_on is null or s.ends_on>=current_date)) has_current
        ),
        contract_state as (
            select case when state.has_history then state.has_current else exists(
                select 1 from agro360.saas_organizations o
                join agro360.saas_plans p on p.id=o.plan_id
                where o.tenant_id=@TenantId and o.status='ACTIVE' and p.active and p.deleted_at is null)
            end allowed
            from subscription_state state
        ),
        plan_modules as (
            select lower(unnest(p.modules)) code
            from agro360.saas_subscriptions s
            join agro360.saas_plans p on p.id=s.plan_id
            where s.tenant_id=@TenantId and s.deleted_at is null and p.active and p.deleted_at is null
              and s.status in ('ACTIVE','TRIAL','COURTESY')
              and s.starts_on<=current_date and (s.ends_on is null or s.ends_on>=current_date)
            union
            select lower(unnest(p.modules)) code
            from agro360.saas_organizations o
            join agro360.saas_plans p on p.id=o.plan_id
            cross join subscription_state state
            where not state.has_history and o.tenant_id=@TenantId and o.status='ACTIVE'
              and p.active and p.deleted_at is null
        ),
        candidates as (
            select code from plan_modules
            union
            select lower(c.code) from agro360.platform_tenant_module_entitlements entitlement
            join agro360.platform_module_catalog c on c.id=entitlement.module_id
            where entitlement.tenant_id=@TenantId and entitlement.status in('CONTRACTED','ACTIVE','TRIAL') and c.active
            union
            select lower(m.code) from agro360.platform_tenant_modules tenant_module
            join agro360.platform_marketplace_modules m on m.id=tenant_module.module_id
            where tenant_module.tenant_id=@TenantId and tenant_module.status='ACTIVE'
              and tenant_module.deleted_at is null and m.status='ACTIVE' and m.deleted_at is null
              and (tenant_module.trial_ends_at is null or tenant_module.trial_ends_at>now())
            union
            select lower(feature.code) from agro360.saas_tenant_feature_flags flag
            join agro360.saas_feature_flags feature on feature.id=flag.feature_id
            where flag.tenant_id=@TenantId and flag.enabled and flag.origin<>'ADMIN_BLOCK'
              and (flag.expires_at is null or flag.expires_at>now()) and feature.active and feature.deleted_at is null
        ),
        blocked as (
            select lower(c.code) code from agro360.platform_tenant_module_entitlements entitlement
            join agro360.platform_module_catalog c on c.id=entitlement.module_id
            where entitlement.tenant_id=@TenantId and entitlement.status in('BLOCKED','SUSPENDED','DELINQUENT')
            union
            select lower(m.code) from agro360.platform_tenant_modules tenant_module
            join agro360.platform_marketplace_modules m on m.id=tenant_module.module_id
            where tenant_module.tenant_id=@TenantId and tenant_module.status='BLOCKED' and tenant_module.deleted_at is null
            union
            select lower(feature.code) from agro360.saas_tenant_feature_flags flag
            join agro360.saas_feature_flags feature on feature.id=flag.feature_id
            where flag.tenant_id=@TenantId and (not flag.enabled or flag.origin='ADMIN_BLOCK')
              and (flag.expires_at is null or flag.expires_at>now())
        )
        select distinct candidate.code
        from candidates candidate
        cross join contract_state contract
        where contract.allowed
          and not exists(select 1 from blocked where blocked.code=candidate.code)
          and not exists(
              select 1 from agro360.platform_module_catalog module
              join agro360.platform_module_dependencies dependency on dependency.module_id=module.id
              join agro360.platform_module_catalog required on required.id=dependency.depends_on_id
              where lower(module.code)=candidate.code
                and (not exists(select 1 from candidates available where available.code=lower(required.code))
                     or exists(select 1 from blocked denied where denied.code=lower(required.code))))
          and not exists(
              select 1 from agro360.platform_marketplace_modules module
              cross join lateral unnest(module.dependencies) required(code)
              where lower(module.code)=candidate.code
                and (not exists(select 1 from candidates available where available.code=lower(required.code))
                     or exists(select 1 from blocked denied where denied.code=lower(required.code))))
        order by candidate.code;
        """;

    public static async Task<HashSet<string>> EffectiveModulesAsync(
        DbConnection connection,
        DbTransaction transaction,
        Guid tenantId,
        CancellationToken cancellationToken)
    {
        var modules = await connection.QueryAsync<string>(new CommandDefinition(
            EffectiveModulesSql,
            new { TenantId = tenantId },
            transaction,
            cancellationToken: cancellationToken)).ConfigureAwait(false);
        return modules.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsPermissionContracted(string permission, IReadOnlySet<string> effectiveModules)
    {
        if (permission.StartsWith("account.", StringComparison.OrdinalIgnoreCase)) return true;
        return AcceptedModules(permission).Any(effectiveModules.Contains);
    }

    public static string[] AcceptedModules(string permission)
    {
        var group = permission.Split('.', 2, StringSplitOptions.TrimEntries)[0];
        return group switch
        {
            "properties" => ["properties"],
            "agriculture" => ["agriculture"],
            "inventory" => ["inventory"],
            "livestock" => ["livestock"],
            "crm" or "commercial" or "commercial-saas" or "customer-success" => ["commercial"],
            "finance" => ["finance"],
            "purchasing" => ["purchasing"],
            "production" => ["agroindustry"],
            "fleet" or "maintenance" => ["fleet"],
            "dashboard" => ["reports", "analytics"],
            "storage" => ["inventory", "warehousing"],
            "logistics" or "regional-logistics" => ["logistics"],
            "traceability" or "ledger" or "sales-network" => ["traceability"],
            "intelligence" => ["reports", "intelligence", "analytics", "ai", "predictive-ai"],
            "compliance" or "esg" or "sustainability" => ["environment-esg"],
            "maps" => ["properties", "analytics"],
            "cooperative" => ["cooperatives"],
            "rural-hr" or "sst" => ["verticals", "rural-hr"],
            "documents" or "evidences" or "dossiers" or "certificates" => ["documents"],
            "mobile" or "field-checklists" => ["mobile"],
            "export" or "fiscal" => [group],
            "marketplace" or "partners" or "api-keys" or "integrations" => ["platform", "marketplace"],
            "deployment" or "governance" or "lgpd" or "security" or "work" or "support" or "portal" => ["platform"],
            _ => []
        };
    }
}
