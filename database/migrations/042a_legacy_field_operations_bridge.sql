begin;

-- Sprint 43 extends a legacy occurrence table that was not present in the
-- incremental foundation. Materialize the expected compatibility contract;
-- operational agriculture records remain in agriculture.field_operations.
create schema if not exists field_operations;

create table if not exists field_operations.occurrences(
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenancy.tenants(id),
    occurrence_type varchar(60) not null default 'OTHER',
    description text not null default '',
    occurred_at timestamptz not null default now(),
    created_at timestamptz not null default now(),
    created_by uuid,
    unique(tenant_id,id)
);

insert into platform.schema_versions(version,description,installed_at)
values('4.2.1','Bridge da tabela de ocorrências esperada pela Sprint 43',now())
on conflict(version) do update set description=excluded.description;

commit;
