namespace Agro360.Infrastructure.Security;

/// <summary>
/// Fonte única dos módulos efetivamente contratados por um tenant.
/// O snapshot de provisionamento (platform_tenant_module_entitlements) é a fonte primária
/// dos direitos e o marketplace complementa módulos ativos dentro da janela de trial.
/// Bloqueios explícitos (BLOCKED/SUSPENDED/DELINQUENT/INACTIVE) prevalecem sobre concessões positivas.
/// A composição ao vivo do plano deixa de provar contrato: desativar um plano no catálogo
/// nunca corta contratos vigentes (o catálogo não modifica contratos existentes).
/// </summary>
public static class EntitlementQueries
{
    public const string ModuleCodeSelect = """
        select lower(contracted_modules.code) as module_code
        from (
            select c.code
            from agro360.platform_tenant_module_entitlements e
            join agro360.platform_module_catalog c on c.id = e.module_id
            where e.tenant_id = @TenantId and e.status in ('CONTRACTED','ACTIVE','TRIAL')
              and (e.activated_at is null or e.activated_at <= now())
              and (e.valid_until is null or e.valid_until > now())
            union
            select m.code
            from agro360.platform_tenant_modules tm
            join agro360.platform_marketplace_modules m on m.id = tm.module_id
            where tm.tenant_id = @TenantId and tm.status = 'ACTIVE'
              and (tm.trial_ends_at is null or tm.trial_ends_at > now())
        ) contracted_modules
        where not exists (
            select 1
            from agro360.platform_tenant_module_entitlements be
            join agro360.platform_module_catalog bc on bc.id = be.module_id
            where be.tenant_id = @TenantId
              and lower(bc.code) = lower(contracted_modules.code)
              and be.status in ('BLOCKED','SUSPENDED','DELINQUENT','INACTIVE')
        )
        and not exists (
            select 1
            from agro360.platform_tenant_modules btm
            join agro360.platform_marketplace_modules bm on bm.id = btm.module_id
            where btm.tenant_id = @TenantId
              and lower(bm.code) = lower(contracted_modules.code)
              and btm.status in ('BLOCKED','SUSPENDED','DELINQUENT','INACTIVE')
        )
        """;
}
