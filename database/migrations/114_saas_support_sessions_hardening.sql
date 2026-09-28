-- AG-SaaS-ADM-004 - Hardening de suporte assistido e permissões da aplicação
begin;
set local search_path to agro360, public;

alter table agro360.saas_support_sessions enable row level security;
alter table agro360.saas_support_sessions force row level security;

drop policy if exists saas_support_sessions_isolation on agro360.saas_support_sessions;
create policy saas_support_sessions_isolation on agro360.saas_support_sessions
    using (
        nullif(current_setting('app.tenant_id', true), '')::uuid is null
        or tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    )
    with check (
        nullif(current_setting('app.tenant_id', true), '')::uuid is null
        or tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid
    );

do $$
begin
    if exists (select 1 from pg_roles where rolname = 'agro360_app') then
        grant select, insert, update, delete on agro360.saas_support_sessions to agro360_app;
    end if;
end $$;

insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.4.0', 'Hardening de suporte assistido e permissões da aplicação', now())
on conflict(version) do nothing;

commit;
