-- AG-SaaS-ADM-005 - Revisão de política RLS para sessões de suporte (ausência de tenant_id não autoriza globalmente sem platform_context)
begin;
set local search_path to agro360, public;

drop policy if exists saas_support_sessions_isolation on agro360.saas_support_sessions;
create policy saas_support_sessions_isolation on agro360.saas_support_sessions
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.5.0', 'Revisão da política RLS para sessões de suporte com plataforma explícita', now())
on conflict(version) do nothing;

commit;
