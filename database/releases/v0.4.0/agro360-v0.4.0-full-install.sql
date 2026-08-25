create extension if not exists pgcrypto;
create extension if not exists postgis;
create extension if not exists pg_trgm;
create extension if not exists unaccent;

create schema if not exists platform;
create schema if not exists identity;
create schema if not exists tenancy;
create schema if not exists organization;
create schema if not exists geo;
create schema if not exists agriculture;
create schema if not exists agronomy;
create schema if not exists precision_agriculture;
create schema if not exists livestock;
create schema if not exists pasture;
create schema if not exists dairy;
create schema if not exists forestry;
create schema if not exists inventory;
create schema if not exists warehouse;
create schema if not exists fleet;
create schema if not exists purchasing;
create schema if not exists finance;
create schema if not exists cost;
create schema if not exists commercial;
create schema if not exists logistics;
create schema if not exists traceability;
create schema if not exists documents;
create schema if not exists environment;
create schema if not exists hr;
create schema if not exists workflow;
create schema if not exists notification;
create schema if not exists analytics;
create schema if not exists ai;
create schema if not exists iot;
create schema if not exists integration;
create schema if not exists audit;

create table if not exists platform.units (
    code varchar(16) primary key,
    name varchar(80) not null,
    dimension varchar(40) not null,
    base_unit_code varchar(16) null references platform.units(code),
    conversion_factor numeric(24,10) not null default 1,
    active boolean not null default true,
    constraint ck_units_factor check (conversion_factor > 0)
);

create table if not exists platform.modules (
    code varchar(80) primary key,
    name varchar(120) not null,
    phase smallint not null,
    status varchar(24) not null default 'PLANNED',
    description varchar(500) not null,
    constraint ck_modules_status check (status in ('FOUNDATION', 'CORE', 'PLANNED', 'BETA', 'ACTIVE'))
);

create table if not exists tenancy.tenants (
    id uuid primary key,
    code bigint generated always as identity unique,
    name varchar(160) not null,
    slug varchar(80) not null unique,
    timezone_id varchar(64) not null default 'America/Belem',
    status smallint not null default 1,
    plan_code varchar(40) not null default 'ESSENTIAL',
    created_at timestamptz not null default now(),
    updated_at timestamptz null,
    suspended_at timestamptz null,
    deleted_at timestamptz null,
    version bigint not null default 1,
    constraint ck_tenants_status check (status between 1 and 5),
    constraint ck_tenants_slug check (slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$')
);

create table if not exists organization.organizations (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    parent_id uuid null,
    type varchar(40) not null,
    name varchar(160) not null,
    legal_name varchar(200) null,
    document_number varchar(30) null,
    created_at timestamptz not null default now(),
    created_by uuid null,
    updated_at timestamptz null,
    updated_by uuid null,
    deleted_at timestamptz null,
    deleted_by uuid null,
    version bigint not null default 1,
    constraint uq_organizations_tenant_id unique (tenant_id, id),
    constraint fk_organizations_parent foreign key (tenant_id, parent_id)
        references organization.organizations(tenant_id, id),
    constraint ck_organizations_type check (type in ('ECONOMIC_GROUP', 'COMPANY', 'UNIT', 'COOPERATIVE', 'INDUSTRY', 'DISTRIBUTOR'))
);

create unique index if not exists ux_organizations_document
    on organization.organizations (tenant_id, document_number)
    where document_number is not null and deleted_at is null;

create table if not exists identity.users (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    name varchar(160) not null,
    email varchar(254) not null,
    password_hash varchar(500) not null,
    status varchar(24) not null default 'ACTIVE',
    mfa_enabled boolean not null default false,
    last_login_at timestamptz null,
    created_at timestamptz not null default now(),
    created_by uuid null,
    updated_at timestamptz null,
    updated_by uuid null,
    deleted_at timestamptz null,
    deleted_by uuid null,
    version bigint not null default 1,
    constraint uq_users_tenant_id unique (tenant_id, id),
    constraint ck_users_status check (status in ('INVITED', 'ACTIVE', 'LOCKED', 'DISABLED'))
);

create unique index if not exists ux_users_tenant_email
    on identity.users (tenant_id, lower(email))
    where deleted_at is null;

create table if not exists identity.permissions (
    id uuid primary key default gen_random_uuid(),
    code varchar(120) not null unique,
    module varchar(80) not null,
    description varchar(300) not null
);

create table if not exists identity.roles (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    code varchar(80) not null,
    name varchar(120) not null,
    is_system boolean not null default false,
    created_at timestamptz not null default now(),
    constraint uq_roles_tenant_id unique (tenant_id, id),
    constraint uq_roles_tenant_code unique (tenant_id, code)
);

create table if not exists identity.user_roles (
    tenant_id uuid not null references tenancy.tenants(id),
    user_id uuid not null,
    role_id uuid not null,
    primary key (tenant_id, user_id, role_id),
    constraint fk_user_roles_user foreign key (tenant_id, user_id)
        references identity.users(tenant_id, id),
    constraint fk_user_roles_role foreign key (tenant_id, role_id)
        references identity.roles(tenant_id, id)
);

create table if not exists identity.role_permissions (
    tenant_id uuid not null references tenancy.tenants(id),
    role_id uuid not null,
    permission_id uuid not null references identity.permissions(id),
    primary key (tenant_id, role_id, permission_id),
    constraint fk_role_permissions_role foreign key (tenant_id, role_id)
        references identity.roles(tenant_id, id)
);

create table if not exists identity.refresh_tokens (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    user_id uuid not null,
    token_hash char(64) not null,
    expires_at timestamptz not null,
    created_at timestamptz not null default now(),
    revoked_at timestamptz null,
    device_id varchar(160) null,
    constraint fk_refresh_tokens_user foreign key (tenant_id, user_id)
        references identity.users(tenant_id, id),
    constraint uq_refresh_tokens_hash unique (tenant_id, token_hash)
);

create index if not exists ix_refresh_tokens_user_active
    on identity.refresh_tokens (tenant_id, user_id, expires_at)
    where revoked_at is null;

create table if not exists geo.farms (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    organization_id uuid not null,
    code bigint generated always as identity,
    name varchar(160) not null,
    state char(2) not null,
    municipality varchar(120) null,
    total_area_ha numeric(18,4) not null,
    useful_area_ha numeric(18,4) null,
    registration_number varchar(80) null,
    car_number varchar(100) null,
    ccir_number varchar(100) null,
    itr_number varchar(100) null,
    boundary geometry(MultiPolygon, 4326) null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz null,
    updated_by uuid null,
    deleted_at timestamptz null,
    deleted_by uuid null,
    version bigint not null default 1,
    constraint uq_farms_tenant_id unique (tenant_id, id),
    constraint uq_farms_tenant_code unique (tenant_id, code),
    constraint fk_farms_organization foreign key (tenant_id, organization_id)
        references organization.organizations(tenant_id, id),
    constraint ck_farms_total_area check (total_area_ha > 0),
    constraint ck_farms_useful_area check (useful_area_ha is null or (useful_area_ha >= 0 and useful_area_ha <= total_area_ha))
);

create index if not exists ix_farms_boundary on geo.farms using gist (boundary);
create index if not exists ix_farms_name_trgm on geo.farms using gin (name gin_trgm_ops);

create table if not exists geo.fields (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    farm_id uuid not null,
    code bigint generated always as identity,
    name varchar(120) not null,
    area_ha numeric(18,4) not null,
    boundary geometry(MultiPolygon, 4326) null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz null,
    updated_by uuid null,
    deleted_at timestamptz null,
    deleted_by uuid null,
    version bigint not null default 1,
    constraint uq_fields_tenant_id unique (tenant_id, id),
    constraint uq_fields_farm_name unique (tenant_id, farm_id, name),
    constraint fk_fields_farm foreign key (tenant_id, farm_id)
        references geo.farms(tenant_id, id),
    constraint ck_fields_area check (area_ha > 0)
);

create index if not exists ix_fields_boundary on geo.fields using gist (boundary);
create index if not exists ix_fields_name_trgm on geo.fields using gin (name gin_trgm_ops);

create table if not exists agriculture.seasons (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    farm_id uuid not null,
    code bigint generated always as identity,
    name varchar(120) not null,
    crop varchar(80) not null,
    variety varchar(100) null,
    start_date date not null,
    end_date date not null,
    status smallint not null default 1,
    planned_area_ha numeric(18,4) not null,
    expected_yield_per_ha numeric(18,4) not null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz null,
    updated_by uuid null,
    deleted_at timestamptz null,
    deleted_by uuid null,
    version bigint not null default 1,
    constraint uq_seasons_tenant_id unique (tenant_id, id),
    constraint fk_seasons_farm foreign key (tenant_id, farm_id)
        references geo.farms(tenant_id, id),
    constraint ck_seasons_period check (end_date > start_date),
    constraint ck_seasons_status check (status between 1 and 5),
    constraint ck_seasons_area check (planned_area_ha > 0),
    constraint ck_seasons_yield check (expected_yield_per_ha > 0)
);

create index if not exists ix_seasons_farm_period on agriculture.seasons (tenant_id, farm_id, start_date, end_date);
create index if not exists ix_seasons_search on agriculture.seasons using gin (name gin_trgm_ops);
create index if not exists ix_seasons_crop_search on agriculture.seasons using gin (crop gin_trgm_ops);

create table if not exists inventory.products (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    code bigint generated always as identity,
    sku varchar(60) not null,
    name varchar(160) not null,
    category varchar(60) not null,
    base_unit varchar(16) not null references platform.units(code),
    requires_lot boolean not null default false,
    is_perishable boolean not null default false,
    controlled_product boolean not null default false,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz null,
    updated_by uuid null,
    deleted_at timestamptz null,
    deleted_by uuid null,
    version bigint not null default 1,
    constraint uq_products_tenant_id unique (tenant_id, id),
    constraint uq_products_tenant_sku unique (tenant_id, sku)
);

create index if not exists ix_products_name_trgm on inventory.products using gin (name gin_trgm_ops);
create index if not exists ix_products_sku_trgm on inventory.products using gin (sku gin_trgm_ops);

create table if not exists inventory.warehouses (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    farm_id uuid not null,
    code varchar(40) not null,
    name varchar(160) not null,
    type varchar(40) not null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz null,
    updated_by uuid null,
    deleted_at timestamptz null,
    deleted_by uuid null,
    version bigint not null default 1,
    constraint uq_warehouses_tenant_id unique (tenant_id, id),
    constraint uq_warehouses_tenant_code unique (tenant_id, code),
    constraint fk_warehouses_farm foreign key (tenant_id, farm_id)
        references geo.farms(tenant_id, id),
    constraint ck_warehouses_type check (type in ('INPUTS', 'GRAINS', 'MEDICINES', 'FUEL', 'PARTS', 'FEED', 'GENERAL'))
);

create table if not exists inventory.stock_balances (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    warehouse_id uuid not null,
    product_id uuid not null,
    unit varchar(16) not null references platform.units(code),
    available numeric(20,6) not null default 0,
    reserved numeric(20,6) not null default 0,
    minimum numeric(20,6) not null default 0,
    average_cost numeric(18,4) not null default 0,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    version bigint not null default 0,
    constraint uq_stock_balances_tenant_id unique (tenant_id, id),
    constraint uq_stock_balances_product unique (tenant_id, warehouse_id, product_id),
    constraint fk_stock_balances_warehouse foreign key (tenant_id, warehouse_id)
        references inventory.warehouses(tenant_id, id),
    constraint fk_stock_balances_product foreign key (tenant_id, product_id)
        references inventory.products(tenant_id, id),
    constraint ck_stock_balances_available check (available >= 0),
    constraint ck_stock_balances_reserved check (reserved >= 0 and reserved <= available),
    constraint ck_stock_balances_minimum check (minimum >= 0),
    constraint ck_stock_balances_cost check (average_cost >= 0)
);

create table if not exists inventory.stock_movements (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    warehouse_id uuid not null,
    product_id uuid not null,
    movement_type varchar(30) not null,
    quantity numeric(20,6) not null,
    unit varchar(16) not null references platform.units(code),
    unit_cost numeric(18,4) not null default 0,
    total_cost numeric(18,4) not null default 0,
    lot_number varchar(100) null,
    expires_on date null,
    reference_type varchar(60) not null,
    reference_id uuid null,
    notes varchar(1000) null,
    idempotency_key varchar(160) null,
    balance_after numeric(20,6) not null,
    average_cost_after numeric(18,4) not null,
    balance_version bigint not null,
    occurred_at timestamptz not null,
    created_by uuid not null,
    constraint uq_stock_movements_tenant_id unique (tenant_id, id),
    constraint fk_stock_movements_warehouse foreign key (tenant_id, warehouse_id)
        references inventory.warehouses(tenant_id, id),
    constraint fk_stock_movements_product foreign key (tenant_id, product_id)
        references inventory.products(tenant_id, id),
    constraint ck_stock_movements_type check (movement_type in ('RECEIPT', 'CONSUMPTION', 'TRANSFER_IN', 'TRANSFER_OUT', 'ADJUSTMENT_IN', 'ADJUSTMENT_OUT', 'PRODUCTION', 'SALE')),
    constraint ck_stock_movements_quantity check (quantity > 0),
    constraint ck_stock_movements_cost check (unit_cost >= 0 and total_cost >= 0),
    constraint ck_stock_movements_balance check (balance_after >= 0)
);

create unique index if not exists ux_stock_movements_idempotency
    on inventory.stock_movements (tenant_id, idempotency_key)
    where idempotency_key is not null;
create index if not exists ix_stock_movements_product_date
    on inventory.stock_movements (tenant_id, product_id, occurred_at desc);

create table if not exists agriculture.field_operations (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    farm_id uuid not null,
    field_id uuid not null,
    season_id uuid not null,
    operation_type varchar(40) not null,
    status varchar(24) not null,
    area_ha numeric(18,4) null,
    quantity numeric(20,6) null,
    unit varchar(16) null references platform.units(code),
    equipment_id uuid null,
    gps geography(Point, 4326) null,
    executed_at timestamptz not null,
    notes varchar(2000) null,
    idempotency_key varchar(160) null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz null,
    updated_by uuid null,
    version bigint not null default 1,
    constraint uq_field_operations_tenant_id unique (tenant_id, id),
    constraint fk_field_operations_farm foreign key (tenant_id, farm_id)
        references geo.farms(tenant_id, id),
    constraint fk_field_operations_field foreign key (tenant_id, field_id)
        references geo.fields(tenant_id, id),
    constraint fk_field_operations_season foreign key (tenant_id, season_id)
        references agriculture.seasons(tenant_id, id),
    constraint ck_field_operations_type check (operation_type in ('PLANTING', 'FERTILIZATION', 'SPRAYING', 'IRRIGATION', 'MONITORING', 'HARVEST', 'OCCURRENCE')),
    constraint ck_field_operations_status check (status in ('PLANNED', 'RELEASED', 'IN_PROGRESS', 'PAUSED', 'COMPLETED', 'CANCELLED')),
    constraint ck_field_operations_area check (area_ha is null or area_ha > 0),
    constraint ck_field_operations_quantity check (quantity is null or quantity > 0)
);

create unique index if not exists ux_field_operations_idempotency
    on agriculture.field_operations (tenant_id, idempotency_key)
    where idempotency_key is not null;
create index if not exists ix_field_operations_season_date
    on agriculture.field_operations (tenant_id, season_id, executed_at desc);

create table if not exists livestock.herds (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    farm_id uuid not null,
    name varchar(120) not null,
    purpose varchar(40) not null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    deleted_at timestamptz null,
    version bigint not null default 1,
    constraint uq_herds_tenant_id unique (tenant_id, id),
    constraint fk_herds_farm foreign key (tenant_id, farm_id)
        references geo.farms(tenant_id, id),
    constraint uq_herds_farm_name unique (tenant_id, farm_id, name)
);

create table if not exists livestock.animals (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    farm_id uuid not null,
    herd_id uuid null,
    code bigint generated always as identity,
    tag varchar(80) not null,
    rfid varchar(120) null,
    sisbov varchar(120) null,
    species varchar(60) not null,
    breed varchar(80) not null,
    sex varchar(20) not null,
    birth_date date not null,
    mother_id uuid null,
    father_id uuid null,
    status smallint not null default 1,
    current_weight_kg numeric(12,4) null,
    last_weight_date date null,
    withdrawal_until date null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz null,
    updated_by uuid null,
    deleted_at timestamptz null,
    deleted_by uuid null,
    version bigint not null default 1,
    constraint uq_animals_tenant_id unique (tenant_id, id),
    constraint uq_animals_tenant_tag unique (tenant_id, tag),
    constraint fk_animals_farm foreign key (tenant_id, farm_id)
        references geo.farms(tenant_id, id),
    constraint fk_animals_herd foreign key (tenant_id, herd_id)
        references livestock.herds(tenant_id, id),
    constraint fk_animals_mother foreign key (tenant_id, mother_id)
        references livestock.animals(tenant_id, id),
    constraint fk_animals_father foreign key (tenant_id, father_id)
        references livestock.animals(tenant_id, id),
    constraint ck_animals_status check (status between 1 and 5),
    constraint ck_animals_weight check (current_weight_kg is null or current_weight_kg > 0)
);

create unique index if not exists ux_animals_tenant_rfid
    on livestock.animals (tenant_id, rfid) where rfid is not null and deleted_at is null;
create index if not exists ix_animals_tag_trgm on livestock.animals using gin (tag gin_trgm_ops);
create index if not exists ix_animals_rfid_trgm on livestock.animals using gin (rfid gin_trgm_ops);

create table if not exists livestock.animal_events (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    animal_id uuid not null,
    event_type varchar(40) not null,
    occurred_on date not null,
    data jsonb not null default '{}'::jsonb,
    cost_amount numeric(18,4) not null default 0,
    idempotency_key varchar(160) null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    constraint uq_animal_events_tenant_id unique (tenant_id, id),
    constraint fk_animal_events_animal foreign key (tenant_id, animal_id)
        references livestock.animals(tenant_id, id),
    constraint ck_animal_events_cost check (cost_amount >= 0)
);

create unique index if not exists ux_animal_events_idempotency
    on livestock.animal_events (tenant_id, idempotency_key)
    where idempotency_key is not null;
create index if not exists ix_animal_events_timeline
    on livestock.animal_events (tenant_id, animal_id, occurred_on desc, created_at desc);

create table if not exists cost.entries (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    farm_id uuid not null,
    season_id uuid null,
    field_id uuid null,
    animal_id uuid null,
    herd_id uuid null,
    asset_id uuid null,
    source_type varchar(60) not null,
    source_id uuid not null,
    category varchar(60) not null,
    amount numeric(18,4) not null,
    currency char(3) not null default 'BRL',
    occurred_on date not null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    constraint uq_cost_entries_tenant_id unique (tenant_id, id),
    constraint fk_cost_entries_farm foreign key (tenant_id, farm_id)
        references geo.farms(tenant_id, id),
    constraint fk_cost_entries_season foreign key (tenant_id, season_id)
        references agriculture.seasons(tenant_id, id),
    constraint fk_cost_entries_field foreign key (tenant_id, field_id)
        references geo.fields(tenant_id, id),
    constraint fk_cost_entries_animal foreign key (tenant_id, animal_id)
        references livestock.animals(tenant_id, id),
    constraint fk_cost_entries_herd foreign key (tenant_id, herd_id)
        references livestock.herds(tenant_id, id),
    constraint ck_cost_entries_amount check (amount >= 0)
);

create index if not exists ix_cost_entries_allocation
    on cost.entries (tenant_id, farm_id, season_id, field_id, animal_id, occurred_on);
create unique index if not exists ux_cost_entries_source
    on cost.entries (tenant_id, source_type, source_id, category);

create table if not exists commercial.sales (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    farm_id uuid not null,
    code bigint generated always as identity,
    product_type varchar(40) not null,
    origin_id uuid not null,
    warehouse_id uuid null,
    quantity numeric(20,6) not null,
    unit varchar(16) not null references platform.units(code),
    unit_price numeric(18,4) not null,
    total_amount numeric(18,4) not null,
    currency char(3) not null default 'BRL',
    buyer_name varchar(160) not null,
    buyer_document varchar(30) null,
    due_date date not null,
    status varchar(24) not null,
    idempotency_key varchar(160) null,
    confirmed_at timestamptz null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz null,
    updated_by uuid null,
    cancelled_at timestamptz null,
    version bigint not null default 1,
    constraint uq_sales_tenant_id unique (tenant_id, id),
    constraint fk_sales_farm foreign key (tenant_id, farm_id)
        references geo.farms(tenant_id, id),
    constraint fk_sales_warehouse foreign key (tenant_id, warehouse_id)
        references inventory.warehouses(tenant_id, id),
    constraint ck_sales_product_type check (product_type in ('CROP', 'ANIMAL', 'MILK', 'WOOD', 'FRUIT', 'COMMODITY', 'BYPRODUCT')),
    constraint ck_sales_quantity check (quantity > 0),
    constraint ck_sales_amount check (unit_price >= 0 and total_amount >= 0),
    constraint ck_sales_status check (status in ('DRAFT', 'CONFIRMED', 'FULFILLED', 'CANCELLED'))
);

create unique index if not exists ux_sales_idempotency
    on commercial.sales (tenant_id, idempotency_key)
    where idempotency_key is not null;

create table if not exists finance.receivables (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    farm_id uuid not null,
    sale_id uuid null,
    code bigint generated always as identity,
    description varchar(300) not null,
    amount numeric(18,4) not null,
    paid_amount numeric(18,4) not null default 0,
    currency char(3) not null default 'BRL',
    due_date date not null,
    status varchar(24) not null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz null,
    updated_by uuid null,
    version bigint not null default 1,
    constraint uq_receivables_tenant_id unique (tenant_id, id),
    constraint fk_receivables_farm foreign key (tenant_id, farm_id)
        references geo.farms(tenant_id, id),
    constraint fk_receivables_sale foreign key (tenant_id, sale_id)
        references commercial.sales(tenant_id, id),
    constraint ck_receivables_amount check (amount >= 0 and paid_amount >= 0 and paid_amount <= amount),
    constraint ck_receivables_status check (status in ('OPEN', 'PARTIAL', 'PAID', 'OVERDUE', 'CANCELLED'))
);

create index if not exists ix_receivables_due
    on finance.receivables (tenant_id, status, due_date);

create table if not exists traceability.nodes (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    entity_type varchar(60) not null,
    entity_id uuid not null,
    label varchar(240) not null,
    public_data jsonb null,
    created_at timestamptz not null default now(),
    constraint uq_traceability_nodes_tenant_id unique (tenant_id, id),
    constraint uq_traceability_nodes_entity unique (tenant_id, entity_type, entity_id)
);

create table if not exists traceability.edges (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    from_node_id uuid not null,
    to_node_id uuid not null,
    relation_type varchar(60) not null,
    attributes jsonb null,
    created_at timestamptz not null default now(),
    constraint uq_traceability_edges_tenant_id unique (tenant_id, id),
    constraint uq_traceability_edges_relation unique (tenant_id, from_node_id, to_node_id, relation_type),
    constraint fk_traceability_edges_from foreign key (tenant_id, from_node_id)
        references traceability.nodes(tenant_id, id),
    constraint fk_traceability_edges_to foreign key (tenant_id, to_node_id)
        references traceability.nodes(tenant_id, id),
    constraint ck_traceability_no_self_edge check (from_node_id <> to_node_id)
);

create index if not exists ix_traceability_edges_from on traceability.edges (tenant_id, from_node_id);
create index if not exists ix_traceability_edges_to on traceability.edges (tenant_id, to_node_id);

create table if not exists platform.outbox_messages (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    event_type varchar(180) not null,
    aggregate_id uuid not null,
    payload jsonb not null,
    occurred_at timestamptz not null,
    processed_at timestamptz null,
    attempts integer not null default 0,
    next_attempt_at timestamptz not null default now(),
    last_error varchar(2000) null,
    constraint uq_outbox_tenant_id unique (tenant_id, id),
    constraint ck_outbox_attempts check (attempts >= 0)
);

create index if not exists ix_outbox_pending
    on platform.outbox_messages (tenant_id, next_attempt_at, occurred_at)
    where processed_at is null;

create table if not exists notification.alerts (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    farm_id uuid null,
    type varchar(60) not null,
    severity varchar(20) not null,
    title varchar(180) not null,
    message varchar(1000) not null,
    entity_type varchar(60) null,
    entity_id uuid null,
    status varchar(20) not null default 'OPEN',
    created_at timestamptz not null default now(),
    acknowledged_at timestamptz null,
    acknowledged_by uuid null,
    constraint uq_alerts_tenant_id unique (tenant_id, id),
    constraint fk_alerts_farm foreign key (tenant_id, farm_id)
        references geo.farms(tenant_id, id),
    constraint ck_alerts_severity check (severity in ('LOW', 'MEDIUM', 'HIGH', 'CRITICAL')),
    constraint ck_alerts_status check (status in ('OPEN', 'ACKNOWLEDGED', 'RESOLVED', 'DISMISSED'))
);

create index if not exists ix_alerts_open on notification.alerts (tenant_id, severity, created_at desc) where status = 'OPEN';

create table if not exists documents.files (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    farm_id uuid null,
    entity_type varchar(60) null,
    entity_id uuid null,
    category varchar(60) not null,
    name varchar(240) not null,
    storage_key varchar(500) not null,
    content_type varchar(120) not null,
    size_bytes bigint not null,
    version_number integer not null default 1,
    expires_on date null,
    ocr_status varchar(30) not null default 'NOT_REQUESTED',
    checksum_sha256 char(64) not null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    deleted_at timestamptz null,
    deleted_by uuid null,
    constraint uq_files_tenant_id unique (tenant_id, id),
    constraint fk_files_farm foreign key (tenant_id, farm_id)
        references geo.farms(tenant_id, id),
    constraint ck_files_size check (size_bytes > 0),
    constraint ck_files_version check (version_number > 0)
);

create table if not exists fleet.assets (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    farm_id uuid not null,
    code varchar(60) not null,
    name varchar(160) not null,
    asset_type varchar(60) not null,
    manufacturer varchar(100) null,
    model varchar(100) null,
    serial_number varchar(120) null,
    current_hour_meter numeric(14,2) not null default 0,
    status varchar(24) not null default 'AVAILABLE',
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz null,
    version bigint not null default 1,
    constraint uq_assets_tenant_id unique (tenant_id, id),
    constraint uq_assets_tenant_code unique (tenant_id, code),
    constraint fk_assets_farm foreign key (tenant_id, farm_id)
        references geo.farms(tenant_id, id),
    constraint ck_assets_hour_meter check (current_hour_meter >= 0)
);

create table if not exists purchasing.requests (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    farm_id uuid not null,
    requester_id uuid not null,
    status varchar(30) not null default 'DRAFT',
    total_estimated numeric(18,4) not null default 0,
    currency char(3) not null default 'BRL',
    justification varchar(1000) not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz null,
    version bigint not null default 1,
    constraint uq_purchase_requests_tenant_id unique (tenant_id, id),
    constraint fk_purchase_requests_farm foreign key (tenant_id, farm_id)
        references geo.farms(tenant_id, id),
    constraint fk_purchase_requests_user foreign key (tenant_id, requester_id)
        references identity.users(tenant_id, id),
    constraint ck_purchase_requests_total check (total_estimated >= 0)
);

create table if not exists logistics.shipments (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    farm_id uuid not null,
    origin varchar(240) not null,
    destination varchar(240) not null,
    cargo_type varchar(60) not null,
    quantity numeric(20,6) not null,
    unit varchar(16) not null references platform.units(code),
    status varchar(30) not null default 'WAITING',
    scheduled_at timestamptz null,
    delivered_at timestamptz null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    version bigint not null default 1,
    constraint uq_shipments_tenant_id unique (tenant_id, id),
    constraint fk_shipments_farm foreign key (tenant_id, farm_id)
        references geo.farms(tenant_id, id),
    constraint ck_shipments_quantity check (quantity > 0),
    constraint ck_shipments_status check (status in ('WAITING', 'LOADING', 'IN_TRANSIT', 'DELIVERED', 'DELAYED', 'BLOCKED', 'CANCELLED'))
);

create table if not exists environment.compliance_items (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    farm_id uuid not null,
    type varchar(60) not null,
    number varchar(120) null,
    status varchar(30) not null,
    issued_on date null,
    expires_on date null,
    document_id uuid null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    version bigint not null default 1,
    constraint uq_compliance_tenant_id unique (tenant_id, id),
    constraint fk_compliance_farm foreign key (tenant_id, farm_id)
        references geo.farms(tenant_id, id),
    constraint fk_compliance_document foreign key (tenant_id, document_id)
        references documents.files(tenant_id, id)
);

create table if not exists hr.workers (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    organization_id uuid not null,
    name varchar(160) not null,
    document_number varchar(30) not null,
    role_name varchar(100) not null,
    status varchar(24) not null default 'ACTIVE',
    hired_on date not null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    version bigint not null default 1,
    constraint uq_workers_tenant_id unique (tenant_id, id),
    constraint uq_workers_document unique (tenant_id, document_number),
    constraint fk_workers_organization foreign key (tenant_id, organization_id)
        references organization.organizations(tenant_id, id)
);

create table if not exists workflow.definitions (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    code varchar(80) not null,
    name varchar(160) not null,
    entity_type varchar(80) not null,
    definition jsonb not null,
    active boolean not null default true,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    version bigint not null default 1,
    constraint uq_workflow_definitions_tenant_id unique (tenant_id, id),
    constraint uq_workflow_definitions_code unique (tenant_id, code, version)
);

create table if not exists workflow.instances (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    definition_id uuid not null,
    entity_type varchar(80) not null,
    entity_id uuid not null,
    current_step varchar(80) not null,
    status varchar(24) not null,
    data jsonb not null default '{}'::jsonb,
    started_at timestamptz not null default now(),
    completed_at timestamptz null,
    version bigint not null default 1,
    constraint uq_workflow_instances_tenant_id unique (tenant_id, id),
    constraint fk_workflow_instance_definition foreign key (tenant_id, definition_id)
        references workflow.definitions(tenant_id, id)
);

create table if not exists iot.devices (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    farm_id uuid null,
    external_id varchar(160) not null,
    device_type varchar(80) not null,
    protocol varchar(30) not null,
    manufacturer varchar(100) null,
    status varchar(24) not null default 'ACTIVE',
    last_seen_at timestamptz null,
    created_at timestamptz not null default now(),
    constraint uq_devices_tenant_id unique (tenant_id, id),
    constraint uq_devices_external_id unique (tenant_id, external_id),
    constraint fk_devices_farm foreign key (tenant_id, farm_id)
        references geo.farms(tenant_id, id)
);

create table if not exists iot.telemetry_events (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    device_id uuid not null,
    metric varchar(100) not null,
    numeric_value numeric(24,8) null,
    text_value varchar(500) null,
    unit varchar(30) null,
    quality varchar(24) not null default 'VALID',
    occurred_at timestamptz not null,
    received_at timestamptz not null default now(),
    raw_payload jsonb null,
    constraint uq_telemetry_tenant_id unique (tenant_id, id),
    constraint fk_telemetry_device foreign key (tenant_id, device_id)
        references iot.devices(tenant_id, id),
    constraint ck_telemetry_value check (numeric_value is not null or text_value is not null)
);

create index if not exists ix_telemetry_device_time
    on iot.telemetry_events (tenant_id, device_id, occurred_at desc);

create table if not exists analytics.data_quality_scores (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    farm_id uuid null,
    scope_type varchar(60) not null,
    scope_id uuid not null,
    score numeric(5,2) not null,
    findings jsonb not null default '[]'::jsonb,
    calculated_at timestamptz not null,
    method_version varchar(40) not null,
    constraint uq_data_quality_tenant_id unique (tenant_id, id),
    constraint fk_data_quality_farm foreign key (tenant_id, farm_id)
        references geo.farms(tenant_id, id),
    constraint ck_data_quality_score check (score between 0 and 100)
);

create table if not exists audit.logs (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    user_id uuid null,
    action varchar(80) not null,
    entity_type varchar(120) not null,
    entity_id uuid not null,
    before_data jsonb null,
    after_data jsonb null,
    ip_address inet null,
    device varchar(300) null,
    correlation_id varchar(100) null,
    occurred_at timestamptz not null default now(),
    constraint uq_audit_logs_tenant_id unique (tenant_id, id),
    constraint fk_audit_logs_user foreign key (tenant_id, user_id)
        references identity.users(tenant_id, id)
);

create index if not exists ix_audit_logs_entity
    on audit.logs (tenant_id, entity_type, entity_id, occurred_at desc);
create index if not exists ix_audit_logs_user
    on audit.logs (tenant_id, user_id, occurred_at desc);

insert into platform.units (code, name, dimension, base_unit_code, conversion_factor) values
    ('kg', 'Quilograma', 'MASS', null, 1),
    ('g', 'Grama', 'MASS', 'kg', 0.001),
    ('t', 'Tonelada', 'MASS', 'kg', 1000),
    ('l', 'Litro', 'VOLUME', null, 1),
    ('ml', 'Mililitro', 'VOLUME', 'l', 0.001),
    ('m3', 'Metro cúbico', 'VOLUME', null, 1),
    ('ha', 'Hectare', 'AREA', null, 1),
    ('m2', 'Metro quadrado', 'AREA', 'ha', 0.0001),
    ('sc', 'Saca', 'PACKAGE', null, 1),
    ('arroba', 'Arroba', 'MASS', 'kg', 15),
    ('head', 'Cabeça', 'COUNT', null, 1),
    ('unit', 'Unidade', 'COUNT', null, 1),
    ('hour', 'Hora', 'TIME', null, 1),
    ('km', 'Quilômetro', 'DISTANCE', null, 1)
on conflict (code) do update set
    name = excluded.name,
    dimension = excluded.dimension,
    base_unit_code = excluded.base_unit_code,
    conversion_factor = excluded.conversion_factor;

insert into identity.permissions (code, module, description) values
    ('properties.read', 'Properties', 'Consultar propriedades e talhões.'),
    ('properties.write', 'Properties', 'Cadastrar e alterar propriedades e talhões.'),
    ('agriculture.read', 'Agriculture', 'Consultar safras e operações agrícolas.'),
    ('agriculture.write', 'Agriculture', 'Planejar e executar operações agrícolas.'),
    ('inventory.read', 'Inventory', 'Consultar produtos, depósitos e saldos.'),
    ('inventory.move', 'Inventory', 'Registrar entradas, saídas e transferências.'),
    ('inventory.adjust', 'Inventory', 'Realizar ajuste justificado de estoque.'),
    ('livestock.read', 'Livestock', 'Consultar rebanho e linha do tempo animal.'),
    ('livestock.write', 'Livestock', 'Registrar manejos, pesagens e sanidade.'),
    ('livestock.sell', 'Livestock', 'Autorizar venda de animais.'),
    ('commercial.write', 'Commercial', 'Criar e confirmar vendas.'),
    ('finance.read', 'Finance', 'Consultar recebíveis, custos e indicadores financeiros.'),
    ('dashboard.read', 'Analytics', 'Consultar Command Center, busca e rastreabilidade.')
on conflict (code) do update set
    module = excluded.module,
    description = excluded.description;

insert into platform.modules (code, name, phase, status, description) values
    ('platform', 'Plataforma e segurança', 1, 'CORE', 'Tenant, organização, usuário, permissão e auditoria.'),
    ('properties', 'Propriedades e geoprocessamento', 2, 'CORE', 'Fazendas, talhões e geometrias PostGIS.'),
    ('agriculture', 'Agricultura', 3, 'CORE', 'Safras, planejamento, plantio e colheita.'),
    ('inventory', 'Estoque', 4, 'CORE', 'Produtos, depósitos, lotes, custo médio e movimentações.'),
    ('fleet', 'Máquinas e frota', 5, 'FOUNDATION', 'Ativos, horímetro e fundação de telemetria.'),
    ('livestock', 'Pecuária', 6, 'CORE', 'Animais, eventos, pesagem, GMD e sanidade.'),
    ('pasture-feedlot', 'Pastagem e confinamento', 7, 'PLANNED', 'Piquetes, dieta, trato, conversão e custo por arroba.'),
    ('purchasing', 'Compras', 8, 'FOUNDATION', 'Requisição, alçada, cotação, pedido e recebimento.'),
    ('finance', 'Financeiro', 9, 'FOUNDATION', 'Recebíveis integrados e expansão para caixa, bancos e DRE.'),
    ('cost', 'Motor universal de custos', 10, 'CORE', 'Custos por fazenda, safra, talhão, animal, lote e ativo.'),
    ('commercial', 'Comercial', 11, 'CORE', 'Venda integrada a estoque, animal, recebível e rastreabilidade.'),
    ('warehousing', 'Armazenagem e balança', 12, 'PLANNED', 'Recepção, classificação, secagem, silo e expedição.'),
    ('logistics', 'Logística', 13, 'FOUNDATION', 'Carga e estados da torre de controle.'),
    ('traceability', 'AgroGraph e rastreabilidade', 14, 'CORE', 'Malha de nós e relações entre origem, operação e venda.'),
    ('mobile', 'Mobile', 15, 'FOUNDATION', 'PWA instalável e contratos do app .NET MAUI.'),
    ('offline', 'Offline-first', 16, 'FOUNDATION', 'Local Outbox, sincronização e conflitos por criticidade.'),
    ('documents', 'GED Agro', 17, 'FOUNDATION', 'Metadados, storage externo, OCR e temporalidade.'),
    ('environment-esg', 'Ambiental e ESG', 18, 'FOUNDATION', 'Itens de compliance e vencimentos.'),
    ('cooperatives', 'Cooperativas', 19, 'PLANNED', 'Cooperados, recebimento, armazenagem e relacionamento.'),
    ('distributors', 'Revendas', 20, 'PLANNED', 'CRM, vendas, estoque, crédito e assistência.'),
    ('agroindustry', 'Agroindústria', 21, 'PLANNED', 'Transformação, qualidade, lote industrial e fábrica de ração.'),
    ('verticals', 'Verticais produtivas', 22, 'PLANNED', 'Leite, aves, suínos, aquicultura e florestal.'),
    ('iot', 'IoT', 23, 'FOUNDATION', 'Dispositivos e telemetria independente de fabricante.'),
    ('analytics', 'BI e Command Center', 24, 'CORE', 'KPIs operacionais e qualidade dos dados.'),
    ('ai', 'Agro 360 Copilot', 25, 'PLANNED', 'IA contextual, autorizada e explicável.'),
    ('predictive-ai', 'IA preditiva', 26, 'PLANNED', 'Previsões, anomalias e cenários.')
on conflict (code) do update set
    name = excluded.name,
    phase = excluded.phase,
    status = excluded.status,
    description = excluded.description;

create or replace function platform.current_tenant_id()
returns uuid
language sql
stable
as $$
    select nullif(current_setting('app.tenant_id', true), '')::uuid
$$;

create or replace function platform.enable_tenant_rls(target_table regclass)
returns void
language plpgsql
as $$
begin
    execute format('alter table %s enable row level security', target_table);
    execute format('alter table %s force row level security', target_table);
    execute format(
        'create policy tenant_isolation on %s using (tenant_id = platform.current_tenant_id()) with check (tenant_id = platform.current_tenant_id())',
        target_table);
end;
$$;

select platform.enable_tenant_rls('organization.organizations');
select platform.enable_tenant_rls('identity.users');
select platform.enable_tenant_rls('identity.roles');
select platform.enable_tenant_rls('identity.user_roles');
select platform.enable_tenant_rls('identity.role_permissions');
select platform.enable_tenant_rls('identity.refresh_tokens');
select platform.enable_tenant_rls('geo.farms');
select platform.enable_tenant_rls('geo.fields');
select platform.enable_tenant_rls('agriculture.seasons');
select platform.enable_tenant_rls('agriculture.field_operations');
select platform.enable_tenant_rls('inventory.products');
select platform.enable_tenant_rls('inventory.warehouses');
select platform.enable_tenant_rls('inventory.stock_balances');
select platform.enable_tenant_rls('inventory.stock_movements');
select platform.enable_tenant_rls('livestock.herds');
select platform.enable_tenant_rls('livestock.animals');
select platform.enable_tenant_rls('livestock.animal_events');
select platform.enable_tenant_rls('cost.entries');
select platform.enable_tenant_rls('commercial.sales');
select platform.enable_tenant_rls('finance.receivables');
select platform.enable_tenant_rls('traceability.nodes');
select platform.enable_tenant_rls('traceability.edges');
select platform.enable_tenant_rls('platform.outbox_messages');
select platform.enable_tenant_rls('notification.alerts');
select platform.enable_tenant_rls('documents.files');
select platform.enable_tenant_rls('fleet.assets');
select platform.enable_tenant_rls('purchasing.requests');
select platform.enable_tenant_rls('logistics.shipments');
select platform.enable_tenant_rls('environment.compliance_items');
select platform.enable_tenant_rls('hr.workers');
select platform.enable_tenant_rls('workflow.definitions');
select platform.enable_tenant_rls('workflow.instances');
select platform.enable_tenant_rls('iot.devices');
select platform.enable_tenant_rls('iot.telemetry_events');
select platform.enable_tenant_rls('analytics.data_quality_scores');
select platform.enable_tenant_rls('audit.logs');

drop function platform.enable_tenant_rls(regclass);
create or replace view analytics.season_profitability
with (security_invoker = true)
as
select
    s.tenant_id,
    s.id as season_id,
    s.farm_id,
    s.name as season_name,
    s.crop,
    s.planned_area_ha,
    coalesce(c.total_cost, 0) as total_cost,
    coalesce(v.total_revenue, 0) as total_revenue,
    coalesce(v.total_revenue, 0) - coalesce(c.total_cost, 0) as margin,
    case when s.planned_area_ha > 0 then coalesce(c.total_cost, 0) / s.planned_area_ha else 0 end as cost_per_ha
from agriculture.seasons s
left join lateral (
    select sum(e.amount) as total_cost
    from cost.entries e
    where e.tenant_id = s.tenant_id and e.season_id = s.id
) c on true
left join lateral (
    select sum(sa.total_amount) as total_revenue
    from commercial.sales sa
    where sa.tenant_id = s.tenant_id
      and sa.origin_id = s.id
      and sa.status in ('CONFIRMED', 'FULFILLED')
) v on true
where s.deleted_at is null;

create or replace view analytics.inventory_position
with (security_invoker = true)
as
select
    b.tenant_id,
    w.farm_id,
    b.warehouse_id,
    b.product_id,
    p.sku,
    p.name as product_name,
    p.category,
    b.unit,
    b.available,
    b.reserved,
    b.available - b.reserved as free_quantity,
    b.average_cost,
    b.available * b.average_cost as inventory_value,
    b.available < b.minimum as below_minimum,
    b.version
from inventory.stock_balances b
join inventory.products p on p.id = b.product_id and p.tenant_id = b.tenant_id
join inventory.warehouses w on w.id = b.warehouse_id and w.tenant_id = b.tenant_id
where p.deleted_at is null and w.deleted_at is null;

create or replace view analytics.animal_performance
with (security_invoker = true)
as
select
    a.tenant_id,
    a.farm_id,
    a.id as animal_id,
    a.tag,
    a.species,
    a.breed,
    a.current_weight_kg,
    a.last_weight_date,
    previous.previous_weight,
    previous.previous_date,
    case
        when previous.previous_date is not null and a.last_weight_date > previous.previous_date
        then round((a.current_weight_kg - previous.previous_weight) / (a.last_weight_date - previous.previous_date), 4)
        else null
    end as daily_gain_kg,
    coalesce(costs.total_cost, 0) as accumulated_cost
from livestock.animals a
left join lateral (
    select
        cast(e.data->>'weightKg' as numeric) as previous_weight,
        e.occurred_on as previous_date
    from livestock.animal_events e
    where e.tenant_id = a.tenant_id
      and e.animal_id = a.id
      and e.event_type = 'WEIGHING'
      and e.occurred_on < a.last_weight_date
    order by e.occurred_on desc, e.created_at desc
    limit 1
) previous on true
left join lateral (
    select sum(c.amount) as total_cost
    from cost.entries c
    where c.tenant_id = a.tenant_id and c.animal_id = a.id
) costs on true
where a.deleted_at is null;

comment on view analytics.season_profitability is 'Margem de safra derivada de custos e vendas autorizados pelo RLS.';
comment on view analytics.inventory_position is 'Posição de estoque com disponibilidade, custo médio e alerta mínimo.';
comment on view analytics.animal_performance is 'Peso, GMD e custo acumulado por animal.';
alter table platform.outbox_messages
    add column if not exists correlation_id varchar(100) null,
    add column if not exists last_attempt_at timestamptz null,
    add column if not exists dead_lettered_at timestamptz null;

drop index if exists platform.ix_outbox_pending;
create index ix_outbox_pending
    on platform.outbox_messages (tenant_id, next_attempt_at, occurred_at)
    where processed_at is null and dead_lettered_at is null;

create index if not exists ix_outbox_dead_letter
    on platform.outbox_messages (tenant_id, dead_lettered_at desc)
    where dead_lettered_at is not null;

comment on column platform.outbox_messages.correlation_id is
    'Identificador técnico de correlação; o payload não deve ser escrito em logs.';
comment on column platform.outbox_messages.dead_lettered_at is
    'Marca falha permanente após o limite configurado de tentativas.';

update platform.modules
set status = 'FOUNDATION',
    description = case code
        when 'agriculture' then 'Backend transacional de safras, plantio e colheita; interface operacional completa permanece pendente.'
        when 'inventory' then 'Backend de produtos, depósitos, saldos e movimentos; interface operacional completa permanece pendente.'
        when 'livestock' then 'Backend de animais, pesagens e sanidade; interface operacional completa permanece pendente.'
        when 'cost' then 'Apropriação transacional inicial; consultas e interface operacional completas permanecem pendentes.'
        when 'commercial' then 'Backend de venda integrada; interface operacional e ciclo posterior permanecem pendentes.'
        when 'traceability' then 'Grafo e consulta pela API; navegação operacional completa permanece pendente.'
        else description
    end
where code in ('agriculture', 'inventory', 'livestock', 'cost', 'commercial', 'traceability');
-- Sprint 6: supply, purchasing, generalized stock, fleet, maintenance and fuel.
create table if not exists platform.schema_versions(version varchar(30) primary key, description varchar(240) not null, installed_at timestamptz not null default now());
insert into platform.schema_versions values ('0.3.0','Sprint 6 operational modules',now()) on conflict(version) do nothing;

alter table inventory.products add column if not exists minimum_stock numeric(20,6) not null default 0;
alter table fleet.assets alter column farm_id drop not null;
alter table fleet.assets alter column code drop not null;
alter table fleet.assets alter column name drop not null;
alter table fleet.assets alter column asset_type drop not null;
alter table fleet.assets add column if not exists type varchar(40);
alter table fleet.assets add column if not exists brand varchar(100);
alter table fleet.assets add column if not exists year integer;
alter table fleet.assets add column if not exists identification varchar(80);
alter table fleet.assets add column if not exists hour_meter numeric(14,2);
alter table fleet.assets add column if not exists odometer numeric(14,2);
alter table fleet.assets add column if not exists responsible_id uuid;
alter table fleet.assets add column if not exists updated_by uuid;
alter table fleet.assets add column if not exists deleted_at timestamptz;
create unique index if not exists ux_assets_identification on fleet.assets(tenant_id,identification) where deleted_at is null and identification is not null;

create table if not exists purchasing.suppliers(id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), name varchar(160) not null, type varchar(40) not null, document_number varchar(30) not null, state_registration varchar(30), email varchar(254), phone varchar(40), address text, categories text[] not null default '{}', status varchar(20) not null default 'ACTIVE', notes text, created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz, updated_by uuid, deleted_at timestamptz, deleted_by uuid, version bigint not null default 1, unique(tenant_id,id), check(status in('ACTIVE','INACTIVE','BLOCKED')));
create unique index if not exists ux_suppliers_document on purchasing.suppliers(tenant_id,document_number) where deleted_at is null;
create index if not exists ix_suppliers_search on purchasing.suppliers using gin(name gin_trgm_ops);
create table if not exists purchasing.purchase_orders(id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), supplier_id uuid not null, farm_id uuid, cost_center varchar(100), season_id uuid, status varchar(30) not null default 'DRAFT', discount numeric(18,4) not null default 0, freight numeric(18,4) not null default 0, taxes numeric(18,4) not null default 0, total numeric(18,4) not null, received_at timestamptz, created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz, updated_by uuid, unique(tenant_id,id), foreign key(tenant_id,supplier_id) references purchasing.suppliers(tenant_id,id), check(total>=0));
create index if not exists ix_purchase_status on purchasing.purchase_orders(tenant_id,status,created_at desc);
create table if not exists purchasing.purchase_items(id uuid primary key, tenant_id uuid not null, purchase_id uuid not null, product_id uuid not null, description varchar(240) not null, quantity numeric(20,6) not null, received_quantity numeric(20,6) not null default 0, unit_price numeric(18,4) not null, foreign key(tenant_id,purchase_id) references purchasing.purchase_orders(tenant_id,id), foreign key(tenant_id,product_id) references inventory.products(tenant_id,id), check(quantity>0 and received_quantity between 0 and quantity and unit_price>=0));
create table if not exists purchasing.purchase_status_history(id uuid primary key,tenant_id uuid not null,purchase_id uuid not null,from_status varchar(30) not null,to_status varchar(30) not null,changed_at timestamptz not null default now(),changed_by uuid not null,foreign key(tenant_id,purchase_id) references purchasing.purchase_orders(tenant_id,id));

create table if not exists inventory.stock_lots(id uuid primary key,tenant_id uuid not null,warehouse_id uuid not null,product_id uuid not null,lot_number varchar(100) not null,expires_on date,quantity numeric(20,6) not null default 0,unique(tenant_id,warehouse_id,product_id,lot_number),foreign key(tenant_id,warehouse_id) references inventory.warehouses(tenant_id,id),foreign key(tenant_id,product_id) references inventory.products(tenant_id,id));
create or replace function inventory.apply_stock_movement(p_tenant uuid,p_warehouse uuid,p_product uuid,p_quantity numeric,p_cost numeric,p_type varchar,p_reference uuid,p_lot varchar,p_expires date,p_user uuid,p_reason text default null) returns uuid language plpgsql as $$
declare b inventory.stock_balances%rowtype; movement uuid:=gen_random_uuid(); baseunit varchar(16); newbalance numeric; newcost numeric;
begin if p_quantity=0 or p_cost<0 then raise exception 'invalid stock movement'; end if; select base_unit into baseunit from inventory.products where tenant_id=p_tenant and id=p_product; if baseunit is null then raise exception 'product not found'; end if;
 select * into b from inventory.stock_balances where tenant_id=p_tenant and warehouse_id=p_warehouse and product_id=p_product for update;
 if not found then if p_quantity<0 then raise exception 'insufficient stock'; end if; insert into inventory.stock_balances(id,tenant_id,warehouse_id,product_id,unit,available,minimum,average_cost,version) select gen_random_uuid(),p_tenant,p_warehouse,p_product,baseunit,p_quantity,minimum_stock,p_cost,1 from inventory.products where id=p_product returning * into b; newbalance:=p_quantity;newcost:=p_cost;
 else newbalance:=b.available+p_quantity;if newbalance<0 then raise exception 'insufficient stock';end if;newcost:=case when p_quantity>0 then ((b.available*b.average_cost)+(p_quantity*p_cost))/nullif(newbalance,0) else b.average_cost end;update inventory.stock_balances set available=newbalance,average_cost=newcost,updated_at=now(),version=version+1 where id=b.id;end if;
 insert into inventory.stock_movements(id,tenant_id,warehouse_id,product_id,movement_type,quantity,unit,unit_cost,total_cost,lot_number,expires_on,reference_type,reference_id,notes,balance_after,average_cost_after,balance_version,occurred_at,created_by) values(movement,p_tenant,p_warehouse,p_product,case when p_type in('ENTRY','PURCHASE_RECEIPT') then 'RECEIPT' when p_type in('EXIT','MAINTENANCE','FUEL') then 'CONSUMPTION' when p_type='ADJUST' and p_quantity>0 then 'ADJUSTMENT_IN' when p_type='ADJUST' then 'ADJUSTMENT_OUT' else p_type end,abs(p_quantity),baseunit,p_cost,abs(p_quantity)*p_cost,p_lot,p_expires,p_type,p_reference,p_reason,newbalance,newcost,coalesce(b.version,0)+1,now(),p_user);
 if p_lot is not null then insert into inventory.stock_lots values(gen_random_uuid(),p_tenant,p_warehouse,p_product,p_lot,p_expires,p_quantity) on conflict(tenant_id,warehouse_id,product_id,lot_number) do update set quantity=inventory.stock_lots.quantity+excluded.quantity,expires_on=coalesce(excluded.expires_on,inventory.stock_lots.expires_on);end if;return movement;end $$;

create table if not exists fleet.maintenance_orders(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),asset_id uuid not null,type varchar(30) not null,description varchar(500) not null,supplier_id uuid,responsible_id uuid,status varchar(24) not null,scheduled_for date,next_review_date date,next_hour_meter numeric(14,2),next_odometer numeric(14,2),parts_cost numeric(18,4) not null,labor_cost numeric(18,4) not null,total_cost numeric(18,4) not null,completed_at timestamptz,completion_notes text,created_at timestamptz not null default now(),created_by uuid not null,updated_at timestamptz,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,asset_id) references fleet.assets(tenant_id,id));
create table if not exists fleet.maintenance_parts(id uuid primary key,tenant_id uuid not null,maintenance_id uuid not null,product_id uuid not null,warehouse_id uuid not null,quantity numeric(20,6) not null,unit_cost numeric(18,4) not null,foreign key(tenant_id,maintenance_id) references fleet.maintenance_orders(tenant_id,id));
create table if not exists fleet.fuel_fillups(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),asset_id uuid not null,fuel_product_id uuid not null,warehouse_id uuid not null,quantity numeric(20,6) not null,unit_price numeric(18,4) not null,total numeric(18,4) not null,hour_meter numeric(14,2),odometer numeric(14,2),responsible_id uuid,location varchar(200),created_at timestamptz not null default now(),created_by uuid not null,foreign key(tenant_id,asset_id) references fleet.assets(tenant_id,id),check(quantity>0 and unit_price>=0));
create index if not exists ix_maintenance_due on fleet.maintenance_orders(tenant_id,scheduled_for) where status<>'COMPLETED';
create index if not exists ix_fillups_month on fleet.fuel_fillups(tenant_id,created_at desc);
create or replace view analytics.operational_inventory_alerts as select b.tenant_id,b.product_id,b.warehouse_id,b.available,b.minimum,l.expires_on,(b.available<b.minimum) low_stock,(l.expires_on<=current_date+30 and l.quantity>0) expiring from inventory.stock_balances b left join inventory.stock_lots l on l.tenant_id=b.tenant_id and l.product_id=b.product_id and l.warehouse_id=b.warehouse_id;

create or replace function platform.enable_tenant_rls(target_table regclass) returns void language plpgsql as $$ begin execute format('alter table %s enable row level security',target_table); execute format('alter table %s force row level security',target_table); execute format('create policy tenant_isolation on %s using (tenant_id=platform.current_tenant_id()) with check (tenant_id=platform.current_tenant_id())',target_table); end $$;
select platform.enable_tenant_rls('purchasing.suppliers');select platform.enable_tenant_rls('purchasing.purchase_orders');select platform.enable_tenant_rls('purchasing.purchase_items');select platform.enable_tenant_rls('purchasing.purchase_status_history');select platform.enable_tenant_rls('inventory.stock_lots');select platform.enable_tenant_rls('fleet.maintenance_orders');select platform.enable_tenant_rls('fleet.maintenance_parts');select platform.enable_tenant_rls('fleet.fuel_fillups');

drop function platform.enable_tenant_rls(regclass);

create or replace function platform.set_updated_at() returns trigger language plpgsql as $$ begin new.updated_at=now(); return new; end $$;
drop trigger if exists suppliers_set_updated_at on purchasing.suppliers;
create trigger suppliers_set_updated_at before update on purchasing.suppliers for each row execute function platform.set_updated_at();
drop trigger if exists assets_set_updated_at on fleet.assets;
create trigger assets_set_updated_at before update on fleet.assets for each row execute function platform.set_updated_at();
-- Dados técnicos mínimos já são mantidos idempotentemente pela migration histórica 001.
-- Este seed permanece separado e seguro para reexecução; nunca cria usuários ou dados de demonstração.
SELECT 1;
-- Sprint 7 / schema 0.4.0: Pecuária 360 (idempotent migration)
create schema if not exists livestock;
alter table livestock.animals add column if not exists category varchar(60);
alter table livestock.animals add column if not exists paddock_id uuid;
alter table livestock.animals drop constraint if exists ck_animals_status;
alter table livestock.animals add constraint ck_animals_status check(status between 1 and 6);
alter table livestock.animals drop constraint if exists ck_animals_weight;
alter table livestock.animals add constraint ck_animals_weight check(current_weight_kg is null or current_weight_kg>=0);
create unique index if not exists ux_animals_tenant_tag on livestock.animals(tenant_id,lower(tag)) where deleted_at is null;
create unique index if not exists ux_animals_tenant_rfid on livestock.animals(tenant_id,lower(rfid)) where rfid is not null and deleted_at is null;

create table if not exists livestock.herds(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),farm_id uuid not null references geo.farms(id),name varchar(120) not null,species varchar(40) not null,category varchar(60) not null,head_count integer not null default 0 check(head_count>=0),active boolean not null default true,created_at timestamptz not null default now(),created_by uuid not null,updated_at timestamptz,updated_by uuid,unique(tenant_id,name));
create table if not exists livestock.pastures(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),farm_id uuid not null references geo.farms(id),name varchar(120) not null,area_hectares numeric(14,4) not null check(area_hectares>0),forage_type varchar(100) not null,status varchar(20) not null check(status in('AVAILABLE','IN_USE','RESTING','DEGRADED','RENOVATION','INACTIVE','DISPONIVEL','EM_USO','EM_DESCANSO','DEGRADADA','EM_REFORMA','INATIVA')),created_at timestamptz not null default now(),created_by uuid not null,updated_at timestamptz,updated_by uuid,unique(tenant_id,farm_id,name));
create table if not exists livestock.paddocks(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),pasture_id uuid not null references livestock.pastures(id),name varchar(120) not null,area_hectares numeric(14,4) not null check(area_hectares>0),capacity_au numeric(14,2) not null check(capacity_au>=0),status varchar(20) not null,rest_days integer not null default 0 check(rest_days>=0),occupation_days integer not null default 0 check(occupation_days>=0),entry_height_cm numeric(10,2),exit_height_cm numeric(10,2),forage_mass_kg_ha numeric(14,2),last_occupied_at timestamptz,last_released_at timestamptz,created_at timestamptz not null default now(),created_by uuid not null,updated_at timestamptz,updated_by uuid,unique(tenant_id,pasture_id,name));
do $$ begin if not exists(select 1 from pg_constraint where conname='fk_animals_paddock') then alter table livestock.animals add constraint fk_animals_paddock foreign key(paddock_id) references livestock.paddocks(id); end if; end $$;
create table if not exists livestock.paddock_movements(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),paddock_id uuid not null references livestock.paddocks(id),origin_paddock_id uuid references livestock.paddocks(id),animal_id uuid references livestock.animals(id),herd_id uuid references livestock.herds(id),movement_type varchar(10) not null check(movement_type in('ENTRY','EXIT')),occurred_at timestamptz not null,notes text,created_at timestamptz not null default now(),created_by uuid not null,check(animal_id is not null or herd_id is not null));
create table if not exists livestock.animal_movements(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),animal_id uuid not null references livestock.animals(id),from_farm_id uuid not null references geo.farms(id),to_farm_id uuid not null references geo.farms(id),from_paddock_id uuid references livestock.paddocks(id),to_paddock_id uuid references livestock.paddocks(id),moved_on date not null,notes text,created_at timestamptz not null default now(),created_by uuid not null);
create table if not exists livestock.handling_events(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),animal_id uuid references livestock.animals(id),herd_id uuid references livestock.herds(id),event_type varchar(40) not null,occurred_on date not null,responsible varchar(120) not null,notes text,weight_kg numeric(12,3) check(weight_kg>=0),body_score numeric(4,2) check(body_score between 0 and 10),paddock_id uuid references livestock.paddocks(id),estimated_cost numeric(14,4) not null default 0 check(estimated_cost>=0),created_at timestamptz not null default now(),created_by uuid not null,check(animal_id is not null or herd_id is not null));
create table if not exists livestock.health_events(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),animal_id uuid references livestock.animals(id),herd_id uuid references livestock.herds(id),event_type varchar(40) not null,occurred_on date not null,dose numeric(14,4) not null check(dose>0),unit varchar(16) not null,product_id uuid references inventory.products(id),withdrawal_until date,next_application date,technician varchar(120),diagnosis text,notes text,cost_amount numeric(14,4) not null default 0,created_at timestamptz not null default now(),created_by uuid not null,check(animal_id is not null or herd_id is not null));
create table if not exists livestock.reproduction_cycles(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),female_id uuid not null references livestock.animals(id),started_on date not null,ended_on date,expected_birth_on date,status varchar(12) not null check(status in('ACTIVE','CLOSED')),created_at timestamptz not null default now(),created_by uuid not null,updated_at timestamptz);
create unique index if not exists ux_reproduction_active_female on livestock.reproduction_cycles(tenant_id,female_id) where status='ACTIVE';
create table if not exists livestock.reproduction_events(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),female_id uuid not null references livestock.animals(id),sire_id uuid references livestock.animals(id),event_type varchar(40) not null,occurred_on date not null,genetic_lot varchar(100),positive boolean,expected_birth_on date,notes text,created_at timestamptz not null default now(),created_by uuid not null);
create table if not exists livestock.nutrition_plans(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),farm_id uuid not null references geo.farms(id),herd_id uuid references livestock.herds(id),name varchar(120) not null,starts_on date not null,ends_on date,status varchar(15) not null,created_at timestamptz not null default now(),created_by uuid not null);
create table if not exists livestock.nutrition_plan_items(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),plan_id uuid not null references livestock.nutrition_plans(id) on delete cascade,product_id uuid not null references inventory.products(id),quantity_per_day numeric(14,4) not null check(quantity_per_day>0),unit varchar(16) not null,unit_cost numeric(14,4) not null check(unit_cost>=0));
create table if not exists livestock.feedings(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),plan_id uuid not null references livestock.nutrition_plans(id),warehouse_id uuid not null references inventory.warehouses(id),supplied_on date not null,head_count integer not null check(head_count>0),total_cost numeric(14,4) not null check(total_cost>=0),cost_per_head numeric(14,4) not null check(cost_per_head>=0),notes text,created_at timestamptz not null default now(),created_by uuid not null);
create table if not exists livestock.milk_production(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),farm_id uuid not null references geo.farms(id),animal_id uuid references livestock.animals(id),herd_id uuid references livestock.herds(id),produced_on date not null,quantity_liters numeric(14,3) not null check(quantity_liters>=0),discarded_liters numeric(14,3) not null default 0 check(discarded_liters>=0 and discarded_liters<=quantity_liters),quality jsonb,destination varchar(120),notes text,withdrawal_alert boolean not null default false,created_at timestamptz not null default now(),created_by uuid not null,check(animal_id is not null or herd_id is not null));
create index if not exists ix_handling_animal_date on livestock.handling_events(tenant_id,animal_id,occurred_on desc); create index if not exists ix_health_due on livestock.health_events(tenant_id,next_application); create index if not exists ix_reproduction_due on livestock.reproduction_cycles(tenant_id,expected_birth_on) where status='ACTIVE'; create index if not exists ix_milk_period on livestock.milk_production(tenant_id,farm_id,produced_on desc); create index if not exists ix_paddock_movement on livestock.paddock_movements(tenant_id,paddock_id,occurred_at desc);
create or replace view livestock.v_paddock_occupancy as select p.tenant_id,p.id paddock_id,p.name,p.capacity_au,count(a.id) occupancy,count(a.id)>p.capacity_au overcapacity from livestock.paddocks p left join livestock.animals a on a.tenant_id=p.tenant_id and a.paddock_id=p.id and a.status in(1,2,6) group by p.tenant_id,p.id,p.name,p.capacity_au;
create or replace function livestock.set_updated_at() returns trigger language plpgsql as $$ begin new.updated_at=now(); return new; end $$;
drop trigger if exists trg_paddock_updated on livestock.paddocks; create trigger trg_paddock_updated before update on livestock.paddocks for each row execute function livestock.set_updated_at();
do $$ declare t text; begin foreach t in array array['herds','pastures','paddocks','paddock_movements','animal_movements','handling_events','health_events','reproduction_cycles','reproduction_events','nutrition_plans','nutrition_plan_items','feedings','milk_production'] loop execute format('alter table livestock.%I enable row level security',t); if not exists(select 1 from pg_policies where schemaname='livestock' and tablename=t and policyname=t||'_tenant') then execute format('create policy %I on livestock.%I using (tenant_id = nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id = nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',t||'_tenant',t); end if; end loop; end $$;
insert into platform.schema_versions(version,description,installed_at) values('0.4.0','Sprint 7 - Pecuaria 360',now()) on conflict(version) do nothing;
