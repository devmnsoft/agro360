-- Pecuária integrada: identidade, locais, controle coletivo e trilha operacional.
-- Migração aditiva e reexecutável; não altera migrations publicadas.

alter table agro360.livestock_animals
    add column if not exists internal_identifier varchar(80),
    add column if not exists birth_date_estimated boolean not null default false,
    add column if not exists origin varchar(160),
    add column if not exists notes text;

update agro360.livestock_animals set internal_identifier=tag where internal_identifier is null;
alter table agro360.livestock_animals alter column internal_identifier set not null;
create unique index if not exists ux_livestock_animals_tenant_internal_identifier
    on agro360.livestock_animals(tenant_id, lower(internal_identifier)) where deleted_at is null;

create table if not exists agro360.livestock_identifier_history (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    animal_id uuid not null,
    identifier_type varchar(20) not null check(identifier_type in ('INTERNAL','EAR_TAG','RFID')),
    identifier varchar(80) not null,
    valid_from timestamptz not null,
    valid_until timestamptz,
    change_reason text not null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    constraint fk_livestock_identifier_history_animal foreign key (tenant_id, animal_id)
        references agro360.livestock_animals(tenant_id, id),
    check(valid_until is null or valid_until >= valid_from)
);
create unique index if not exists ux_livestock_identifier_current
    on agro360.livestock_identifier_history(tenant_id, identifier_type, lower(identifier)) where valid_until is null;

create table if not exists agro360.livestock_locations (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    farm_id uuid not null,
    parent_id uuid,
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
    constraint uq_livestock_locations_tenant_id unique(tenant_id, id),
    constraint uq_livestock_locations_tenant_farm_name unique(tenant_id, farm_id, name),
    constraint fk_livestock_locations_farm foreign key (tenant_id, farm_id)
        references agro360.geo_farms(tenant_id, id),
    constraint fk_livestock_locations_parent foreign key (tenant_id, parent_id)
        references agro360.livestock_locations(tenant_id, id),
    check(capacity is null or capacity > 0)
);

alter table agro360.livestock_herds
    add column if not exists control_mode varchar(16) not null default 'COLLECTIVE',
    add column if not exists location_id uuid,
    add column if not exists version bigint not null default 1;
do $$ begin
    if not exists(select 1 from pg_constraint where conname='ck_livestock_herds_control_mode') then
        alter table agro360.livestock_herds add constraint ck_livestock_herds_control_mode check(control_mode in ('COLLECTIVE','INDIVIDUAL'));
    end if;
end $$;
do $$ begin
    if not exists(select 1 from pg_constraint where conname='fk_livestock_herds_location' and conrelid='agro360.livestock_herds'::regclass) then
        alter table agro360.livestock_herds add constraint fk_livestock_herds_location
            foreign key (tenant_id, location_id) references agro360.livestock_locations(tenant_id, id);
    end if;
end $$;

create table if not exists agro360.livestock_group_movements (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    herd_id uuid not null,
    movement_type varchar(20) not null check(movement_type in ('ENTRY','TRANSFER','LOT_CHANGE','LOCATION_CHANGE','SALE','DEATH','DISCARD','ADJUSTMENT','REVERSAL')),
    quantity integer not null check(quantity > 0),
    from_farm_id uuid,
    to_farm_id uuid,
    from_location_id uuid,
    to_location_id uuid,
    reason varchar(160) not null,
    occurred_at timestamptz not null,
    responsible_id uuid not null,
    reverses_id uuid,
    idempotency_key varchar(120),
    created_at timestamptz not null default now(),
    created_by uuid not null,
    constraint uq_livestock_group_movements_tenant_id unique(tenant_id, id),
    constraint uq_livestock_group_movements_idempotency unique(tenant_id, idempotency_key),
    constraint fk_livestock_group_movements_herd foreign key (tenant_id, herd_id)
        references agro360.livestock_herds(tenant_id, id),
    constraint fk_livestock_group_movements_from_farm foreign key (tenant_id, from_farm_id)
        references agro360.geo_farms(tenant_id, id),
    constraint fk_livestock_group_movements_to_farm foreign key (tenant_id, to_farm_id)
        references agro360.geo_farms(tenant_id, id),
    constraint fk_livestock_group_movements_from_location foreign key (tenant_id, from_location_id)
        references agro360.livestock_locations(tenant_id, id),
    constraint fk_livestock_group_movements_to_location foreign key (tenant_id, to_location_id)
        references agro360.livestock_locations(tenant_id, id),
    constraint fk_livestock_group_movements_reversal foreign key (tenant_id, reverses_id)
        references agro360.livestock_group_movements(tenant_id, id)
);

create table if not exists agro360.livestock_individualization_reconciliations (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    herd_id uuid not null,
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
    constraint fk_livestock_reconciliations_herd foreign key (tenant_id, herd_id)
        references agro360.livestock_herds(tenant_id, id),
    check(status <> 'CONFIRMED' or collective_quantity = identified_quantity)
);

alter table agro360.livestock_identifier_history enable row level security;
alter table agro360.livestock_identifier_history force row level security;
drop policy if exists tenant_isolation on agro360.livestock_identifier_history;
create policy tenant_isolation on agro360.livestock_identifier_history
    using (tenant_id=agro360.platform_current_tenant_id())
    with check (tenant_id=agro360.platform_current_tenant_id());

alter table agro360.livestock_locations enable row level security;
alter table agro360.livestock_locations force row level security;
drop policy if exists tenant_isolation on agro360.livestock_locations;
create policy tenant_isolation on agro360.livestock_locations
    using (tenant_id=agro360.platform_current_tenant_id())
    with check (tenant_id=agro360.platform_current_tenant_id());

alter table agro360.livestock_group_movements enable row level security;
alter table agro360.livestock_group_movements force row level security;
drop policy if exists tenant_isolation on agro360.livestock_group_movements;
create policy tenant_isolation on agro360.livestock_group_movements
    using (tenant_id=agro360.platform_current_tenant_id())
    with check (tenant_id=agro360.platform_current_tenant_id());

alter table agro360.livestock_individualization_reconciliations enable row level security;
alter table agro360.livestock_individualization_reconciliations force row level security;
drop policy if exists tenant_isolation on agro360.livestock_individualization_reconciliations;
create policy tenant_isolation on agro360.livestock_individualization_reconciliations
    using (tenant_id=agro360.platform_current_tenant_id())
    with check (tenant_id=agro360.platform_current_tenant_id());

insert into agro360.platform_schema_versions(version, description, installed_at)
values ('0.6.8', 'Pecuária integrada - fundações de rebanho e rastreabilidade', now())
on conflict(version) do nothing;
