begin;

-- Auditoria e exclusão lógica padronizadas para pecuária e frota.
-- Não inventa created_by/updated_by para registros legados (colunas nullable).
-- deleted_at é a fonte única de exclusão lógica; status/active permanecem operacionais.

do $$
declare
    t text;
begin
    for t in
        select tablename
        from pg_tables
        where schemaname = 'agro360'
          and (tablename like 'fleet_%' or tablename like 'livestock_%')
    loop
        execute format('
            alter table agro360.%I
                add column if not exists created_at timestamptz not null default now(),
                add column if not exists created_by uuid,
                add column if not exists updated_at timestamptz not null default now(),
                add column if not exists updated_by uuid,
                add column if not exists deleted_at timestamptz,
                add column if not exists deleted_by uuid,
                add column if not exists deletion_reason text;
        ', t);
    end loop;
end $$;

-- Unicidade apenas entre registros ativos (exclusão lógica não libera o histórico).
create unique index if not exists ux_fleet_assets_internal_code_active
    on agro360.fleet_assets (tenant_id, lower(internal_code))
    where deleted_at is null and internal_code is not null;

create unique index if not exists ux_fleet_assets_plate_active
    on agro360.fleet_assets (tenant_id, upper(plate))
    where deleted_at is null and plate is not null;

create unique index if not exists ux_livestock_animals_tag_active
    on agro360.livestock_animals (tenant_id, lower(tag))
    where deleted_at is null and tag is not null;

-- Histórico de auditoria: sem exclusão pela interface comum (app role).
do $$
begin
    if exists (select 1 from pg_roles where rolname = 'agro360_app') then
        revoke delete, truncate on agro360.audit_logs from agro360_app;
        execute 'grant select, insert on agro360.audit_logs to agro360_app';
    end if;
exception when undefined_object then
    null;
end $$;

insert into agro360.platform_schema_versions(version, description, installed_at)
values ('7.0.0', 'Auditoria e exclusao logica padronizadas (pecuaria/frota)', now())
on conflict (version) do update set description = excluded.description;

commit;
