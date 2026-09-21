begin;

create table if not exists agro360.operational_genealogy_links (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    kind varchar(60) not null,
    origin_type varchar(40) not null,
    origin_id uuid not null,
    destination_type varchar(40) not null,
    destination_id uuid not null,
    season_id uuid,
    field_id uuid,
    lot_number varchar(100),
    quantity numeric(20,6),
    unit varchar(20),
    status varchar(30) not null default 'ACTIVE',
    metadata jsonb,
    idempotency_key varchar(160) not null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique (tenant_id, id),
    unique (tenant_id, idempotency_key),
    unique (tenant_id, kind, origin_id, destination_id),
    foreign key (tenant_id, season_id) references agro360.agriculture_seasons(tenant_id, id),
    foreign key (tenant_id, field_id) references agro360.geo_fields(tenant_id, id),
    check (quantity is null or quantity > 0)
);

create index if not exists ix_genealogy_links_season on agro360.operational_genealogy_links(tenant_id, season_id) where season_id is not null;
create index if not exists ix_genealogy_links_lot on agro360.operational_genealogy_links(tenant_id, lot_number) where lot_number is not null;
create index if not exists ix_genealogy_links_origin on agro360.operational_genealogy_links(tenant_id, origin_type, origin_id);
create index if not exists ix_genealogy_links_destination on agro360.operational_genealogy_links(tenant_id, destination_type, destination_id);

select agro360.platform_enable_tenant_rls('agro360.operational_genealogy_links');

insert into agro360.platform_schema_versions(version, description, installed_at)
values('9.5.0', 'Genealogia operacional da safra até produção e expedição (AG-E6-GEN-001)', now())
on conflict(version) do nothing;

commit;
