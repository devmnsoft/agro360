begin;

alter table agro360.production_material_consumptions
    add column if not exists idempotency_key varchar(160),
    add column if not exists request_hash char(64);

create unique index if not exists ux_prod_material_consumptions_idempotency
    on agro360.production_material_consumptions(tenant_id, idempotency_key)
    where idempotency_key is not null and deleted_at is null;

do $$ begin
    perform agro360.platform_enable_tenant_rls('agro360.production_material_consumptions');
    execute 'alter table agro360.production_material_consumptions force row level security';
    if exists (select 1 from pg_roles where rolname = 'agro360_app') then
        execute 'grant usage on schema agro360 to agro360_app';
        execute 'grant select, insert, update, delete on agro360.production_material_consumptions to agro360_app';
    end if;
end $$;

insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.8.0', 'Integridade e idempotência de consumo industrial', now())
on conflict(version) do nothing;

commit;
