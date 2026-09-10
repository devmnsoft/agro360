-- Pecuária integrada: identidade, locais, controle coletivo e trilha operacional.
-- Migração aditiva e reexecutável; não altera migrations publicadas.

alter table agro360.livestock_animals
    add column if not exists internal_identifier varchar(80),
    add column if not exists birth_date_estimated boolean not null default false,
    add column if not exists origin varchar(160),
    add column if not exists notes text;

update agro360.livestock_animals
set internal_identifier = coalesce(nullif(internal_identifier, ''), tag)
where internal_identifier is null or internal_identifier = '';
-- Nullable para permitir seeds aditivos posteriores; unicidade apenas quando preenchido.
create unique index if not exists ux_livestock_animals_tenant_internal_identifier
    on agro360.livestock_animals(tenant_id, lower(internal_identifier))
    where deleted_at is null and internal_identifier is not null;

create table if not exists agro360.livestock_identifier_history (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    animal_id uuid not null references agro360.livestock_animals(id),
    identifier_type varchar(20) not null check(identifier_type in ('INTERNAL','EAR_TAG','RFID')),
    identifier varchar(80) not null,
    valid_from timestamptz not null,
    valid_until timestamptz,
    change_reason text not null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    check(valid_until is null or valid_until >= valid_from)
);
create unique index if not exists ux_livestock_identifier_current
    on agro360.livestock_identifier_history(tenant_id, identifier_type, lower(identifier)) where valid_until is null;

create table if not exists agro360.livestock_locations (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    farm_id uuid not null references agro360.geo_farms(id),
    parent_id uuid references agro360.livestock_locations(id),
    name varchar(120) not null,
    location_type varchar(20) not null check(location_type in ('PASTURE','PADDOCK','CORRAL','FACILITY')),
    status varchar(20) not null default 'ACTIVE' check(status in ('ACTIVE','INACTIVE','QUARANTINE')),
    capacity numeric(14,3),
    capacity_unit varchar(16),
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, farm_id, name),
    check(capacity is null or capacity > 0)
);

alter table agro360.livestock_herds
    add column if not exists control_mode varchar(16) not null default 'COLLECTIVE',
    add column if not exists location_id uuid references agro360.livestock_locations(id),
    add column if not exists version bigint not null default 1;
do $$ begin
    if not exists(select 1 from pg_constraint where conname='ck_livestock_herds_control_mode') then
        alter table agro360.livestock_herds add constraint ck_livestock_herds_control_mode check(control_mode in ('COLLECTIVE','INDIVIDUAL'));
    end if;
end $$;

create table if not exists agro360.livestock_group_movements (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    herd_id uuid not null references agro360.livestock_herds(id),
    movement_type varchar(20) not null check(movement_type in ('ENTRY','TRANSFER','LOT_CHANGE','LOCATION_CHANGE','SALE','DEATH','DISCARD','ADJUSTMENT','REVERSAL')),
    quantity integer not null check(quantity > 0),
    from_farm_id uuid references agro360.geo_farms(id),
    to_farm_id uuid references agro360.geo_farms(id),
    from_location_id uuid references agro360.livestock_locations(id),
    to_location_id uuid references agro360.livestock_locations(id),
    reason varchar(160) not null,
    occurred_at timestamptz not null,
    responsible_id uuid not null,
    reverses_id uuid references agro360.livestock_group_movements(id),
    idempotency_key varchar(120),
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique(tenant_id, idempotency_key)
);

create table if not exists agro360.livestock_individualization_reconciliations (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    herd_id uuid not null references agro360.livestock_herds(id),
    collective_quantity integer not null check(collective_quantity >= 0),
    identified_quantity integer not null check(identified_quantity >= 0),
    difference integer generated always as (collective_quantity - identified_quantity) stored,
    occurred_at timestamptz not null,
    reason text not null,
    status varchar(16) not null check(status in ('DRAFT','CONFIRMED','REVERSED')),
    created_at timestamptz not null default now(),
    created_by uuid not null,
    confirmed_at timestamptz,
    confirmed_by uuid,
    check(status <> 'CONFIRMED' or collective_quantity = identified_quantity)
);

select agro360.platform_enable_tenant_rls('agro360.livestock_identifier_history');
select agro360.platform_enable_tenant_rls('agro360.livestock_locations');
select agro360.platform_enable_tenant_rls('agro360.livestock_group_movements');
select agro360.platform_enable_tenant_rls('agro360.livestock_individualization_reconciliations');

insert into agro360.platform_schema_versions(version, description, installed_at)
values ('0.6.8', 'Pecuária integrada - fundações de rebanho e rastreabilidade', now())
on conflict(version) do nothing;
