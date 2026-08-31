create extension if not exists pgcrypto;
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
    boundary jsonb null,
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

create index if not exists ix_farms_boundary on geo.farms using gin (boundary);
create index if not exists ix_farms_name_trgm on geo.farms using gin (name gin_trgm_ops);

create table if not exists geo.fields (
    id uuid primary key,
    tenant_id uuid not null references tenancy.tenants(id),
    farm_id uuid not null,
    code bigint generated always as identity,
    name varchar(120) not null,
    area_ha numeric(18,4) not null,
    boundary jsonb null,
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
    gps jsonb null,
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
    ('properties', 'Propriedades e geoprocessamento', 2, 'CORE', 'Fazendas, talhões e geometrias GeoJSON.'),
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
-- Sprint 8 / schema 0.5.0: Financeiro e comercialização agro
create schema if not exists finance;
create table if not exists finance.chart_of_accounts(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),code varchar(40) not null,name varchar(160) not null,type varchar(16) not null check(type in('REVENUE','EXPENSE','COST','INVESTMENT','ASSET','LIABILITY')),nature varchar(8) not null check(nature in('DEBIT','CREDIT')),category varchar(100),parent_id uuid references finance.chart_of_accounts(id),active boolean not null default true,display_order integer not null default 0,created_at timestamptz not null default now(),created_by uuid not null,updated_at timestamptz,updated_by uuid,unique(tenant_id,code),unique(tenant_id,id));
create table if not exists finance.cost_centers(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),code varchar(40) not null,name varchar(160) not null,kind varchar(24) not null check(kind in('PROPERTY','FARM','PLOT','PADDOCK','SEASON','CROP','ACTIVITY','HERD','ANIMAL_LOT','MACHINE','EQUIPMENT','PROJECT','ADMINISTRATIVE')),reference_id uuid,active boolean not null default true,created_at timestamptz not null default now(),created_by uuid not null,updated_at timestamptz,updated_by uuid,unique(tenant_id,code),unique(tenant_id,id));
create table if not exists finance.payables(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),supplier_name varchar(160) not null,document varchar(100),original_amount numeric(18,4) not null check(original_amount>0),discount numeric(18,4) not null default 0 check(discount>=0),interest numeric(18,4) not null default 0 check(interest>=0),fine numeric(18,4) not null default 0 check(fine>=0),final_amount numeric(18,4) not null check(final_amount>=0),balance numeric(18,4) not null check(balance>=0),issued_on date not null,due_on date not null,settled_on date,account_id uuid not null references finance.chart_of_accounts(id),cost_center_id uuid references finance.cost_centers(id),notes text,source_id uuid,status varchar(12) not null check(status in('OPEN','PARTIAL','PAID','CANCELLED')),cancel_reason text,created_at timestamptz not null default now(),created_by uuid not null,updated_at timestamptz,updated_by uuid,check(due_on>=issued_on));
create table if not exists finance.receivables(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),customer_name varchar(160) not null,document varchar(100),original_amount numeric(18,4) not null check(original_amount>0),discount numeric(18,4) not null default 0 check(discount>=0),interest numeric(18,4) not null default 0 check(interest>=0),fine numeric(18,4) not null default 0 check(fine>=0),final_amount numeric(18,4) not null check(final_amount>=0),balance numeric(18,4) not null check(balance>=0),issued_on date not null,due_on date not null,settled_on date,account_id uuid not null references finance.chart_of_accounts(id),cost_center_id uuid references finance.cost_centers(id),notes text,source_id uuid,status varchar(12) not null check(status in('OPEN','PARTIAL','RECEIVED','CANCELLED')),cancel_reason text,created_at timestamptz not null default now(),created_by uuid not null,updated_at timestamptz,updated_by uuid,check(due_on>=issued_on));
create table if not exists finance.settlements(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),title_id uuid not null,direction varchar(3) not null check(direction in('IN','OUT')),amount numeric(18,4) not null check(amount>0),settled_on date not null,account_id uuid not null references finance.chart_of_accounts(id),notes text,created_at timestamptz not null default now(),created_by uuid not null);
create table if not exists finance.manual_entries(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),type varchar(12) not null check(type in('REVENUE','EXPENSE','COST','INVESTMENT')),amount numeric(18,4) not null check(amount>0),entry_date date not null,account_id uuid not null references finance.chart_of_accounts(id),cost_center_id uuid references finance.cost_centers(id),property_id uuid,season_id uuid,plot_id uuid,herd_id uuid,machine_id uuid,notes text,origin varchar(40) not null,status varchar(12) not null check(status in('POSTED','CANCELLED')),created_at timestamptz not null default now(),created_by uuid not null,updated_at timestamptz,updated_by uuid,check(type<>'COST' or cost_center_id is not null));
create table if not exists finance.cost_allocations(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),entry_id uuid not null references finance.manual_entries(id),cost_center_id uuid not null references finance.cost_centers(id),percentage numeric(7,4) not null check(percentage>0 and percentage<=100),amount numeric(18,4) not null check(amount>=0),unique(entry_id,cost_center_id));
create table if not exists commercial.agro_sales(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),buyer varchar(160) not null,product_id uuid not null references inventory.products(id),origin_type varchar(20) not null check(origin_type in('SEASON','PLOT','ANIMAL','ANIMAL_LOT','MILK','STOCK','OTHER')),origin_id uuid,quantity numeric(18,4) not null check(quantity>0),unit varchar(16) not null,unit_price numeric(18,4) not null check(unit_price>=0),total_amount numeric(18,4) not null check(total_amount>=0),sold_on date not null,delivery_on date,payment_terms varchar(100) not null,installments integer not null default 1 check(installments>0),warehouse_id uuid references inventory.warehouses(id),cost_center_id uuid references finance.cost_centers(id),status varchar(16) not null check(status in('DRAFT','CONFIRMED','PARTIAL','DELIVERED','INVOICED','CANCELLED')),cancel_reason text,created_at timestamptz not null default now(),created_by uuid not null,updated_at timestamptz,updated_by uuid);
create index if not exists ix_payables_due on finance.payables(tenant_id,due_on,status); create index if not exists ix_receivables_due on finance.receivables(tenant_id,due_on,status); create index if not exists ix_settlements_date on finance.settlements(tenant_id,settled_on); create index if not exists ix_manual_results on finance.manual_entries(tenant_id,entry_date,type);
create or replace function finance.cash_flow(p_tenant uuid,p_from date default null,p_to date default null) returns table(period_date date,period_kind text,description text,account_id uuid,cost_center_id uuid,expected_in numeric,expected_out numeric,actual_in numeric,actual_out numeric) language sql stable as $$ select d,'DAILY',description,account_id,cost_center_id,sum(ei),sum(eo),sum(ai),sum(ao) from (select due_on d,customer_name description,account_id,cost_center_id,balance ei,0::numeric eo,0::numeric ai,0::numeric ao from finance.receivables where tenant_id=p_tenant and status in('OPEN','PARTIAL') union all select due_on,supplier_name,account_id,cost_center_id,0,balance,0,0 from finance.payables where tenant_id=p_tenant and status in('OPEN','PARTIAL') union all select settled_on,'BAIXA',account_id,null,0,0,case when direction='IN' then amount else 0 end,case when direction='OUT' then amount else 0 end from finance.settlements where tenant_id=p_tenant) x where d between coalesce(p_from,'0001-01-01') and coalesce(p_to,'9999-12-31') group by d,description,account_id,cost_center_id order by d $$;
create or replace function finance.economic_results(p_tenant uuid,p_from date default null,p_to date default null,p_property uuid default null,p_season uuid default null,p_plot uuid default null,p_herd uuid default null,p_machine uuid default null) returns table(dimension text,dimension_id uuid,gross_revenue numeric,direct_costs numeric,indirect_expenses numeric,investments numeric,gross_margin numeric,net_margin numeric) language sql stable as $$ select case when p_season is not null then 'SEASON' when p_plot is not null then 'PLOT' when p_herd is not null then 'HERD' when p_machine is not null then 'MACHINE' when p_property is not null then 'PROPERTY' else 'CONSOLIDATED' end,coalesce(p_season,p_plot,p_herd,p_machine,p_property),sum(amount) filter(where type='REVENUE'),sum(amount) filter(where type='COST'),sum(amount) filter(where type='EXPENSE'),sum(amount) filter(where type='INVESTMENT'),sum(amount) filter(where type='REVENUE')-sum(amount) filter(where type='COST'),sum(amount) filter(where type='REVENUE')-sum(amount) filter(where type in('COST','EXPENSE')) from finance.manual_entries where tenant_id=p_tenant and status='POSTED' and entry_date between coalesce(p_from,'0001-01-01') and coalesce(p_to,'9999-12-31') and (p_property is null or property_id=p_property) and (p_season is null or season_id=p_season) and (p_plot is null or plot_id=p_plot) and (p_herd is null or herd_id=p_herd) and (p_machine is null or machine_id=p_machine) $$;
create or replace function finance.dashboard(p_tenant uuid) returns table(expected_balance numeric,actual_balance numeric,overdue_payables numeric,overdue_receivables numeric,payable_month numeric,receivable_month numeric,monthly_revenue numeric,monthly_expense numeric,monthly_margin numeric,top_costs jsonb,top_revenues jsonb,alerts jsonb) language sql stable as $$ select (select coalesce(sum(balance),0) from finance.receivables where tenant_id=p_tenant and status in('OPEN','PARTIAL'))-(select coalesce(sum(balance),0) from finance.payables where tenant_id=p_tenant and status in('OPEN','PARTIAL')),(select coalesce(sum(case when direction='IN' then amount else -amount end),0) from finance.settlements where tenant_id=p_tenant),(select coalesce(sum(balance),0) from finance.payables where tenant_id=p_tenant and status in('OPEN','PARTIAL') and due_on<current_date),(select coalesce(sum(balance),0) from finance.receivables where tenant_id=p_tenant and status in('OPEN','PARTIAL') and due_on<current_date),(select coalesce(sum(balance),0) from finance.payables where tenant_id=p_tenant and status in('OPEN','PARTIAL') and date_trunc('month',due_on)=date_trunc('month',current_date)),(select coalesce(sum(balance),0) from finance.receivables where tenant_id=p_tenant and status in('OPEN','PARTIAL') and date_trunc('month',due_on)=date_trunc('month',current_date)),coalesce(sum(amount) filter(where type='REVENUE'),0),coalesce(sum(amount) filter(where type in('EXPENSE','COST')),0),coalesce(sum(amount) filter(where type='REVENUE'),0)-coalesce(sum(amount) filter(where type in('EXPENSE','COST')),0),'[]'::jsonb,'[]'::jsonb,jsonb_build_array(case when exists(select 1 from finance.payables where tenant_id=p_tenant and status in('OPEN','PARTIAL') and due_on<current_date) then 'Há contas a pagar vencidas' end) from finance.manual_entries where tenant_id=p_tenant and status='POSTED' and date_trunc('month',entry_date)=date_trunc('month',current_date) $$;
do $$ declare t text; begin foreach t in array array['chart_of_accounts','cost_centers','payables','receivables','settlements','manual_entries','cost_allocations'] loop execute format('alter table finance.%I enable row level security',t); if not exists(select 1 from pg_policies where schemaname='finance' and tablename=t and policyname=t||'_tenant') then execute format('create policy %I on finance.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',t||'_tenant',t); end if; end loop; end $$;
alter table commercial.agro_sales enable row level security; do $$ begin if not exists(select 1 from pg_policies where schemaname='commercial' and tablename='agro_sales' and policyname='agro_sales_tenant') then create policy agro_sales_tenant on commercial.agro_sales using(tenant_id=nullif(current_setting('app.tenant_id',true),'')::uuid) with check(tenant_id=nullif(current_setting('app.tenant_id',true),'')::uuid); end if; end $$;
insert into platform.schema_versions(version,description,installed_at) values('0.5.0','Sprint 8 - Financeiro Agro',now()) on conflict(version) do nothing;
begin;
create schema if not exists storage; create schema if not exists logistics;
create table if not exists storage.structures(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),code varchar(40) not null,name varchar(160) not null,type varchar(30) not null check(type in('WAREHOUSE','SILO','HOPPER','BIN','SHED','CHAMBER','DEPOT','BOX')),location varchar(240) not null,total_capacity numeric(18,3) not null check(total_capacity>=0),available_capacity numeric(18,3) not null check(available_capacity>=0),unit varchar(12) not null,allowed_product_id uuid, status varchar(20) not null check(status in('AVAILABLE','IN_USE','FULL','MAINTENANCE','INTERDICTED','INACTIVE')),property_id uuid,responsible varchar(160),notes text,created_at timestamptz not null default now(),updated_at timestamptz,created_by uuid,updated_by uuid,unique(tenant_id,code),check(available_capacity<=total_capacity));
create table if not exists storage.structure_usage(id uuid primary key,tenant_id uuid not null,structure_id uuid not null references storage.structures(id),movement varchar(20) not null,quantity numeric(18,3) not null,reference_type varchar(40),reference_id uuid,notes text,created_at timestamptz not null default now(),created_by uuid);
create table if not exists storage.quality_parameters(id uuid primary key,tenant_id uuid not null,product_id uuid not null,name varchar(40) not null,warning_value numeric(10,4),reject_value numeric(10,4),discount_percent numeric(7,4) not null default 0,active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz,created_by uuid,updated_by uuid,unique(tenant_id,product_id,name),check(coalesce(warning_value,0)>=0 and coalesce(reject_value,0)>=0 and discount_percent>=0));
create table if not exists storage.receipts(id uuid primary key,tenant_id uuid not null,number varchar(40) not null,entry_type varchar(30) not null check(entry_type in('OWN_PRODUCTION','PURCHASE','THIRD_PARTY','TRANSFER')),product_id uuid not null,season_id uuid,plot_id uuid,origin_property_id uuid,supplier varchar(160),carrier varchar(160),driver varchar(160),vehicle varchar(100),plate varchar(16),gross_weight numeric(18,3),tare numeric(18,3),net_weight numeric(18,3),technical_discount numeric(7,4) not null default 0,final_weight numeric(18,3),entered_at timestamptz not null,unloading_location varchar(240),destination_structure_id uuid not null references storage.structures(id),quality_result varchar(20),status varchar(30) not null,notes text,cancel_reason text,created_at timestamptz not null default now(),updated_at timestamptz,created_by uuid,updated_by uuid,unique(tenant_id,number),check(coalesce(gross_weight,0)>=0 and coalesce(tare,0)>=0 and coalesce(tare,0)<=coalesce(gross_weight,0)));
create table if not exists storage.quality_classifications(id uuid primary key,tenant_id uuid not null,receipt_id uuid references storage.receipts(id),lot_id uuid,moisture numeric(8,4) not null,impurity numeric(8,4) not null,damaged numeric(8,4) not null,burnt numeric(8,4) not null,broken numeric(8,4) not null,green numeric(8,4),hectoliter_weight numeric(8,3),protein numeric(8,4),acidity numeric(8,4),temperature numeric(8,3),result varchar(20) not null,report text not null,responsible varchar(160),notes text,created_at timestamptz not null default now(),created_by uuid,check(moisture>=0 and impurity>=0 and damaged>=0 and burnt>=0 and broken>=0));
create table if not exists storage.lots(id uuid primary key,tenant_id uuid not null,code varchar(60) not null,product_id uuid not null,season_id uuid,origin text,structure_id uuid not null references storage.structures(id),initial_quantity numeric(18,3) not null,current_balance numeric(18,3) not null,average_quality jsonb not null default '{}'::jsonb,formed_at timestamptz not null default now(),status varchar(20) not null check(status in('OPEN','BLOCKED','AVAILABLE','IN_DISPATCH','CLOSED')),block_reason text,created_at timestamptz not null default now(),updated_at timestamptz,created_by uuid,unique(tenant_id,code),check(initial_quantity>=0 and current_balance>=0));
create table if not exists storage.lot_origins(id uuid primary key,tenant_id uuid not null,lot_id uuid not null references storage.lots(id),receipt_id uuid references storage.receipts(id),source_lot_id uuid references storage.lots(id),plot_id uuid,quantity numeric(18,3) not null,created_at timestamptz not null default now());
create table if not exists storage.lot_movements(id uuid primary key,tenant_id uuid not null,lot_id uuid not null references storage.lots(id),type varchar(30) not null,quantity numeric(18,3) not null,from_structure_id uuid,to_structure_id uuid,reference_id uuid,notes text,created_at timestamptz not null default now(),created_by uuid);
create table if not exists storage.processing_orders(id uuid primary key,tenant_id uuid not null,input_lot_id uuid not null references storage.lots(id),output_product_id uuid not null,input_quantity numeric(18,3) not null,process varchar(30) not null check(process in('DRYING','CLEANING','CLASSIFICATION','BLENDING','REPROCESSING','OTHER')),technical_loss numeric(18,3),output_lot_id uuid,output_quantity numeric(18,3),cost numeric(18,2) not null default 0,responsible varchar(160),status varchar(20) not null,justification text,notes text,cancel_reason text,started_at timestamptz,completed_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz,created_by uuid,check(input_quantity>0 and cost>=0));
create table if not exists commercial.delivery_contracts(id uuid primary key,tenant_id uuid not null,number varchar(50) not null,customer varchar(160) not null,product_id uuid not null,contracted_quantity numeric(18,3) not null,delivered_quantity numeric(18,3) not null default 0,contracted_price numeric(18,4) not null,unit varchar(12) not null,delivery_deadline date not null,payment_terms varchar(240) not null,status varchar(30) not null,cancellation_reason text,allow_overdelivery boolean not null default false,created_at timestamptz not null default now(),updated_at timestamptz,created_by uuid,unique(tenant_id,number),check(contracted_quantity>0 and delivered_quantity>=0 and contracted_price>=0));
create table if not exists storage.shipments(id uuid primary key,tenant_id uuid not null,number varchar(50) not null,contract_id uuid references commercial.delivery_contracts(id),customer varchar(160) not null,product_id uuid not null,lot_id uuid not null references storage.lots(id),requested_quantity numeric(18,3) not null,loaded_quantity numeric(18,3),gross_weight numeric(18,3),tare numeric(18,3),net_weight numeric(18,3),destination varchar(240) not null,carrier varchar(160),driver varchar(160),vehicle varchar(100),plate varchar(16),status varchar(20) not null,loaded_at timestamptz,dispatched_at timestamptz,cancel_reason text,created_at timestamptz not null default now(),updated_at timestamptz,created_by uuid,unique(tenant_id,number),check(requested_quantity>0));
create table if not exists logistics.trips(id uuid primary key,tenant_id uuid not null,number varchar(50) not null,shipment_id uuid references storage.shipments(id),origin varchar(240) not null,destination varchar(240) not null,estimated_distance numeric(14,3) not null,carrier varchar(160),driver varchar(160),vehicle varchar(100),freight_type varchar(30) not null,freight_value numeric(18,2) not null,cost_per_tonne numeric(18,2) not null default 0,cost_per_km numeric(18,2) not null default 0,status varchar(30) not null,delivered_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz,created_by uuid,unique(tenant_id,number),check(estimated_distance>=0 and freight_value>=0));
create table if not exists logistics.trip_occurrences(id uuid primary key,tenant_id uuid not null,trip_id uuid not null references logistics.trips(id),description text not null,created_at timestamptz not null default now(),created_by uuid);
create index if not exists ix_storage_receipts_tenant_status on storage.receipts(tenant_id,status); create index if not exists ix_storage_lots_tenant_product on storage.lots(tenant_id,product_id,status); create index if not exists ix_shipments_tenant_status on storage.shipments(tenant_id,status); create index if not exists ix_trips_tenant_status on logistics.trips(tenant_id,status);
create or replace function storage.unload_receipt(p_tenant uuid,p_receipt uuid,p_user uuid) returns void language plpgsql as $$ declare r storage.receipts%rowtype; s storage.structures%rowtype; l uuid; begin select * into r from storage.receipts where tenant_id=p_tenant and id=p_receipt for update; if r.status<>'APPROVED' then raise exception 'Romaneio não aprovado'; end if; select * into s from storage.structures where tenant_id=p_tenant and id=r.destination_structure_id for update; if s.status in('INACTIVE','INTERDICTED','MAINTENANCE') or s.available_capacity<r.final_weight then raise exception 'Estrutura indisponível ou sem capacidade'; end if; select id into l from storage.lots where tenant_id=p_tenant and product_id=r.product_id and structure_id=s.id and status='OPEN' limit 1 for update; if l is null then l=gen_random_uuid(); insert into storage.lots(id,tenant_id,code,product_id,season_id,origin,structure_id,initial_quantity,current_balance,status,created_by) values(l,p_tenant,'LOT-'||replace(l::text,'-',''),r.product_id,r.season_id,r.entry_type,s.id,r.final_weight,r.final_weight,'OPEN',p_user); else update storage.lots set initial_quantity=initial_quantity+r.final_weight,current_balance=current_balance+r.final_weight where id=l; end if; insert into storage.lot_origins values(gen_random_uuid(),p_tenant,l,r.id,null,r.plot_id,r.final_weight,now()); insert into storage.lot_movements values(gen_random_uuid(),p_tenant,l,'RECEIPT',r.final_weight,null,s.id,r.id,null,now(),p_user); update storage.structures set available_capacity=available_capacity-r.final_weight,status=case when available_capacity-r.final_weight=0 then 'FULL' else 'IN_USE' end,updated_at=now() where id=s.id; update storage.receipts set status='UNLOADED',updated_at=now() where id=r.id; end $$;
create or replace function storage.transfer_lot(p_tenant uuid,p_lot uuid,p_destination uuid,p_quantity numeric,p_user uuid,p_notes text,p_overflow boolean) returns void language plpgsql as $$ declare l storage.lots%rowtype; d storage.structures%rowtype; begin select * into l from storage.lots where tenant_id=p_tenant and id=p_lot for update; select * into d from storage.structures where tenant_id=p_tenant and id=p_destination for update; if l.status='BLOCKED' or p_quantity<=0 or p_quantity>l.current_balance then raise exception 'Lote indisponível'; end if; if d.status in('INACTIVE','INTERDICTED','MAINTENANCE') or (not p_overflow and d.available_capacity<p_quantity) then raise exception 'Destino indisponível'; end if; update storage.structures set available_capacity=least(total_capacity,available_capacity+p_quantity) where id=l.structure_id; update storage.structures set available_capacity=greatest(0,available_capacity-p_quantity) where id=d.id; update storage.lots set structure_id=d.id,updated_at=now() where id=l.id; insert into storage.lot_movements values(gen_random_uuid(),p_tenant,l.id,'TRANSFER',p_quantity,l.structure_id,d.id,null,p_notes,now(),p_user); end $$;
create or replace function storage.complete_processing(p_tenant uuid,p_order uuid,p_output numeric,p_loss numeric,p_justification text,p_user uuid) returns void language plpgsql as $$ declare o storage.processing_orders%rowtype; l storage.lots%rowtype; n uuid:=gen_random_uuid(); begin select * into o from storage.processing_orders where tenant_id=p_tenant and id=p_order for update; select * into l from storage.lots where tenant_id=p_tenant and id=o.input_lot_id for update; if o.status<>'IN_PROGRESS' or p_output<=0 or o.input_quantity>l.current_balance or (p_output>o.input_quantity and coalesce(p_justification,'')='') or p_output+p_loss>o.input_quantity then raise exception 'Conclusão de processamento inválida'; end if; update storage.lots set current_balance=current_balance-o.input_quantity,status=case when current_balance-o.input_quantity=0 then 'CLOSED' else status end where id=l.id; insert into storage.lots(id,tenant_id,code,product_id,season_id,origin,structure_id,initial_quantity,current_balance,status,created_by) values(n,p_tenant,'PROC-'||replace(n::text,'-',''),o.output_product_id,l.season_id,'PROCESSING:'||o.id,l.structure_id,p_output,p_output,'AVAILABLE',p_user); insert into storage.lot_origins values(gen_random_uuid(),p_tenant,n,null,l.id,null,p_output,now()); update storage.processing_orders set output_lot_id=n,output_quantity=p_output,technical_loss=p_loss,justification=p_justification,status='COMPLETED',completed_at=now(),updated_at=now() where id=o.id; end $$;
create or replace function storage.dispatch_shipment(p_tenant uuid,p_shipment uuid,p_user uuid) returns void language plpgsql as $$ declare s storage.shipments%rowtype; l storage.lots%rowtype; c commercial.delivery_contracts%rowtype; begin select * into s from storage.shipments where tenant_id=p_tenant and id=p_shipment for update; select * into l from storage.lots where tenant_id=p_tenant and id=s.lot_id for update; if s.status<>'LOADED' or l.status='BLOCKED' or s.loaded_quantity>l.current_balance then raise exception 'Expedição inválida'; end if; update storage.lots set current_balance=current_balance-s.loaded_quantity,status=case when current_balance-s.loaded_quantity=0 then 'CLOSED' else 'AVAILABLE' end where id=l.id; update storage.structures set available_capacity=least(total_capacity,available_capacity+s.loaded_quantity) where id=l.structure_id; insert into storage.lot_movements values(gen_random_uuid(),p_tenant,l.id,'SHIPMENT',s.loaded_quantity,l.structure_id,null,s.id,null,now(),p_user); update storage.shipments set status='DISPATCHED',dispatched_at=now(),updated_at=now() where id=s.id; if s.contract_id is not null then select * into c from commercial.delivery_contracts where tenant_id=p_tenant and id=s.contract_id for update; if not c.allow_overdelivery and c.delivered_quantity+s.loaded_quantity>c.contracted_quantity then raise exception 'Entrega supera saldo contratado'; end if; update commercial.delivery_contracts set delivered_quantity=delivered_quantity+s.loaded_quantity,status=case when delivered_quantity+s.loaded_quantity>=contracted_quantity then 'DELIVERED' else 'PARTIALLY_DELIVERED' end,updated_at=now() where id=c.id; end if; end $$;
create or replace function storage.dashboard(p_tenant uuid) returns table(total_capacity numeric,occupied_capacity numeric,available_capacity numeric,blocked_lots bigint,pending_receipts bigint,unloaded_this_month bigint,technical_losses numeric,pending_shipments bigint,shipments_this_month bigint,open_contracts bigint,trips_in_transit bigint,freight_this_month numeric,quality_alerts bigint,capacity_alerts bigint) language sql stable as $$ select coalesce(sum(total_capacity),0),coalesce(sum(total_capacity-available_capacity),0),coalesce(sum(available_capacity),0),(select count(*) from storage.lots where tenant_id=p_tenant and status='BLOCKED'),(select count(*) from storage.receipts where tenant_id=p_tenant and status not in('UNLOADED','CANCELLED')),(select count(*) from storage.receipts where tenant_id=p_tenant and status='UNLOADED' and updated_at>=date_trunc('month',now())),(select coalesce(sum(technical_loss),0) from storage.processing_orders where tenant_id=p_tenant and completed_at>=date_trunc('month',now())),(select count(*) from storage.shipments where tenant_id=p_tenant and status not in('DISPATCHED','CANCELLED')),(select count(*) from storage.shipments where tenant_id=p_tenant and dispatched_at>=date_trunc('month',now())),(select count(*) from commercial.delivery_contracts where tenant_id=p_tenant and status in('OPEN','PARTIALLY_DELIVERED')),(select count(*) from logistics.trips where tenant_id=p_tenant and status='IN_TRANSIT'),(select coalesce(sum(freight_value),0) from logistics.trips where tenant_id=p_tenant and created_at>=date_trunc('month',now())),(select count(*) from storage.quality_classifications where tenant_id=p_tenant and result='REJECTED'),count(*) filter(where total_capacity>0 and available_capacity/total_capacity<.1) from storage.structures where tenant_id=p_tenant $$;
do $$ declare rec record; begin for rec in select * from (values ('storage','structures'),('storage','structure_usage'),('storage','quality_parameters'),('storage','receipts'),('storage','quality_classifications'),('storage','lots'),('storage','lot_origins'),('storage','lot_movements'),('storage','processing_orders'),('storage','shipments'),('logistics','trips'),('logistics','trip_occurrences'),('commercial','delivery_contracts')) x(schema_name,table_name) loop execute format('alter table %I.%I enable row level security',rec.schema_name,rec.table_name); execute format('alter table %I.%I force row level security',rec.schema_name,rec.table_name); if not exists(select 1 from pg_policies where schemaname=rec.schema_name and tablename=rec.table_name and policyname=rec.table_name||'_tenant') then execute format('create policy %I on %I.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',rec.table_name||'_tenant',rec.schema_name,rec.table_name); end if; end loop; end $$;
insert into identity.permissions(code,module,description) values ('storage.read','Storage','Consultar armazenagem e pós-colheita.'),('storage.write','Storage','Operar armazenagem e pós-colheita.'),('logistics.read','Logistics','Consultar viagens e fretes.'),('logistics.write','Logistics','Operar viagens e fretes.') on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('0.6.0','Sprint 9 - Armazenagem e Logística',now()) on conflict(version) do nothing;
create or replace view storage.product_traceability as select l.tenant_id,l.id lot_id,l.code lot_code,l.product_id,l.season_id,o.receipt_id,o.plot_id,o.source_lot_id,s.id shipment_id,s.number shipment_number from storage.lots l left join storage.lot_origins o on o.lot_id=l.id left join storage.shipments s on s.lot_id=l.id;
create schema if not exists traceability; create schema if not exists processing; create schema if not exists ledger; create schema if not exists regional_logistics; create schema if not exists sales_network;
create table if not exists traceability.lots(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),code varchar(80) not null,product_id uuid not null references inventory.products(id),property_id uuid,plot_id uuid,producer varchar(160) not null,cooperative varchar(160),harvested_at timestamptz not null,quantity numeric(18,4) not null check(quantity>0),unit varchar(16) not null,notes text,status varchar(20) not null default 'ACTIVE',created_at timestamptz not null default now(),created_by uuid not null,updated_at timestamptz,unique(tenant_id,code));
create table if not exists traceability.lot_events(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),lot_id uuid not null references traceability.lots(id),event_type varchar(60) not null,payload jsonb not null,source_lot_id uuid references traceability.lots(id),composition_quantity numeric(18,4),occurred_at timestamptz not null,created_by uuid not null,check(event_type<>'MIX' or (source_lot_id is not null and composition_quantity>0)));
create table if not exists processing.compliance_rules(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),product_id uuid not null references inventory.products(id),step varchar(60) not null,minimum_minutes integer not null default 0,minimum_temperature numeric(8,2),effective_from date not null default current_date,active boolean not null default true,created_at timestamptz not null default now(),created_by uuid not null);
create table if not exists processing.compliance_events(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),lot_id uuid not null references traceability.lots(id),step varchar(60) not null,responsible varchar(160) not null,started_at timestamptz not null,ended_at timestamptz not null,duration_minutes integer not null generated always as ((extract(epoch from (ended_at-started_at))/60)::integer) stored,temperature numeric(8,2),equipment varchar(160),evidence text,status varchar(20) not null,notes text,result jsonb,created_at timestamptz not null default now(),created_by uuid not null,check(ended_at>started_at));
create table if not exists ledger.events(sequence bigint generated always as identity primary key,id uuid not null unique,tenant_id uuid not null references tenancy.tenants(id),entity_name varchar(100) not null,entity_id uuid not null,event_type varchar(80) not null,payload jsonb not null,previous_hash char(64),current_hash char(64) not null,occurred_at timestamptz not null,user_id uuid not null,logical_signature text not null,status varchar(20) not null,unique(tenant_id,current_hash));
create or replace function ledger.reject_mutation() returns trigger language plpgsql as $$ begin raise exception 'Eventos do ledger são imutáveis; registre compensação'; end $$;
drop trigger if exists ledger_immutable on ledger.events; create trigger ledger_immutable before update or delete on ledger.events for each row execute function ledger.reject_mutation();
create table if not exists traceability.certificates(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),lot_id uuid not null references traceability.lots(id),code varchar(64) not null unique,issued_at timestamptz not null,revoked_at timestamptz,created_by uuid not null);
create or replace function traceability.public_certificate(p_code text) returns jsonb language sql stable security definer set search_path=pg_catalog,public as $$ select jsonb_build_object('code',c.code,'issuedAt',c.issued_at,'lotCode',l.code,'product',p.name,'producer',l.producer,'cooperative',l.cooperative,'harvestedAt',l.harvested_at,'quantity',l.quantity,'unit',l.unit,'processing',(select coalesce(jsonb_agg(jsonb_build_object('step',e.step,'status',e.status,'startedAt',e.started_at,'endedAt',e.ended_at)),'[]'::jsonb) from processing.compliance_events e where e.lot_id=l.id),'ledgerRegistered',exists(select 1 from ledger.events e where e.tenant_id=c.tenant_id and e.entity_id in(c.id,l.id))) from traceability.certificates c join traceability.lots l on l.id=c.lot_id join inventory.products p on p.id=l.product_id where c.code=p_code and c.revoked_at is null $$; revoke all on function traceability.public_certificate(text) from public; grant execute on function traceability.public_certificate(text) to public;
create table if not exists regional_logistics.points(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(160) not null,type varchar(40) not null,community varchar(160),latitude numeric(10,7),longitude numeric(10,7),created_at timestamptz not null default now(),created_by uuid not null);
create table if not exists regional_logistics.routes(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),code varchar(60) not null,name varchar(160) not null,modal varchar(20) not null check(modal in('FLUVIAL','ROAD','VICINAL','FERRY','MIXED')),origin_point_id uuid not null references regional_logistics.points(id),destination_point_id uuid not null references regional_logistics.points(id),estimated_cost numeric(18,2) not null default 0,estimated_minutes integer not null,notes text,created_at timestamptz not null default now(),created_by uuid not null,unique(tenant_id,code));
create table if not exists regional_logistics.route_segments(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),route_id uuid not null references regional_logistics.routes(id),position integer not null,modal varchar(20) not null,origin_point_id uuid not null references regional_logistics.points(id),destination_point_id uuid not null references regional_logistics.points(id),estimated_minutes integer not null,capacity numeric(18,3),road_condition varchar(20),risk varchar(20),unique(route_id,position));
create table if not exists regional_logistics.operational_windows(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),segment_id uuid not null references regional_logistics.route_segments(id),weekday integer check(weekday between 0 and 6),opens_at time not null,closes_at time not null,season varchar(30),created_by uuid not null);
create table if not exists regional_logistics.vehicles(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(120) not null,type varchar(40) not null,capacity numeric(18,3) not null,registration varchar(80),active boolean not null default true,created_by uuid not null);
create table if not exists regional_logistics.trips(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),route_id uuid not null references regional_logistics.routes(id),number varchar(60) not null,vehicle_id uuid references regional_logistics.vehicles(id),planned_start timestamptz not null,interdiction_authorized boolean not null default false,status varchar(20) not null,created_at timestamptz not null default now(),created_by uuid not null,unique(tenant_id,number));
create table if not exists regional_logistics.occurrences(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),trip_id uuid not null references regional_logistics.trips(id),type varchar(50) not null,description text not null,occurred_at timestamptz not null,latitude numeric(10,7),longitude numeric(10,7),created_by uuid not null);
create table if not exists sales_network.partners(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(160) not null,type varchar(30) not null check(type in('PRODUCER','COOPERATIVE','SELLER','REPRESENTATIVE','PLATFORM')),document varchar(40),contact varchar(160),active boolean not null,created_at timestamptz not null default now(),created_by uuid not null);
create table if not exists sales_network.commission_rules(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),partner_id uuid not null references sales_network.partners(id),role varchar(30) not null,calculation_type varchar(12) not null check(calculation_type in('PERCENTAGE','FIXED')),value numeric(18,4) not null check(value>=0),effective_from date not null,effective_to date,created_at timestamptz not null default now(),created_by uuid not null);
create table if not exists sales_network.revenue_splits(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),sale_id uuid not null,gross_amount numeric(18,2) not null,status varchar(20) not null,provider_reference text,approved_at timestamptz,created_at timestamptz not null default now(),created_by uuid not null);
create table if not exists sales_network.split_items(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),split_id uuid not null references sales_network.revenue_splits(id),partner_id uuid not null references sales_network.partners(id),role varchar(30) not null,amount numeric(18,2) not null,status varchar(20) not null);
create table if not exists sales_network.commission_entries(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),split_item_id uuid not null references sales_network.split_items(id),amount numeric(18,2) not null,status varchar(20) not null,paid_at timestamptz,created_by uuid not null);
do $$ declare rec record; begin for rec in select * from (values ('traceability','lots'),('traceability','lot_events'),('traceability','certificates'),('processing','compliance_rules'),('processing','compliance_events'),('ledger','events'),('regional_logistics','points'),('regional_logistics','routes'),('regional_logistics','route_segments'),('regional_logistics','operational_windows'),('regional_logistics','vehicles'),('regional_logistics','trips'),('regional_logistics','occurrences'),('sales_network','partners'),('sales_network','commission_rules'),('sales_network','revenue_splits'),('sales_network','split_items'),('sales_network','commission_entries')) x(schema_name,table_name) loop execute format('alter table %I.%I enable row level security',rec.schema_name,rec.table_name); execute format('alter table %I.%I force row level security',rec.schema_name,rec.table_name); if not exists(select 1 from pg_policies where schemaname=rec.schema_name and tablename=rec.table_name and policyname=rec.table_name||'_tenant') then execute format('create policy %I on %I.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',rec.table_name||'_tenant',rec.schema_name,rec.table_name); end if; end loop; end $$;
insert into identity.permissions(code,module,description) values ('traceability.read','Traceability','Consultar rastreabilidade.'),('traceability.write','Traceability','Operar rastreabilidade.'),('ledger.validate','Ledger','Validar ledger.'),('regional-logistics.read','RegionalLogistics','Consultar logística regional.'),('regional-logistics.write','RegionalLogistics','Operar logística regional.'),('sales-network.read','SalesNetwork','Consultar rede de vendas.'),('sales-network.write','SalesNetwork','Operar rede e splits.'),('sales-network.approve','SalesNetwork','Aprovar splits.') on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into inventory.products(id,tenant_id,sku,name,category,base_unit,requires_lot,created_by) select gen_random_uuid(),t.id,'AMZ-'||x.sku,x.name,'AMAZON_REGIONAL',x.unit,true,(select id from identity.users where tenant_id=t.id order by created_at limit 1) from tenancy.tenants t cross join (values('ACAI','Açaí','kg'),('TUCUPI','Tucupi','l'),('CACAU','Cacau','kg'),('CASTANHA','Castanha-do-pará','kg'),('MANDIOCA','Mandioca/Farinha','kg'),('GEN','Produto genérico','kg')) x(sku,name,unit) where exists(select 1 from identity.users where tenant_id=t.id) and not exists(select 1 from inventory.products p where p.tenant_id=t.id and p.sku='AMZ-'||x.sku);
insert into platform.schema_versions(version,description,installed_at) values('0.7.0','Sprint 10 - Rastreabilidade Amazônica',now()) on conflict(version) do nothing;


-- Sprint 11 - Agricultura 360
create table if not exists agriculture.crops(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), name varchar(120) not null,
 description varchar(300), active boolean not null default true, created_at timestamptz not null default now(),
 created_by uuid not null, unique(tenant_id,name));
create table if not exists agriculture.records(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), module varchar(30) not null,
 status varchar(24) not null, data jsonb not null default '{}'::jsonb, created_at timestamptz not null default now(),
 created_by uuid not null, updated_at timestamptz, updated_by uuid, deleted_at timestamptz, version bigint not null default 1,
 unique(tenant_id,id), check(module in('field-notes','plans','scouting','recommendations','applications','irrigations','weather-records','work-orders')),
 check(status in('OPEN','PLANNED','RELEASED','IN_PROGRESS','PAUSED','COMPLETED','CANCELLED','APPROVED','REVISION','CLOSED')));
create index if not exists ix_agriculture_records_tenant_module on agriculture.records(tenant_id,module,created_at desc) where deleted_at is null;
create index if not exists ix_agriculture_records_data on agriculture.records using gin(data);
create table if not exists agriculture.status_history(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), record_id uuid not null references agriculture.records(id),
 from_status varchar(24) not null, to_status varchar(24) not null, reason varchar(500), changed_at timestamptz not null default now(), changed_by uuid not null);
do $$ declare rec record; begin for rec in select * from (values ('agriculture','crops'),('agriculture','records'),('agriculture','status_history')) x(schema_name,table_name) loop execute format('alter table %I.%I enable row level security',rec.schema_name,rec.table_name); execute format('alter table %I.%I force row level security',rec.schema_name,rec.table_name); if not exists(select 1 from pg_policies where schemaname=rec.schema_name and tablename=rec.table_name and policyname=rec.table_name||'_tenant') then execute format('create policy %I on %I.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',rec.table_name||'_tenant',rec.schema_name,rec.table_name); end if; end loop; end $$;
insert into platform.schema_versions(version,description,installed_at) values('0.8.0','Sprint 11 - Agricultura 360',now()) on conflict(version) do nothing;



-- Sprint 12 - Operacao de Campo Mobile/Offline
create schema if not exists mobile;
create table if not exists mobile.devices(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),user_id uuid not null,last_seen_at timestamptz not null,platform varchar(40),push_token_hash text,unique(id,tenant_id,user_id));
create table if not exists mobile.sessions(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),user_id uuid not null,device_id uuid not null references mobile.devices(id),started_at timestamptz not null default now(),ended_at timestamptz,last_sync_at timestamptz);
create table if not exists mobile.sync_batches(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),user_id uuid not null,device_id uuid not null references mobile.devices(id),session_id uuid,status varchar(20) not null,started_at timestamptz not null,finished_at timestamptz);
create table if not exists mobile.offline_commands(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),user_id uuid not null,device_id uuid not null,session_id uuid,batch_id uuid references mobile.sync_batches(id),idempotency_key varchar(160) not null,temporary_id varchar(160) not null,command_type varchar(80) not null,payload jsonb not null,status varchar(20) not null,created_offline_at timestamptz not null,processed_at timestamptz,definitive_id uuid,unique(tenant_id,user_id,idempotency_key));
create table if not exists mobile.sync_items(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),batch_id uuid not null references mobile.sync_batches(id),command_id uuid not null references mobile.offline_commands(id),status varchar(20) not null,processed_at timestamptz);
create table if not exists mobile.sync_errors(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),batch_id uuid not null references mobile.sync_batches(id),temporary_id varchar(160),error_code varchar(80) not null,message text not null,retryable boolean not null,created_at timestamptz not null default now());
create table if not exists mobile.sync_conflicts(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),batch_id uuid references mobile.sync_batches(id),command_id uuid,server_version jsonb not null,client_version jsonb not null,resolution varchar(30),resolved_at timestamptz,resolved_by uuid,created_at timestamptz not null default now());
create table if not exists mobile.id_mappings(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),temporary_id varchar(160) not null,definitive_id uuid not null,entity_type varchar(80) not null,unique(tenant_id,temporary_id));
create table if not exists mobile.quick_records(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),area varchar(20) not null check(area in('agriculture','livestock','inventory','logistics')),kind varchar(60) not null,entity_type varchar(40) not null,entity_id uuid not null,quantity numeric(18,4) check(quantity>=0),occurred_at timestamptz not null,notes text,latitude numeric(10,7) check(latitude between -90 and 90),longitude numeric(10,7) check(longitude between -180 and 180),created_at timestamptz not null default now(),created_by uuid not null);
create table if not exists mobile.evidences(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),type varchar(30) not null,entity_type varchar(40) not null,entity_id uuid not null,file_name varchar(255) not null,content_type varchar(100) not null,file_data bytea not null,file_hash char(64) not null,notes text,latitude numeric(10,7),longitude numeric(10,7),captured_at timestamptz not null,sync_status varchar(20) not null,created_at timestamptz not null default now(),created_by uuid not null);
create table if not exists mobile.geolocation_events(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),entity_type varchar(40) not null,entity_id uuid not null,event_type varchar(60) not null,origin varchar(20) not null check(origin in('MANUAL','GPS','INTEGRATION')),latitude numeric(10,7) check(latitude between -90 and 90),longitude numeric(10,7) check(longitude between -180 and 180),accuracy numeric(10,2) check(accuracy>=0),occurred_at timestamptz not null,location_available boolean not null,created_at timestamptz not null default now(),created_by uuid not null);
create table if not exists mobile.qr_codes(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),entity_type varchar(40) not null,entity_id uuid not null,code varchar(64) not null unique,is_public boolean not null,active boolean not null default true,created_at timestamptz not null default now(),created_by uuid not null);
create table if not exists mobile.checklist_templates(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(160) not null,usage varchar(60) not null,required boolean not null,active boolean not null,created_at timestamptz not null default now(),created_by uuid not null);
create table if not exists mobile.checklist_questions(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),template_id uuid not null references mobile.checklist_templates(id),text varchar(500) not null,response_type varchar(20) not null check(response_type in('YES_NO','MULTIPLE','NUMBER','TEXT','DATE','PHOTO')),required boolean not null,options jsonb not null default '[]',position integer not null,unique(template_id,position));
create table if not exists mobile.checklist_runs(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),template_id uuid not null references mobile.checklist_templates(id),entity_type varchar(40) not null,entity_id uuid not null,responsible_id uuid not null,status varchar(20) not null,applied_at timestamptz not null,completed_at timestamptz,created_at timestamptz not null default now(),created_by uuid not null);
create table if not exists mobile.checklist_answers(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),run_id uuid not null references mobile.checklist_runs(id),question_id uuid not null references mobile.checklist_questions(id),value text,evidence_id uuid references mobile.evidences(id),answered_by uuid not null,answered_at timestamptz not null default now(),unique(run_id,question_id));
create table if not exists mobile.audit_events(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),entity_type varchar(60) not null,entity_id uuid not null,event_type varchar(80) not null,payload jsonb not null,occurred_at timestamptz not null default now(),user_id uuid not null);
create index if not exists ix_mobile_commands_pending on mobile.offline_commands(tenant_id,user_id,status,created_offline_at);
create index if not exists ix_mobile_timeline on mobile.geolocation_events(tenant_id,entity_type,entity_id,occurred_at desc);
do $$ declare tab text; begin foreach tab in array array['devices','sessions','sync_batches','offline_commands','sync_items','sync_errors','sync_conflicts','id_mappings','quick_records','evidences','geolocation_events','qr_codes','checklist_templates','checklist_questions','checklist_runs','checklist_answers','audit_events'] loop execute format('alter table mobile.%I enable row level security',tab); execute format('alter table mobile.%I force row level security',tab); if not exists(select 1 from pg_policies where schemaname='mobile' and tablename=tab and policyname=tab||'_tenant') then execute format('create policy %I on mobile.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',tab||'_tenant',tab); end if; end loop; end $$;
insert into platform.schema_versions(version,description,installed_at) values('0.9.0','Sprint 12 - Mobile Offline e PWA',now()) on conflict(version) do nothing;



-- Sprint 13 - Inteligencia Agro, BI, alertas e paineis executivos
create schema if not exists intelligence;
create table if not exists intelligence.alert_rules(
 id uuid primary key, tenant_id uuid references tenancy.tenants(id), type varchar(60) not null,
 name varchar(160) not null, severity varchar(12) not null check(severity in('LOW','MEDIUM','HIGH','CRITICAL')),
 parameters jsonb not null default '{}', enabled boolean not null default true, cooldown_minutes integer not null default 1440 check(cooldown_minutes>0),
 created_at timestamptz not null default now(), created_by uuid, unique(tenant_id,type));
create table if not exists intelligence.alerts(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), farm_id uuid, rule_id uuid references intelligence.alert_rules(id),
 type varchar(60) not null, severity varchar(12) not null, title varchar(240) not null, entity_type varchar(60), entity_id uuid,
 fingerprint varchar(128) not null, status varchar(12) not null default 'OPEN' check(status in('OPEN','SNOOZED','RESOLVED','IGNORED')),
 evidence jsonb not null default '{}', detected_at timestamptz not null default now(), snoozed_until timestamptz,
 resolved_by uuid, resolved_at timestamptz, resolution_reason varchar(500));
create unique index if not exists ux_intelligence_alert_dedup on intelligence.alerts(tenant_id,fingerprint) where status in('OPEN','SNOOZED');
create index if not exists ix_intelligence_alert_queue on intelligence.alerts(tenant_id,status,severity,detected_at desc);
create table if not exists intelligence.alert_audit(
 id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),alert_id uuid not null references intelligence.alerts(id),
 action varchar(12) not null,acted_by uuid not null,acted_at timestamptz not null,reason varchar(500));
create table if not exists intelligence.custom_dashboards(
 id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(120) not null,description varchar(500),
 shared_roles text[] not null default '{}',created_by uuid not null,created_at timestamptz not null default now(),updated_at timestamptz,
 unique(tenant_id,name));
create table if not exists intelligence.dashboard_widgets(
 id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),dashboard_id uuid not null references intelligence.custom_dashboards(id) on delete cascade,
 indicator_code varchar(80) not null,farm_id uuid,season_id uuid,position integer not null default 0,size varchar(10) not null check(size in('SMALL','MEDIUM','LARGE')),
 unique(dashboard_id,position));
create table if not exists intelligence.report_runs(
 id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),report_id varchar(80) not null,filters jsonb not null,
 requested_by uuid not null,requested_at timestamptz not null default now(),row_count integer,finished_at timestamptz);
do $$ declare tab text; begin foreach tab in array array['alert_rules','alerts','alert_audit','custom_dashboards','dashboard_widgets','report_runs'] loop
 execute format('alter table intelligence.%I enable row level security',tab); execute format('alter table intelligence.%I force row level security',tab);
 if not exists(select 1 from pg_policies where schemaname='intelligence' and tablename=tab and policyname=tab||'_tenant') then
  execute format('create policy %I on intelligence.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',tab||'_tenant',tab);
 end if; end loop; end $$;
insert into identity.permissions(code,module,description) values
 ('intelligence.read','Intelligence','Consultar BI, relatórios, alertas e previsões.'),
 ('intelligence.write','Intelligence','Administrar alertas e painéis personalizados.')
on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into intelligence.alert_rules(id,tenant_id,type,name,severity,parameters,created_by)
select gen_random_uuid(),t.id,x.type,x.name,x.severity,'{}',u.id from tenancy.tenants t
join lateral(select id from identity.users where tenant_id=t.id order by created_at limit 1)u on true
cross join(values
 ('LOW_STOCK','Estoque baixo','HIGH'),('EXPIRED_PRODUCT','Produto vencido','CRITICAL'),('EXPIRING_PRODUCT','Produto próximo do vencimento','HIGH'),
 ('LATE_ACTIVITY','Atividade agrícola atrasada','HIGH'),('CRITICAL_WEATHER','Aplicação em clima crítico','CRITICAL'),('OVERSTOCKED_PADDOCK','Piquete sobrelotado','HIGH'),
 ('ANIMAL_WITHDRAWAL','Animal em carência','HIGH'),('EXPIRED_VACCINE','Vacina vencida','CRITICAL'),('OVERDUE_MAINTENANCE','Manutenção vencida','HIGH'),
 ('OVERDUE_PAYABLE','Conta a pagar vencida','CRITICAL'),('OVERDUE_RECEIVABLE','Conta a receber vencida','HIGH'),('STOPPED_RECEIPT','Romaneio parado','HIGH'),
 ('NONCONFORMING_LOT','Lote sem conformidade','CRITICAL'),('INVALID_LEDGER','Ledger inválido','CRITICAL'),('LATE_SHIPMENT','Expedição atrasada','HIGH'),
 ('CRITICAL_ROUTE','Viagem em rota crítica','CRITICAL'),('PENDING_SPLIT','Split pendente','HIGH'),('MOBILE_SYNC_ERROR','Erro de sincronização mobile','HIGH'))x(type,name,severity)
on conflict(tenant_id,type) do nothing;
insert into platform.schema_versions(version,description,installed_at) values('1.0.0','Sprint 13 - Inteligencia Agro e BI',now()) on conflict(version) do nothing;

commit;



-- Sprint 26 - Agro360 Campo, fila offline auditavel e checklists inteligentes
begin;
create schema if not exists field_operations;

create table if not exists field_operations.occurrences(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), user_id uuid not null,
 occurrence_type varchar(40) not null, severity varchar(12) not null check(severity in('LOW','MEDIUM','HIGH','CRITICAL')),
 title varchar(160) not null, description varchar(2000) not null, entity_type varchar(40), entity_id uuid,
 latitude numeric(10,7), longitude numeric(10,7), occurred_at timestamptz not null,
 status varchar(20) not null default 'OPEN' check(status in('OPEN','IN_PROGRESS','RESOLVED','CANCELLED')),
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, deleted_at timestamptz,
 unique(tenant_id,id), check((latitude is null)=(longitude is null)), check(latitude is null or latitude between -90 and 90), check(longitude is null or longitude between -180 and 180),
 foreign key(tenant_id,user_id) references identity.users(tenant_id,id));

create table if not exists field_operations.checkins(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), user_id uuid not null,
 operation_type varchar(40) not null, entity_type varchar(40), entity_id uuid, occurred_at timestamptz not null,
 latitude numeric(10,7), longitude numeric(10,7), accuracy numeric(10,2), location_source varchar(12) not null check(location_source in('GPS','MANUAL')),
 manual_reason varchar(500), observation varchar(1000), evidence_id uuid,
 status varchar(20) not null default 'REGISTERED' check(status in('REGISTERED','VALIDATED','REJECTED','CANCELLED')),
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, deleted_at timestamptz,
 unique(tenant_id,id), check(latitude is null or latitude between -90 and 90), check(longitude is null or longitude between -180 and 180),
 check((latitude is null)=(longitude is null)), check(accuracy is null or accuracy>=0), check(location_source<>'MANUAL' or length(trim(coalesce(manual_reason,'')))>=5),
 foreign key(tenant_id,user_id) references identity.users(tenant_id,id));

create table if not exists field_operations.visit_logs(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), user_id uuid not null, visit_type varchar(40) not null,
 entity_type varchar(40) not null, entity_id uuid not null, started_at timestamptz not null, ended_at timestamptz, notes varchar(2000), outcome varchar(500),
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, deleted_at timestamptz,
 unique(tenant_id,id), check(ended_at is null or ended_at>=started_at), foreign key(tenant_id,user_id) references identity.users(tenant_id,id));

create table if not exists field_operations.media_uploads(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), user_id uuid not null, entity_type varchar(40) not null, entity_id uuid not null,
 original_file_name varchar(240) not null, storage_key varchar(500) not null, content_type varchar(100) not null, size_bytes bigint not null check(size_bytes between 1 and 10485760),
 sha256 char(64) not null, description varchar(1000), tags text[] not null default '{}', latitude numeric(10,7), longitude numeric(10,7), captured_at timestamptz not null,
 status varchar(20) not null check(status in('PENDING','UPLOADED','FAILED','REJECTED')),
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, deleted_at timestamptz,
 unique(tenant_id,id), unique(tenant_id,sha256,entity_type,entity_id), check((latitude is null)=(longitude is null)), check(latitude is null or latitude between -90 and 90), check(longitude is null or longitude between -180 and 180),
 foreign key(tenant_id,user_id) references identity.users(tenant_id,id));

alter table mobile.devices add column if not exists created_at timestamptz not null default now();
alter table mobile.devices add column if not exists updated_at timestamptz not null default now();
alter table mobile.devices add column if not exists created_by uuid;
alter table mobile.devices add column if not exists updated_by uuid;
alter table mobile.devices add column if not exists deleted_at timestamptz;
alter table mobile.offline_commands add column if not exists attempts int not null default 0 check(attempts>=0);
alter table mobile.offline_commands add column if not exists error_message varchar(1000);
alter table mobile.offline_commands add column if not exists received_at timestamptz;
alter table mobile.offline_commands add column if not exists updated_at timestamptz not null default now();
alter table mobile.offline_commands drop constraint if exists offline_commands_status_check;
alter table mobile.offline_commands add constraint offline_commands_status_check check(status in('PENDING','SYNCING','SYNCED','FAILED','CONFLICT','CANCELLED'));
create unique index if not exists ux_mobile_idempotency_operation on mobile.offline_commands(tenant_id,command_type,idempotency_key);

create table if not exists mobile.operation_logs(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), user_id uuid not null, command_id uuid,
 operation_type varchar(80) not null, status varchar(20) not null check(status in('PENDING','SYNCING','SYNCED','FAILED','CONFLICT','CANCELLED')),
 safe_details jsonb not null default '{}', occurred_at timestamptz not null default now(), created_at timestamptz not null default now(), created_by uuid not null,
 foreign key(tenant_id,user_id) references identity.users(tenant_id,id));
create table if not exists mobile.user_preferences(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), user_id uuid not null, profile varchar(40) not null,
 quick_actions text[] not null default '{}', preferences jsonb not null default '{}', created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, deleted_at timestamptz,
 unique(tenant_id,id), unique(tenant_id,user_id), foreign key(tenant_id,user_id) references identity.users(tenant_id,id));
create table if not exists mobile.pwa_install_events(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), user_id uuid not null, device_id uuid,
 event_type varchar(20) not null check(event_type in('PROMPTED','ACCEPTED','DISMISSED','INSTALLED')), platform varchar(80), occurred_at timestamptz not null default now(),
 created_at timestamptz not null default now(), created_by uuid not null, foreign key(tenant_id,user_id) references identity.users(tenant_id,id));

alter table mobile.checklist_templates add column if not exists checklist_type varchar(40) not null default 'PROPERTY_INSPECTION';
alter table mobile.checklist_templates add column if not exists updated_at timestamptz not null default now();
alter table mobile.checklist_templates add column if not exists updated_by uuid;
alter table mobile.checklist_templates add column if not exists deleted_at timestamptz;
alter table mobile.checklist_questions add column if not exists evidence_required boolean not null default false;
alter table mobile.checklist_questions add column if not exists weight numeric(8,2) not null default 1 check(weight>=0);
alter table mobile.checklist_questions add column if not exists criticality varchar(12) not null default 'MEDIUM' check(criticality in('LOW','MEDIUM','HIGH','CRITICAL'));
alter table mobile.checklist_runs add column if not exists rejection_reason varchar(1000);
alter table mobile.checklist_runs add column if not exists updated_at timestamptz not null default now();
alter table mobile.checklist_runs add column if not exists updated_by uuid;
alter table mobile.checklist_answers add column if not exists updated_at timestamptz not null default now();

create index if not exists ix_field_occurrences_queue on field_operations.occurrences(tenant_id,user_id,status,occurred_at desc) where deleted_at is null;
create index if not exists ix_field_occurrences_type on field_operations.occurrences(tenant_id,occurrence_type,severity,created_at desc) where deleted_at is null;
create index if not exists ix_field_checkins_queue on field_operations.checkins(tenant_id,user_id,status,occurred_at desc) where deleted_at is null;
create index if not exists ix_field_checkins_operation on field_operations.checkins(tenant_id,operation_type,occurred_at desc) where deleted_at is null;
create index if not exists ix_field_visits_user_date on field_operations.visit_logs(tenant_id,user_id,started_at desc) where deleted_at is null;
create index if not exists ix_field_media_status on field_operations.media_uploads(tenant_id,user_id,status,created_at desc) where deleted_at is null;
create index if not exists ix_mobile_sync_status on mobile.offline_commands(tenant_id,user_id,status,created_offline_at desc);
create index if not exists ix_mobile_sync_type on mobile.offline_commands(tenant_id,command_type,created_offline_at desc);
create index if not exists ix_mobile_operation_logs on mobile.operation_logs(tenant_id,user_id,status,occurred_at desc);

do $$ declare item record; begin
 for item in select * from (values ('field_operations','occurrences'),('field_operations','checkins'),('field_operations','visit_logs'),('field_operations','media_uploads'),('mobile','operation_logs'),('mobile','user_preferences'),('mobile','pwa_install_events')) as scoped(schema_name,table_name) loop
  execute format('alter table %I.%I enable row level security',item.schema_name,item.table_name);
  execute format('drop policy if exists tenant_isolation on %I.%I',item.schema_name,item.table_name);
  execute format('create policy tenant_isolation on %I.%I using (tenant_id=platform.current_tenant_id()) with check (tenant_id=platform.current_tenant_id())',item.schema_name,item.table_name);
 end loop;
end $$;
insert into identity.permissions(code,module,description) values
 ('mobile.read','Agro360 Campo','Consultar painel e operações de campo.'),('mobile.write','Agro360 Campo','Registrar ocorrências, check-ins, visitas e evidências.'),
 ('mobile.sync','Agro360 Campo','Sincronizar e reenviar operações offline.'),('mobile.conflicts.resolve','Agro360 Campo','Resolver conflitos de sincronização.'),
 ('field-checklists.manage','Checklists de Campo','Criar e administrar modelos de checklist.')
on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('2.3.0','Sprint 26 - Agro360 Campo, Offline Sync e Checklists',now()) on conflict(version) do nothing;
commit;

-- Sprint 14 - governança SaaS B2B (instalação autocontida)
create schema if not exists saas;
create schema if not exists audit;
create table if not exists saas.plans(id uuid primary key default gen_random_uuid(),name varchar(100) not null unique,description varchar(500) not null,monthly_price numeric(14,2) not null check(monthly_price>=0),annual_price numeric(14,2) not null check(annual_price>=0),user_limit int not null check(user_limit>0),property_limit int not null check(property_limit>0),storage_limit_mb bigint not null check(storage_limit_mb>0),device_limit int not null check(device_limit>0),modules varchar[] not null,premium_features varchar[] not null default '{}',active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz);
insert into saas.plans(name,description,monthly_price,annual_price,user_limit,property_limit,storage_limit_mb,device_limit,modules,premium_features) values
('Essencial','Gestão essencial para o produtor',199,1990,5,2,5120,3,array['properties','agriculture','inventory'],array[]::varchar[]),('Profissional','Operação rural integrada',499,4990,20,10,51200,15,array['properties','agriculture','livestock','inventory','finance','reports'],array['offline']),('Cooperativa','Gestão de cooperados e logística',1290,12900,100,100,204800,80,array['properties','agriculture','inventory','finance','logistics','traceability','reports'],array['offline','bi']),('Agroindústria','Originação, indústria e rastreabilidade',1890,18900,150,50,512000,120,array['properties','inventory','finance','logistics','traceability','reports'],array['offline','bi','ledger']),('Enterprise','Limites e módulos ampliados',0,0,1000,1000,2097152,1000,array['properties','agriculture','livestock','inventory','finance','logistics','traceability','reports','intelligence'],array['offline','bi','ledger','support']) on conflict(name) do nothing;
create table if not exists saas.organizations(tenant_id uuid primary key references tenancy.tenants(id),organization_type varchar(30) not null check(organization_type in('PRODUCER','COOPERATIVE','AGRIBUSINESS','CONSULTANCY','DISTRIBUTOR','CARRIER','OTHER')),document varchar(14) not null unique,responsible_name varchar(160) not null,responsible_email varchar(254) not null,plan_id uuid not null references saas.plans(id),status varchar(20) not null default 'IMPLEMENTING' check(status in('IMPLEMENTING','ACTIVE','SUSPENDED','BLOCKED','CANCELLED')),activated_at timestamptz,blocked_at timestamptz,block_reason varchar(500),onboarding_status varchar(30) not null default 'ORGANIZATION',created_at timestamptz not null default now(),updated_at timestamptz,check(status<>'BLOCKED' or (blocked_at is not null and length(trim(block_reason))>0)));
create table if not exists saas.usage_metrics(tenant_id uuid primary key references tenancy.tenants(id),storage_used_mb bigint not null default 0,tracked_lots bigint not null default 0,certificates bigint not null default 0,offline_records bigint not null default 0,ledger_events bigint not null default 0,exported_reports bigint not null default 0,measured_at timestamptz not null default now());
create table if not exists saas.role_metadata(tenant_id uuid not null,role_id uuid not null,level int not null check(level between 1 and 100),primary key(tenant_id,role_id),foreign key(tenant_id,role_id) references identity.roles(tenant_id,id));
create table if not exists saas.invitations(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),email varchar(254) not null,role_id uuid not null,token_hash char(64) not null unique,status varchar(20) not null default 'PENDING' check(status in('PENDING','ACCEPTED','CANCELLED')),expires_at timestamptz not null,invited_by uuid not null,created_at timestamptz not null default now(),foreign key(tenant_id,role_id) references identity.roles(tenant_id,id));
create unique index if not exists ux_saas_pending_invitation on saas.invitations(tenant_id,lower(email)) where status='PENDING';
create table if not exists saas.sessions(id uuid primary key,tenant_id uuid not null,user_id uuid not null,device varchar(160) not null,ip_address inet not null,created_at timestamptz not null default now(),last_seen_at timestamptz not null default now(),revoked_at timestamptz,revoked_by uuid,foreign key(tenant_id,user_id) references identity.users(tenant_id,id));
create table if not exists saas.devices(id uuid primary key,tenant_id uuid not null,user_id uuid not null,name varchar(160) not null,platform varchar(80) not null,last_seen_at timestamptz not null default now(),revoked_at timestamptz,revoked_by uuid,foreign key(tenant_id,user_id) references identity.users(tenant_id,id));
create table if not exists saas.login_history(id uuid primary key default gen_random_uuid(),tenant_id uuid,user_id uuid,email varchar(254) not null,ip_address inet,success boolean not null,failure_reason varchar(160),occurred_at timestamptz not null default now());
create table if not exists saas.notifications(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),user_id uuid,type varchar(30) not null check(type in('SYSTEM','FINANCE','INVENTORY','AGRICULTURE','LIVESTOCK','LOGISTICS','TRACEABILITY','SECURITY','ONBOARDING','SUBSCRIPTION')),priority varchar(10) not null check(priority in('LOW','NORMAL','HIGH','CRITICAL')),title varchar(160) not null,message varchar(1000) not null,route varchar(300),requires_action boolean not null default false,created_at timestamptz not null default now(),read_at timestamptz,archived_at timestamptz);
create table if not exists saas.requests(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),type varchar(20) not null check(type in('UPGRADE','SUPPORT')),requested_plan_id uuid references saas.plans(id),reason varchar(1000) not null,requested_by uuid not null,status varchar(20) not null default 'OPEN',created_at timestamptz not null default now());
create table if not exists saas.organization_settings(tenant_id uuid primary key references tenancy.tenants(id),unit_system varchar(20) not null default 'METRIC',currency char(3) not null default 'BRL',time_zone varchar(80) not null default 'America/Sao_Paulo',main_culture varchar(100) not null,main_activities varchar[] not null,stock_parameters jsonb not null default '{}',finance_parameters jsonb not null default '{}',traceability_parameters jsonb not null default '{}',compliance_parameters jsonb not null default '{}',notification_preferences varchar[] not null default '{}',updated_at timestamptz,updated_by uuid);
create table if not exists audit.saas_events(id uuid primary key,tenant_id uuid,actor_id uuid,event_type varchar(80) not null,details jsonb not null default '{}',occurred_at timestamptz not null default now());
create index if not exists ix_saas_audit_tenant_time on audit.saas_events(tenant_id,occurred_at desc);

-- Sprint 15 - Compliance Agro, ESG, carbono e exportacao
begin;
create schema if not exists compliance;
create schema if not exists esg;
create table if not exists compliance.subjects(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),type varchar(30) not null,name varchar(180) not null,external_reference varchar(100),created_at timestamptz not null default now(),unique(tenant_id,id));
create table if not exists compliance.buyers(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(180) not null,market varchar(80) not null,document varchar(40),active boolean not null default true,unique(tenant_id,id));
create table if not exists compliance.documents(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),type varchar(40) not null,number varchar(80) not null,subject_id uuid not null references compliance.subjects(id),issued_on date not null,expires_on date not null,responsible_id uuid not null references identity.users(id),status varchar(16) not null default 'VALID' check(status in('DRAFT','VALID','SUSPENDED','REVOKED')),notes varchar(500),decision_reason varchar(500),decided_by uuid,decided_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz,check(expires_on>=issued_on),unique(tenant_id,type,number));
create table if not exists compliance.document_history(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,document_id uuid not null references compliance.documents(id),event varchar(40) not null,details jsonb not null default '{}',actor_id uuid not null,occurred_at timestamptz not null default now());
create table if not exists compliance.product_rules(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),product_id uuid not null references inventory.products(id),market varchar(80) not null,name varchar(160) not null,mandatory boolean not null default true,blocks_sale boolean not null default true,blocks_export boolean not null default true,required_documents text[] not null default '{}',status varchar(16) not null default 'ACTIVE',created_at timestamptz not null default now(),updated_at timestamptz,unique(tenant_id,product_id,market,name));
create table if not exists compliance.lot_requirements(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,lot_id uuid not null references storage.lots(id),rule_id uuid not null references compliance.product_rules(id),compliant boolean not null,evaluated_at timestamptz not null default now(),evidence jsonb not null default '{}',unique(tenant_id,lot_id,rule_id));
create table if not exists compliance.lot_decisions(id uuid primary key,tenant_id uuid not null,lot_id uuid not null references storage.lots(id),blocked boolean not null,reason varchar(500) not null,decided_by uuid not null,decided_at timestamptz not null default now());
create table if not exists compliance.certifications(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),type varchar(100) not null,holder_id uuid not null references compliance.subjects(id),scope varchar(300) not null,valid_from date not null,valid_until date not null,requirements text[] not null default '{}',status varchar(16) not null default 'PENDING' check(status in('PENDING','APPROVED','REJECTED','SUSPENDED','REVOKED')),decision_reason varchar(500),decided_by uuid,decided_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz,check(valid_until>=valid_from));
create table if not exists compliance.audit_templates(id uuid primary key,tenant_id uuid not null,name varchar(160) not null,scope varchar(40) not null,active boolean not null default true,created_at timestamptz not null default now());
create table if not exists compliance.audit_questions(id uuid primary key,tenant_id uuid not null,template_id uuid not null references compliance.audit_templates(id) on delete cascade,question varchar(500) not null,weight numeric(8,2) not null check(weight>0),mandatory boolean not null default true,position int not null);
create table if not exists compliance.audits(id uuid primary key,tenant_id uuid not null,scope varchar(40) not null,entity_id uuid not null references compliance.subjects(id),template_id uuid not null references compliance.audit_templates(id),scheduled_on date not null,responsible_id uuid not null references identity.users(id),status varchar(16) not null default 'PLANNED' check(status in('PLANNED','IN_PROGRESS','COMPLETED','CANCELLED')),score numeric(6,2) not null default 0,completed_at timestamptz,decision_reason varchar(500),decided_by uuid,decided_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz);
create table if not exists compliance.audit_answers(id uuid primary key,tenant_id uuid not null,audit_id uuid not null references compliance.audits(id),question_id uuid not null references compliance.audit_questions(id),compliant boolean not null,answer varchar(1000),unique(audit_id,question_id));
create table if not exists compliance.non_conformities(id uuid primary key,tenant_id uuid not null,title varchar(180) not null,classification varchar(30) not null,severity varchar(12) not null check(severity in('LOW','MEDIUM','HIGH','CRITICAL')),origin varchar(40) not null,entity_type varchar(40) not null,entity_id uuid not null references compliance.subjects(id),audit_id uuid references compliance.audits(id),root_cause varchar(1000) not null,corrective_action varchar(1000) not null,preventive_action varchar(1000),responsible_id uuid not null references identity.users(id),due_on date not null,status varchar(16) not null default 'OPEN' check(status in('OPEN','IN_PROGRESS','PENDING_APPROVAL','CLOSED','CANCELLED')),decision_reason varchar(500),decided_by uuid,decided_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz);
create table if not exists compliance.evidence(id uuid primary key,tenant_id uuid not null,entity_type varchar(40) not null,entity_id uuid not null,file_name varchar(240) not null,storage_url varchar(1000) not null,sha256 char(64) not null,uploaded_by uuid not null,uploaded_at timestamptz not null default now());
create table if not exists esg.indicators(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),pillar varchar(20) not null check(pillar in('ENVIRONMENTAL','SOCIAL','GOVERNANCE')),name varchar(160) not null,value numeric(18,4) not null check(value>=0),unit varchar(30) not null,period date not null,methodology varchar(1000) not null,recorded_by uuid not null,created_at timestamptz not null default now());
create table if not exists esg.carbon_inventory(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),source varchar(100) not null,practice varchar(100) not null check(practice in('ILPF','PASTURE_RECOVERY','REFORESTATION','BIOINPUTS','FERTILIZER_REDUCTION','WASTE_MANAGEMENT','LOGISTICS_EFFICIENCY','OTHER')),activity_amount numeric(18,4) not null check(activity_amount>=0),unit varchar(30) not null,factor_kg_co2e numeric(18,6) not null check(factor_kg_co2e>=0),sequestration_kg_co2e numeric(18,4) not null default 0 check(sequestration_kg_co2e>=0),period date not null,methodology varchar(1000) not null,recorded_by uuid not null,created_at timestamptz not null default now());
create table if not exists compliance.export_dossiers(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),lot_id uuid not null references storage.lots(id),buyer_id uuid not null references compliance.buyers(id),market varchar(80) not null,certificate_code char(20) not null unique,public_token_hash char(64) not null unique,status varchar(16) not null default 'ISSUED' check(status in('DRAFT','ISSUED','REVOKED')),generated_by uuid not null,generated_at timestamptz not null default now());
create index if not exists ix_compliance_documents_expiry on compliance.documents(tenant_id,expires_on);
create index if not exists ix_compliance_nc_queue on compliance.non_conformities(tenant_id,status,severity,due_on);
create index if not exists ix_compliance_audits_queue on compliance.audits(tenant_id,status,scheduled_on);
create index if not exists ix_esg_indicator_period on esg.indicators(tenant_id,period desc,pillar);
do $$ declare sch text; tab text; begin foreach sch in array array['compliance','esg'] loop for tab in select tablename from pg_tables where schemaname=sch loop execute format('alter table %I.%I enable row level security',sch,tab); execute format('alter table %I.%I force row level security',sch,tab); if not exists(select 1 from pg_policies where schemaname=sch and tablename=tab and policyname=tab||'_tenant') then execute format('create policy %I on %I.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',tab||'_tenant',sch,tab); end if; end loop; end loop; end $$;
insert into identity.permissions(code,module,description) values ('compliance.read','Compliance','Consultar compliance, auditoria e exportação.'),('compliance.write','Compliance','Gerenciar compliance e evidências.'),('compliance.approve','Compliance','Aprovar, bloquear e encerrar controles.'),('esg.read','ESG','Consultar indicadores ESG e carbono.'),('esg.write','ESG','Registrar indicadores ESG e inventário de carbono.') on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('1.2.0','Sprint 15 - Compliance Agro, ESG, Carbono e Exportacao',now()) on conflict(version) do nothing;
commit;

-- Consulta publica minimizada: SECURITY DEFINER ignora o contexto de tenant sem expor tabelas.
create or replace function compliance.verify_public_dossier(p_certificate text)
returns table(certificate_code text,product text,lot text,origin text,status text,issued_at timestamptz,verification_hash text)
language sql security definer stable set search_path=pg_catalog,public,compliance,storage,inventory as $$
 select d.certificate_code::text,p.name::text,l.code::text,coalesce(l.origin,'Origem verificada')::text,d.status::text,d.generated_at,
 encode(digest(d.certificate_code||d.id::text,'sha256'),'hex')
 from compliance.export_dossiers d join storage.lots l on l.id=d.lot_id join inventory.products p on p.id=l.product_id
 where d.certificate_code=upper(p_certificate) and d.status='ISSUED' limit 1
$$;
revoke all on function compliance.verify_public_dossier(text) from public;
grant execute on function compliance.verify_public_dossier(text) to public;

-- Sprint 16 - integrações, interoperabilidade e conectores de produção
begin;
create schema if not exists integrations;
create table if not exists integrations.credential_references(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(120) not null,provider varchar(60) not null,secret_reference varchar(500) not null,created_at timestamptz not null default now(),unique(tenant_id,id),unique(tenant_id,name));
create table if not exists integrations.integrations(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(120) not null,type varchar(40) not null,provider varchar(60) not null,status varchar(30) not null default 'AWAITING_CONFIGURATION' check(status in('AWAITING_CONFIGURATION','ACTIVE','PAUSED','ERROR')),credential_reference_id uuid,last_sync timestamptz,last_error varchar(1000),attempts int not null default 0,created_at timestamptz not null default now(),updated_at timestamptz,unique(tenant_id,id),unique(tenant_id,name),foreign key(tenant_id,credential_reference_id) references integrations.credential_references(tenant_id,id));
create table if not exists integrations.integration_logs(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,integration_id uuid not null,event varchar(80) not null,status varchar(30) not null,error varchar(1000),attempt int not null default 0,occurred_at timestamptz not null default now(),foreign key(tenant_id,integration_id) references integrations.integrations(tenant_id,id));
create table if not exists integrations.api_keys(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(120) not null,key_prefix varchar(20) not null,key_hash char(64) not null unique,scopes varchar[] not null,status varchar(20) not null default 'ACTIVE' check(status in('ACTIVE','REVOKED','EXPIRED')),expires_at timestamptz,last_used_at timestamptz,rate_limit_per_minute int not null check(rate_limit_per_minute between 1 and 10000),created_at timestamptz not null default now(),unique(tenant_id,id));
create table if not exists integrations.api_key_usage(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,api_key_id uuid not null,window_started_at timestamptz not null,request_count int not null default 1,unique(api_key_id,window_started_at),foreign key(tenant_id,api_key_id) references integrations.api_keys(tenant_id,id));
create table if not exists integrations.webhooks(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),url varchar(1000) not null,events varchar[] not null,status varchar(30) not null,signing_credential_reference_id uuid,created_at timestamptz not null default now(),unique(tenant_id,id),foreign key(tenant_id,signing_credential_reference_id) references integrations.credential_references(tenant_id,id));
create table if not exists integrations.webhook_events(id uuid primary key,tenant_id uuid not null,event_type varchar(80) not null,payload jsonb not null,signature varchar(128) not null,status varchar(20) not null default 'PENDING',attempts int not null default 0,next_attempt_at timestamptz,last_error varchar(1000),processing_ms numeric(12,2),created_at timestamptz not null default now(),sent_at timestamptz,unique(tenant_id,id));
create table if not exists integrations.webhook_deliveries(id uuid primary key,tenant_id uuid not null,webhook_id uuid not null,event_id uuid not null,http_status int,response_excerpt varchar(1000),attempted_at timestamptz not null default now(),foreign key(tenant_id,webhook_id) references integrations.webhooks(tenant_id,id),foreign key(tenant_id,event_id) references integrations.webhook_events(tenant_id,id));
create table if not exists integrations.imports(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),entity_type varchar(30) not null,file_name varchar(240) not null,csv_content text not null,status varchar(20) not null,total_rows int not null default 0,valid_rows int not null default 0,error_rows int not null default 0,created_at timestamptz not null default now(),validated_at timestamptz,confirmed_at timestamptz,confirmed_by uuid,unique(tenant_id,id));
create table if not exists integrations.import_errors(id uuid primary key,tenant_id uuid not null,import_id uuid not null,line_number int not null,message varchar(1000) not null,foreign key(tenant_id,import_id) references integrations.imports(tenant_id,id) on delete cascade);
create table if not exists integrations.fiscal_documents(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),type varchar(30) not null,file_name varchar(240) not null,sha256 char(64) not null,status varchar(30) not null default 'METADATA_RECEIVED',purchase_id uuid,sale_id uuid,shipment_id uuid,lot_id uuid,producer_id uuid,supplier_id uuid,uploaded_by uuid not null,uploaded_at timestamptz not null default now(),unique(tenant_id,id),unique(tenant_id,sha256));
create table if not exists integrations.iot_devices(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(120) not null,type varchar(40) not null,sensor_type varchar(40) not null,token_hash char(64) not null unique,status varchar(20) not null default 'ACTIVE',minimum numeric(18,6),maximum numeric(18,6),last_seen_at timestamptz,created_at timestamptz not null default now(),unique(tenant_id,id),check(minimum is null or maximum is null or minimum<=maximum));
create table if not exists integrations.iot_readings(id uuid primary key,tenant_id uuid not null,device_id uuid not null,sensor_type varchar(40) not null,value numeric(18,6) not null,unit varchar(20) not null,recorded_at timestamptz not null,latitude numeric(10,7),longitude numeric(10,7),critical boolean not null default false,foreign key(tenant_id,device_id) references integrations.iot_devices(tenant_id,id));
create table if not exists integrations.iot_alerts(id uuid primary key,tenant_id uuid not null,device_id uuid not null,reading_id uuid not null,severity varchar(12) not null,message varchar(500) not null,created_at timestamptz not null default now(),resolved_at timestamptz,foreign key(tenant_id,device_id) references integrations.iot_devices(tenant_id,id),foreign key(tenant_id,reading_id) references integrations.iot_readings(tenant_id,id));
create table if not exists integrations.payment_splits(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),sale_id uuid not null,gross_amount numeric(18,2) not null check(gross_amount>0),status varchar(20) not null,provider varchar(40) not null,created_by uuid not null,created_at timestamptz not null default now(),approved_by uuid,approved_at timestamptz,unique(tenant_id,id));
create table if not exists integrations.split_participants(id uuid primary key,tenant_id uuid not null,split_id uuid not null,party_id uuid not null,percentage numeric(7,4) not null check(percentage>0 and percentage<=100),amount numeric(18,2) not null check(amount>=0),foreign key(tenant_id,split_id) references integrations.payment_splits(tenant_id,id));
create table if not exists integrations.message_outbox(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),channel varchar(20) not null,recipient_id uuid not null,subject varchar(160) not null,body varchar(2000) not null,status varchar(20) not null,provider varchar(40) not null,attempts int not null default 0,last_error varchar(1000),created_at timestamptz not null default now(),sent_at timestamptz,unique(tenant_id,id));
create table if not exists integrations.audit_events(id uuid primary key,tenant_id uuid not null,actor_id uuid,event_type varchar(80) not null,entity_id uuid not null,details jsonb not null default '{}',occurred_at timestamptz not null default now());
create index if not exists ix_webhook_queue on integrations.webhook_events(tenant_id,status,next_attempt_at);
create index if not exists ix_iot_readings_history on integrations.iot_readings(tenant_id,device_id,recorded_at desc);
create index if not exists ix_message_outbox_queue on integrations.message_outbox(tenant_id,status,created_at);
do $$ declare tab text; begin for tab in select tablename from pg_tables where schemaname='integrations' loop execute format('alter table integrations.%I enable row level security',tab); execute format('alter table integrations.%I force row level security',tab); if not exists(select 1 from pg_policies where schemaname='integrations' and tablename=tab and policyname=tab||'_tenant') then execute format('create policy %I on integrations.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',tab||'_tenant',tab); end if; end loop; end $$;
insert into identity.permissions(code,module,description) values('integrations.read','Integrações','Consultar integrações, IoT e interoperabilidade.'),('integrations.write','Integrações','Administrar integrações, chaves, filas e split manual.') on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('1.3.0','Sprint 16 - Integracoes e Interoperabilidade',now()) on conflict(version) do nothing;
commit;

-- Sprint 17 - Mapa Agro e camada geoespacial (PostgreSQL puro, extensões espaciais opcionais)
begin;
create schema if not exists geospatial;
create table if not exists geospatial.features(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), entity_type varchar(30) not null check(entity_type in('PROPERTY','FIELD','PASTURE','PADDOCK','WAREHOUSE','ROUTE','LOGISTICS_POINT','OCCURRENCE','MANAGEMENT_ZONE','ENVIRONMENTAL_AREA')),
 name varchar(180) not null, geometry_type varchar(20) not null check(geometry_type in('Point','LineString','Polygon','MultiPolygon')), geojson jsonb not null,
 centroid_latitude numeric(10,7), centroid_longitude numeric(10,7), bounding_box jsonb, informed_area_ha numeric(18,4) check(informed_area_ha is null or informed_area_ha>0), calculated_area_ha numeric(18,4) check(calculated_area_ha is null or calculated_area_ha>=0),
 property_id uuid, parent_id uuid, status varchar(16) not null default 'ACTIVE' check(status in('DRAFT','ACTIVE','INACTIVE','BLOCKED','RESOLVED')), origin varchar(40) not null,
 created_by uuid not null, updated_by uuid not null, created_at timestamptz not null default now(), updated_at timestamptz not null default now(), unique(tenant_id,id),
 check(geojson ? 'type' and geojson ? 'coordinates'), check(centroid_latitude is null or centroid_latitude between -90 and 90), check(centroid_longitude is null or centroid_longitude between -180 and 180),
 foreign key(tenant_id,property_id) references geo.farms(tenant_id,id), foreign key(tenant_id,parent_id) references geospatial.features(tenant_id,id));
create table if not exists geospatial.occurrences(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),feature_id uuid not null,type varchar(40) not null,severity varchar(12) not null check(severity in('LOW','MEDIUM','HIGH','CRITICAL')),responsible_id uuid not null,status varchar(16) not null default 'OPEN' check(status in('OPEN','IN_PROGRESS','RESOLVED','CLOSED')),notes varchar(1000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),unique(tenant_id,id),foreign key(tenant_id,feature_id) references geospatial.features(tenant_id,id),foreign key(tenant_id,responsible_id) references identity.users(tenant_id,id));
create table if not exists geospatial.occurrence_evidence(id uuid primary key,tenant_id uuid not null,occurrence_id uuid not null,file_name varchar(240) not null,storage_url varchar(1000) not null,sha256 char(64) not null,uploaded_by uuid not null,created_at timestamptz not null default now(),foreign key(tenant_id,occurrence_id) references geospatial.occurrences(tenant_id,id));
create table if not exists geospatial.route_segments(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),route_feature_id uuid not null,type varchar(20) not null check(type in('RIVER','RURAL_ROAD','FERRY','HIGHWAY')),name varchar(180) not null,geojson jsonb not null,distance_km numeric(12,3) not null check(distance_km>0),estimated_minutes int not null check(estimated_minutes>0),status varchar(16) not null check(status in('ACTIVE','RESTRICTED','BLOCKED')),restrictions varchar(500),operational_window varchar(200),authorized_override boolean not null default false,created_by uuid not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),unique(tenant_id,id),foreign key(tenant_id,route_feature_id) references geospatial.features(tenant_id,id),check(geojson->>'type'='LineString'));
create table if not exists geospatial.management_zone_links(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,zone_feature_id uuid not null,link_type varchar(30) not null check(link_type in('ACTIVITY','RECOMMENDATION','OCCURRENCE','COST')),linked_id uuid not null,notes varchar(500),created_at timestamptz not null default now(),foreign key(tenant_id,zone_feature_id) references geospatial.features(tenant_id,id));
create table if not exists geospatial.imports(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),entity_type varchar(30) not null,file_name varchar(240) not null,payload jsonb not null,status varchar(16) not null check(status in('VALIDATED','INVALID','IMPORTED')),total_features int not null,valid_features int not null,error_features int not null,created_by uuid not null,created_at timestamptz not null default now(),unique(tenant_id,id));
create table if not exists geospatial.import_errors(id uuid primary key,tenant_id uuid not null,import_id uuid not null,feature_number int not null,message varchar(1000) not null,foreign key(tenant_id,import_id) references geospatial.imports(tenant_id,id) on delete cascade);
create table if not exists geospatial.feature_audit(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,feature_id uuid,event varchar(40) not null,actor_id uuid not null,details jsonb not null default '{}',occurred_at timestamptz not null default now());
create index if not exists ix_geo_features_map on geospatial.features(tenant_id,entity_type,status);
create index if not exists ix_geo_occurrences_queue on geospatial.occurrences(tenant_id,status,severity);
create index if not exists ix_geo_segments_route on geospatial.route_segments(tenant_id,route_feature_id,status);
do $$ declare tab text; begin for tab in select tablename from pg_tables where schemaname='geospatial' loop execute format('alter table geospatial.%I enable row level security',tab); execute format('alter table geospatial.%I force row level security',tab); if not exists(select 1 from pg_policies where schemaname='geospatial' and tablename=tab and policyname=tab||'_tenant') then execute format('create policy %I on geospatial.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',tab||'_tenant',tab); end if; end loop; end $$;
insert into identity.permissions(code,module,description) values('maps.read','Mapa Agro','Consultar mapas e dados territoriais.'),('maps.write','Mapa Agro','Editar geometrias, ocorrências, zonas e rotas.') on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('1.4.0','Sprint 17 - Mapa Agro e Geoespacial',now()) on conflict(version) do nothing;
commit;

-- Sprint 18 - Cooperativas, assistência técnica e marketplace B2B
begin;
create schema if not exists cooperative;
create table if not exists cooperative.member_classifications(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(120) not null,score int not null default 0,description varchar(500),active boolean not null default true,unique(tenant_id,id),unique(tenant_id,name));
create table if not exists cooperative.members(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(180) not null,document varchar(14) not null,organization_id uuid not null,classification_id uuid not null,property_id uuid not null,productive_profile varchar(2000),productive_capacity jsonb not null default '{}',cultures jsonb not null default '[]',herds jsonb not null default '[]',documents jsonb not null default '[]',status varchar(16) not null check(status in('ACTIVE','INACTIVE','BLOCKED')),created_by uuid not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),unique(tenant_id,id),unique(tenant_id,document),foreign key(tenant_id,classification_id) references cooperative.member_classifications(tenant_id,id),foreign key(tenant_id,property_id) references geo.farms(tenant_id,id));
create table if not exists cooperative.records(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),kind varchar(40) not null,name varchar(180) not null,party_id uuid not null,property_id uuid,traceability_lot_id uuid,amount numeric(18,4) not null default 0 check(amount>=0),details jsonb not null default '{}',status varchar(16) not null default 'DRAFT' check(status in('DRAFT','ACTIVE','APPROVED','CANCELLED','CLOSED')),created_by uuid not null,updated_by uuid not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),unique(tenant_id,id));
create table if not exists cooperative.technical_evidence(id uuid primary key,tenant_id uuid not null,record_id uuid not null,file_name varchar(240) not null,storage_url varchar(1000) not null,sha256 char(64) not null,latitude numeric(10,7),longitude numeric(10,7),checklist jsonb not null default '{}',created_by uuid not null,created_at timestamptz not null default now(),foreign key(tenant_id,record_id) references cooperative.records(tenant_id,id));
create table if not exists cooperative.program_members(id uuid primary key,tenant_id uuid not null,program_id uuid not null,member_id uuid not null,target numeric(18,4),quality_standard jsonb not null default '{}',compliance_status varchar(20) not null default 'PENDING',joined_at timestamptz not null default now(),unique(tenant_id,program_id,member_id),foreign key(tenant_id,program_id) references cooperative.records(tenant_id,id),foreign key(tenant_id,member_id) references cooperative.members(tenant_id,id));
create table if not exists cooperative.collective_purchases(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(180) not null,product_id uuid not null,total_quantity numeric(18,4) not null check(total_quantity>0),status varchar(16) not null default 'DRAFT' check(status in('DRAFT','APPROVED','CANCELLED','CLOSED')),created_by uuid not null,approved_by uuid,approved_at timestamptz,created_at timestamptz not null default now(),unique(tenant_id,id));
create table if not exists cooperative.collective_allocations(id uuid primary key,tenant_id uuid not null,purchase_id uuid not null,member_id uuid not null,quantity numeric(18,4) not null check(quantity>0),unique(tenant_id,purchase_id,member_id),foreign key(tenant_id,purchase_id) references cooperative.collective_purchases(tenant_id,id),foreign key(tenant_id,member_id) references cooperative.members(tenant_id,id));
create table if not exists cooperative.financial_settlements(id uuid primary key,tenant_id uuid not null,member_id uuid not null,source_record_id uuid not null,base_amount numeric(18,2) not null,bonus_amount numeric(18,2) not null default 0,net_amount numeric(18,2) not null,status varchar(16) not null default 'PENDING',calculation jsonb not null,created_at timestamptz not null default now(),foreign key(tenant_id,member_id) references cooperative.members(tenant_id,id),foreign key(tenant_id,source_record_id) references cooperative.records(tenant_id,id));
create table if not exists cooperative.audit_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,entity_type varchar(40) not null,entity_id uuid not null,event varchar(40) not null,actor_id uuid not null,details jsonb not null default '{}',occurred_at timestamptz not null default now());
create index if not exists ix_cooperative_records_queue on cooperative.records(tenant_id,kind,status,updated_at desc);
create index if not exists ix_cooperative_members_status on cooperative.members(tenant_id,status);
do $$ declare tab text; begin for tab in select tablename from pg_tables where schemaname='cooperative' loop execute format('alter table cooperative.%I enable row level security',tab); execute format('alter table cooperative.%I force row level security',tab); if not exists(select 1 from pg_policies where schemaname='cooperative' and tablename=tab and policyname=tab||'_tenant') then execute format('create policy %I on cooperative.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',tab||'_tenant',tab); end if; end loop; end $$;
insert into identity.permissions(code,module,description) values('cooperative.read','Cooperativas','Consultar rede cooperativa e portal.'),('cooperative.write','Cooperativas','Gerenciar cooperados, assistência e negócios.'),('cooperative.approve','Cooperativas','Aprovar compras, contratos e repasses.') on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('1.5.0','Sprint 18 - Cooperativas e Marketplace B2B',now()) on conflict(version) do nothing;
commit;

-- Sprint 19 - RH Rural, jornada, pessoas e SST operacional
begin;
create schema if not exists rural_hr;
create table if not exists rural_hr.roles(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(120) not null,operational_permissions text[] not null default '{}',active boolean not null default true,unique(tenant_id,id),unique(tenant_id,name));
create table if not exists rural_hr.people(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(180) not null,document varchar(14) not null,role_id uuid not null,property_id uuid not null,email varchar(254),phone varchar(30),skills jsonb not null default '[]',status varchar(16) not null check(status in('ACTIVE','INACTIVE','BLOCKED')),created_by uuid not null,updated_by uuid not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),unique(tenant_id,id),unique(tenant_id,document),foreign key(tenant_id,role_id) references rural_hr.roles(tenant_id,id),foreign key(tenant_id,property_id) references geo.farms(tenant_id,id));
create table if not exists rural_hr.records(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),kind varchar(40) not null check(kind in('TEAM','ALLOCATION','LABOR_COST','TRAINING','PPE','RISK','INSPECTION','INCIDENT','CORRECTIVE_ACTION','ACCOMMODATION','TRANSPORT')),name varchar(180) not null,person_id uuid,team_id uuid,property_id uuid,resource_id uuid,starts_at timestamptz,ends_at timestamptz,amount numeric(18,4) not null default 0 check(amount>=0),notes varchar(2000),details jsonb not null default '{}',status varchar(20) not null,created_by uuid not null,updated_by uuid not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),unique(tenant_id,id),foreign key(tenant_id,person_id) references rural_hr.people(tenant_id,id),check(ends_at is null or starts_at is null or ends_at>starts_at));
create table if not exists rural_hr.time_entries(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),person_id uuid not null,team_id uuid,property_id uuid not null,resource_id uuid,started_at timestamptz not null,ended_at timestamptz,break_minutes int not null default 0 check(break_minutes between 0 and 1440),activity_type varchar(40) not null,notes varchar(1000),offline_id varchar(100),status varchar(16) not null check(status in('OPEN','CLOSED','CANCELLED')),created_by uuid not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),unique(tenant_id,id),unique(tenant_id,offline_id),foreign key(tenant_id,person_id) references rural_hr.people(tenant_id,id),check(ended_at is null or ended_at>started_at));
create unique index if not exists ux_rural_hr_open_time on rural_hr.time_entries(tenant_id,person_id) where ended_at is null and status='OPEN';
create table if not exists rural_hr.evidence(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,record_id uuid not null,file_name varchar(240) not null,storage_url varchar(1000) not null,sha256 char(64) not null,created_by uuid not null,created_at timestamptz not null default now(),foreign key(tenant_id,record_id) references rural_hr.records(tenant_id,id));
create table if not exists rural_hr.audit_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,entity_type varchar(40) not null,entity_id uuid not null,event varchar(40) not null,actor_id uuid not null,details jsonb not null default '{}',occurred_at timestamptz not null default now());
create index if not exists ix_rural_hr_records_dashboard on rural_hr.records(tenant_id,kind,status,ends_at);
create index if not exists ix_rural_hr_time_history on rural_hr.time_entries(tenant_id,person_id,started_at desc);
do $$ declare tab text; begin for tab in select tablename from pg_tables where schemaname='rural_hr' loop execute format('alter table rural_hr.%I enable row level security',tab); execute format('alter table rural_hr.%I force row level security',tab); if not exists(select 1 from pg_policies where schemaname='rural_hr' and tablename=tab and policyname=tab||'_tenant') then execute format('create policy %I on rural_hr.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',tab||'_tenant',tab); end if; end loop; end $$;
insert into identity.permissions(code,module,description) values('rural-hr.read','RH Rural/SST','Consultar pessoas, jornada e segurança.'),('rural-hr.write','RH Rural/SST','Gerenciar pessoas, equipes, jornada e conformidade.'),('rural-hr.safety','RH Rural/SST','Gerenciar incidentes e ações SST.') on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('1.6.0','Sprint 19 - RH Rural e SST',now()) on conflict(version) do nothing;
commit;

-- Sprint 20 - release candidate: marcador idempotente do schema consolidado auditado.
insert into platform.schema_versions(version,description,installed_at)
values('2.0.0-rc.1','Sprint 20 - Release Candidate consolidado',now())
on conflict(version) do update set description=excluded.description;

-- Sprint 21 - implantação comercial, onboarding, templates e importações auditáveis
begin;
create schema if not exists deployment;
create table if not exists deployment.templates(
 code varchar(40) primary key,name varchar(120) not null,segment varchar(30) not null,
 description varchar(500) not null,configuration jsonb not null default '{}',active boolean not null default true,
 constraint ck_deployment_template_segment check(segment in('GRAINS','LIVESTOCK','AMAZON','COOPERATIVE','AGROINDUSTRY')));
create table if not exists deployment.onboardings(
 id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),segment varchar(30) not null,
 template_code varchar(40) not null references deployment.templates(code),status varchar(20) not null,
 payload jsonb not null,created_by uuid not null,created_at timestamptz not null default now(),completed_at timestamptz not null default now(),
 unique(tenant_id,id),constraint ck_onboarding_status check(status in('IN_PROGRESS','COMPLETED','CANCELLED')));
create table if not exists deployment.organization_modules(
 tenant_id uuid not null references tenancy.tenants(id),module_code varchar(80) not null references platform.modules(code),enabled boolean not null default true,
 settings jsonb not null default '{}',configured_by uuid not null,configured_at timestamptz not null default now(),primary key(tenant_id,module_code));
create table if not exists deployment.checklist_catalog(code varchar(40) primary key,label varchar(160) not null,required boolean not null,sort_order int not null unique);
create table if not exists deployment.checklist(
 tenant_id uuid not null references tenancy.tenants(id),item_code varchar(40) not null references deployment.checklist_catalog(code),label varchar(160) not null,
 required boolean not null,completed boolean not null default false,notes varchar(500),sort_order int not null default 0,completed_at timestamptz,
 updated_by uuid not null,updated_at timestamptz not null default now(),primary key(tenant_id,item_code));
create table if not exists deployment.import_previews(
 token uuid primary key,tenant_id uuid not null references tenancy.tenants(id),type varchar(30) not null,file_name varchar(240) not null,
 mapping jsonb not null,rows jsonb not null,created_at timestamptz not null default now(),expires_at timestamptz not null);
create table if not exists deployment.import_errors(
 id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),preview_token uuid references deployment.import_previews(token) on delete cascade,
 import_id uuid,line_number int not null check(line_number>1),field_name varchar(120),message varchar(500) not null,created_at timestamptz not null default now());
create table if not exists deployment.imports(
 id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),type varchar(30) not null,file_name varchar(240) not null,
 status varchar(20) not null,total_rows int not null,valid_rows int not null,invalid_rows int not null,rows jsonb not null,
 created_by uuid not null,created_at timestamptz not null default now(),confirmed_at timestamptz,rolled_back_at timestamptz,rolled_back_by uuid,
 unique(tenant_id,id),constraint ck_import_counts check(total_rows>=0 and valid_rows>=0 and invalid_rows>=0 and valid_rows+invalid_rows=total_rows),
 constraint ck_import_status check(status in('COMPLETED','ROLLED_BACK')));
do $$ begin if not exists(select 1 from pg_constraint where conname='fk_deployment_import_error_import') then alter table deployment.import_errors add constraint fk_deployment_import_error_import foreign key(import_id) references deployment.imports(id); end if; end $$;
create index if not exists ix_deployment_import_errors on deployment.import_errors(tenant_id,preview_token,line_number);
create index if not exists ix_deployment_imports_history on deployment.imports(tenant_id,created_at desc);
create index if not exists ix_deployment_checklist_pending on deployment.checklist(tenant_id,required,completed);
insert into deployment.checklist_catalog(code,label,required,sort_order) values
 ('ORGANIZATION','Organização criada',true,10),('ADMIN_USER','Usuário administrador configurado',true,20),('PROPERTY','Propriedade criada',true,30),
 ('CYCLE','Safra ou ciclo configurado',true,40),('PRODUCTS','Produtos, culturas e atividades configurados',true,50),('COST_CENTERS','Centros de custo configurados',true,60),
 ('INITIAL_STOCK','Estoque inicial configurado',false,70),('INITIAL_FINANCE','Financeiro inicial configurado',false,80),('MODULES','Módulos habilitados',true,90),
 ('TRACEABILITY','Rastreabilidade configurada quando aplicável',false,100),('COMMISSIONS_SPLIT','Comissão e split configurados quando aplicável',false,110),('USERS_ROLES','Usuários e perfis configurados',true,120)
on conflict(code) do update set label=excluded.label,required=excluded.required,sort_order=excluded.sort_order;
insert into deployment.templates(code,name,segment,description,configuration) values
 ('GRAINS','Soja, milho e grãos','GRAINS','Operação agrícola e pós-colheita', '{"cultures":["Soja","Milho"],"operations":["Plantio","Pulverização","Colheita"],"inputs":["Sementes","Fertilizantes","Defensivos"],"indicators":["Produtividade","Custo por hectare"],"costCenters":["Lavoura","Máquinas","Pós-colheita"],"postHarvest":["Recepção","Secagem","Armazenagem"]}'),
 ('LIVESTOCK','Pecuária de corte e leite','LIVESTOCK','Manejo e indicadores zootécnicos','{"herdCategories":["Cria","Recria","Engorda","Lactação"],"management":["Sanidade","Pesagem","Reprodução","Nutrição"],"indicators":["GMD","Taxa de prenhez","Produção de leite"]}'),
 ('AMAZON','Açaí, tucupi, cacau e Amazônia','AMAZON','Beneficiamento e rastreabilidade regional','{"products":["Açaí","Tucupi","Cacau"],"processing":["Recepção","Beneficiamento","Fervura","Envase"],"compliance":["Controle de fervura","Hash imutável","Rastreabilidade por lote"],"logistics":["Fluvial","Vicinal"]}'),
 ('COOPERATIVE','Cooperativa agro','COOPERATIVE','Rede de cooperados e operações coletivas','{"features":["Cooperados","Propriedades","Programas produtivos","Compras coletivas","Vendas coletivas","Assistência técnica","Rateios","Bonificações","Comissões"]}'),
 ('AGROINDUSTRY','Agroindústria','AGROINDUSTRY','Recebimento, transformação e expedição','{"stages":["Recebimento","Controle de qualidade","Processamento","Lote industrial","Expedição"],"compliance":["Dossiê de conformidade"]}')
on conflict(code) do update set name=excluded.name,segment=excluded.segment,description=excluded.description,configuration=excluded.configuration;
create or replace view deployment.organization_progress as
 select t.id tenant_id,coalesce(round(100.0*count(*) filter(where c.completed and c.required)/nullif(count(*) filter(where c.required),0)),0)::int progress
 from tenancy.tenants t left join deployment.checklist c on c.tenant_id=t.id group by t.id;
do $$ declare tab text; begin for tab in select unnest(array['onboardings','organization_modules','checklist','import_previews','import_errors','imports']) loop
 execute format('alter table deployment.%I enable row level security',tab); execute format('alter table deployment.%I force row level security',tab);
 if not exists(select 1 from pg_policies where schemaname='deployment' and tablename=tab and policyname=tab||'_tenant') then execute format('create policy %I on deployment.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',tab||'_tenant',tab); end if;
 end loop; end $$;
insert into identity.permissions(code,module,description) values
 ('deployment.read','Implantação','Consultar onboarding, checklist, templates e importações.'),('deployment.write','Implantação','Executar onboarding, parametrização e importações.')
on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('2.1.0','Sprint 21 - implantação comercial e onboarding',now()) on conflict(version) do update set description=excluded.description;
commit;
begin;
create schema if not exists crm;
create schema if not exists sales;
create sequence if not exists sales.order_number_seq;

insert into identity.permissions(code,module,description) values
('commercial.read','Commercial','Consultar CRM e comercial.'),('commercial.orders.approve','Commercial','Aprovar e cancelar pedidos.'),
('commercial.customers.override-block','Commercial','Vender para cliente bloqueado.'),('commercial.commissions.manage','Commercial','Gerenciar comissões.'),
('commercial.splits.approve','Commercial','Aprovar acordos de split.') on conflict(code) do update set description=excluded.description;

create table if not exists crm.customer_segments(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(80) not null,status varchar(16) not null default 'ACTIVE' check(status in('ACTIVE','INACTIVE')),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,name));
create table if not exists sales.regions(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(120) not null,status varchar(16) not null default 'ACTIVE',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,name));
create table if not exists sales.representatives(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),supervisor_id uuid,name varchar(180) not null,type varchar(30) not null check(type in('REPRESENTATIVE','PARTNER','FIELD_TECHNICIAN','INTERNAL_SELLER','SUPERVISOR')),email varchar(254),phone varchar(40),default_commission numeric(7,4) not null default 0 check(default_commission between 0 and 100),status varchar(16) not null default 'ACTIVE' check(status in('ACTIVE','INACTIVE','BLOCKED')),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,supervisor_id) references sales.representatives(tenant_id,id));
create table if not exists sales.representative_regions(tenant_id uuid not null,representative_id uuid not null,region_id uuid not null,primary key(tenant_id,representative_id,region_id),foreign key(tenant_id,representative_id) references sales.representatives(tenant_id,id),foreign key(tenant_id,region_id) references sales.regions(tenant_id,id));
create table if not exists sales.portfolios(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),representative_id uuid,name varchar(120) not null,status varchar(16) not null default 'ACTIVE',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,representative_id) references sales.representatives(tenant_id,id));
create table if not exists crm.customers(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),segment_id uuid not null,representative_id uuid,portfolio_id uuid,name varchar(180) not null,type varchar(16) not null check(type in('CUSTOMER','PROSPECT')),tax_document varchar(14),email varchar(254),phone varchar(40),source varchar(80),classification varchar(40),status varchar(16) not null default 'ACTIVE' check(status in('ACTIVE','INACTIVE','BLOCKED')),notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,tax_document),foreign key(tenant_id,segment_id) references crm.customer_segments(tenant_id,id),foreign key(tenant_id,representative_id) references sales.representatives(tenant_id,id),foreign key(tenant_id,portfolio_id) references sales.portfolios(tenant_id,id),check(tax_document is null or tax_document~'^[0-9]{11,14}$'));
create table if not exists crm.contacts(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),customer_id uuid not null,name varchar(180) not null,role varchar(80),email varchar(254),phone varchar(40),status varchar(16) not null default 'ACTIVE',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,customer_id) references crm.customers(tenant_id,id));
create table if not exists crm.relationship_history(id uuid primary key,tenant_id uuid not null,customer_id uuid not null,kind varchar(40) not null,description text not null,occurred_at timestamptz not null default now(),created_by uuid,foreign key(tenant_id,customer_id) references crm.customers(tenant_id,id));
create table if not exists sales.opportunities(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),customer_id uuid not null,product_id uuid,representative_id uuid,name varchar(180) not null,estimated_value numeric(18,2) not null check(estimated_value>=0),probability int not null check(probability between 0 and 100),stage varchar(24) not null check(stage in('NEW_LEAD','QUALIFICATION','PROPOSAL','NEGOTIATION','WON','LOST')),expected_close date,source varchar(80),next_action text,loss_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,customer_id) references crm.customers(tenant_id,id),foreign key(tenant_id,product_id) references inventory.products(tenant_id,id),foreign key(tenant_id,representative_id) references sales.representatives(tenant_id,id),check(stage<>'LOST' or loss_reason is not null));
create table if not exists sales.opportunity_history(id uuid primary key,tenant_id uuid not null,opportunity_id uuid not null,from_stage varchar(24),to_stage varchar(24) not null,justification text,changed_at timestamptz not null default now(),changed_by uuid,foreign key(tenant_id,opportunity_id) references sales.opportunities(tenant_id,id));
create table if not exists sales.activities(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),customer_id uuid not null,representative_id uuid,name varchar(40) not null,type varchar(30) not null check(type in('VISIT','CALL','MEETING','DEMONSTRATION','NEGOTIATION','FOLLOW_UP','TASK')),scheduled_at timestamptz not null,status varchar(20) not null check(status in('PLANNED','IN_PROGRESS','COMPLETED','CANCELLED')),channel varchar(180),result text,next_action text,notes text,cancellation_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,customer_id) references crm.customers(tenant_id,id),foreign key(tenant_id,representative_id) references sales.representatives(tenant_id,id),check(status<>'COMPLETED' or result is not null),check(status<>'CANCELLED' or cancellation_reason is not null));
create table if not exists sales.price_tables(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),segment_id uuid not null,name varchar(140) not null,valid_from date not null,valid_to date not null,status varchar(16) not null default 'ACTIVE',is_default boolean not null default false,payment_terms text,freight_policy text,commission_policy text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,segment_id) references crm.customer_segments(tenant_id,id),check(valid_to>=valid_from));
create unique index if not exists uq_price_table_default on sales.price_tables(tenant_id,segment_id,valid_from,valid_to) where is_default and status='ACTIVE' and deleted_at is null;
create table if not exists sales.price_table_items(id uuid primary key,tenant_id uuid not null,price_table_id uuid not null,product_id uuid not null,unit varchar(20) not null,base_price numeric(18,4) not null check(base_price>=0),maximum_discount numeric(7,4) not null default 0 check(maximum_discount between 0 and 100),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,price_table_id,product_id),foreign key(tenant_id,price_table_id) references sales.price_tables(tenant_id,id),foreign key(tenant_id,product_id) references inventory.products(tenant_id,id));
create table if not exists sales.contracts(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),customer_id uuid not null,product_id uuid,representative_id uuid,name varchar(180) not null,type varchar(30) not null check(type in('FUTURE_SALE','RECURRING_SUPPLY','EXPORT','COOPERATIVE','INSTITUTIONAL_PURCHASE','PARTNERSHIP')),contracted_quantity numeric(20,6) not null check(contracted_quantity>0),delivered_quantity numeric(20,6) not null default 0,contracted_value numeric(18,2) not null check(contracted_value>=0),valid_from date not null,valid_to date not null,payment_terms text,delivery_frequency varchar(50),status varchar(20) not null check(status in('DRAFT','ACTIVE','EXPIRED','CANCELLED')),notes text,cancellation_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,customer_id) references crm.customers(tenant_id,id),foreign key(tenant_id,product_id) references inventory.products(tenant_id,id),foreign key(tenant_id,representative_id) references sales.representatives(tenant_id,id),check(delivered_quantity<=contracted_quantity),check(valid_to>=valid_from),check(status<>'CANCELLED' or cancellation_reason is not null));
create table if not exists sales.orders(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),customer_id uuid not null,representative_id uuid,property_id uuid,contract_id uuid,order_number varchar(40) not null,status varchar(24) not null check(status in('DRAFT','UNDER_REVIEW','APPROVED','FULFILLMENT','INVOICED','DELIVERED','CANCELLED')),total_amount numeric(18,2) not null check(total_amount>=0),freight numeric(18,2) not null default 0 check(freight>=0),payment_terms text,expected_delivery date,notes text,cancellation_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,order_number),foreign key(tenant_id,customer_id) references crm.customers(tenant_id,id),foreign key(tenant_id,representative_id) references sales.representatives(tenant_id,id),foreign key(tenant_id,contract_id) references sales.contracts(tenant_id,id),check(status<>'CANCELLED' or cancellation_reason is not null));
create unique index if not exists uq_stock_lots_tenant_id on inventory.stock_lots(tenant_id,id);
create table if not exists sales.order_items(id uuid primary key,tenant_id uuid not null,order_id uuid not null,product_id uuid not null,lot_id uuid,quantity numeric(20,6) not null check(quantity>0),unit varchar(20) not null,unit_price numeric(18,4) not null check(unit_price>=0),discount_percentage numeric(7,4) not null default 0 check(discount_percentage between 0 and 100),total_amount numeric(18,2) not null check(total_amount>=0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),unique(tenant_id,id),foreign key(tenant_id,order_id) references sales.orders(tenant_id,id),foreign key(tenant_id,product_id) references inventory.products(tenant_id,id),foreign key(tenant_id,lot_id) references inventory.stock_lots(tenant_id,id));
create table if not exists sales.commission_plans(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(140) not null,status varchar(16) not null default 'ACTIVE',valid_from date not null,valid_to date,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id));
create table if not exists sales.commission_rules(id uuid primary key,tenant_id uuid not null,plan_id uuid not null,name varchar(140) not null,basis_type varchar(30) not null,trigger_type varchar(40) not null,product_id uuid,customer_id uuid,percentage numeric(7,4),fixed_value numeric(18,2),status varchar(16) not null default 'ACTIVE',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,plan_id) references sales.commission_plans(tenant_id,id),check((percentage is null)<>(fixed_value is null)),check(percentage between 0 and 100 or percentage is null),check(fixed_value>=0 or fixed_value is null));
create table if not exists sales.commissions(id uuid primary key,tenant_id uuid not null,order_id uuid not null,rule_id uuid not null,representative_id uuid not null,basis numeric(18,2) not null check(basis>=0),percentage numeric(7,4),fixed_value numeric(18,2),amount numeric(18,2) not null check(amount>=0),status varchar(20) not null check(status in('EXPECTED','APPROVED','BLOCKED','PAID','CANCELLED','REVERSED')),status_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,order_id,rule_id,representative_id),foreign key(tenant_id,order_id) references sales.orders(tenant_id,id),foreign key(tenant_id,rule_id) references sales.commission_rules(tenant_id,id),foreign key(tenant_id,representative_id) references sales.representatives(tenant_id,id));
create table if not exists sales.commission_history(id uuid primary key,tenant_id uuid not null,commission_id uuid not null,from_status varchar(20),to_status varchar(20) not null,reason text,amount numeric(18,2) not null,changed_at timestamptz not null default now(),changed_by uuid,foreign key(tenant_id,commission_id) references sales.commissions(tenant_id,id));
create table if not exists sales.split_agreements(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),order_id uuid,contract_id uuid,name varchar(180) not null,status varchar(20) not null check(status in('DRAFT','APPROVED','ACTIVE','CANCELLED')),status_reason text,release_rule varchar(80),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,order_id) references sales.orders(tenant_id,id),foreign key(tenant_id,contract_id) references sales.contracts(tenant_id,id));
create table if not exists sales.split_participants(id uuid primary key,tenant_id uuid not null,agreement_id uuid not null,participant_id uuid not null,participant_type varchar(30) not null,percentage numeric(7,4),fixed_value numeric(18,2),priority int not null default 0,created_at timestamptz not null default now(),unique(tenant_id,id),unique(tenant_id,agreement_id,participant_id),foreign key(tenant_id,agreement_id) references sales.split_agreements(tenant_id,id),check((percentage is null)<>(fixed_value is null)),check(percentage between 0 and 100 or percentage is null),check(fixed_value>=0 or fixed_value is null));
create table if not exists sales.split_entries(id uuid primary key,tenant_id uuid not null,agreement_id uuid not null,order_id uuid,participant_id uuid not null,amount numeric(18,2) not null check(amount>=0),status varchar(20) not null check(status in('EXPECTED','RELEASED','PAID','CANCELLED')),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),foreign key(tenant_id,agreement_id) references sales.split_agreements(tenant_id,id));
create table if not exists sales.targets(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),representative_id uuid not null,name varchar(140) not null,period_start date not null,period_end date not null,target_amount numeric(18,2) not null check(target_amount>=0),achieved_amount numeric(18,2) not null default 0 check(achieved_amount>=0),status varchar(16) not null default 'ACTIVE',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,representative_id) references sales.representatives(tenant_id,id),check(period_end>=period_start));
create table if not exists sales.commercial_events(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),event_type varchar(60) not null,aggregate_id uuid not null,payload jsonb not null default '{}',created_at timestamptz not null default now(),created_by uuid);

create index if not exists ix_crm_customers_tenant_status on crm.customers(tenant_id,status,name) where deleted_at is null;
create index if not exists ix_sales_opportunities_funnel on sales.opportunities(tenant_id,stage,representative_id) where deleted_at is null;
create index if not exists ix_sales_activities_due on sales.activities(tenant_id,status,scheduled_at) where deleted_at is null;
create index if not exists ix_sales_orders_status on sales.orders(tenant_id,status,created_at desc) where deleted_at is null;
create index if not exists ix_sales_contracts_status on sales.contracts(tenant_id,status,valid_to) where deleted_at is null;
create index if not exists ix_sales_commissions_status on sales.commissions(tenant_id,status,representative_id) where deleted_at is null;
create index if not exists ix_sales_splits_status on sales.split_agreements(tenant_id,status,updated_at desc) where deleted_at is null;
do $$ declare t text; begin foreach t in array array['customer_segments','customers','contacts'] loop execute format('alter table crm.%I enable row level security',t);execute format('drop policy if exists tenant_isolation on crm.%I',t);execute format('create policy tenant_isolation on crm.%I using (tenant_id=platform.current_tenant_id()) with check (tenant_id=platform.current_tenant_id())',t);end loop; foreach t in array array['regions','representatives','portfolios','opportunities','activities','price_tables','contracts','orders','commission_plans','commission_rules','commissions','split_agreements','targets'] loop execute format('alter table sales.%I enable row level security',t);execute format('drop policy if exists tenant_isolation on sales.%I',t);execute format('create policy tenant_isolation on sales.%I using (tenant_id=platform.current_tenant_id()) with check (tenant_id=platform.current_tenant_id())',t);end loop; end $$;
commit;

-- Sprint 23 - biblioteca documental operacional
begin;
create schema if not exists documents;
insert into identity.permissions(code,module,description) values
('documents.read','Documentos','Consultar documentos, evidências, dossiês e certificados.'),('documents.upload','Documentos','Enviar, versionar e arquivar documentos.'),('documents.download','Documentos','Baixar arquivos autorizados.'),('evidences.validate','Documentos','Validar ou rejeitar evidências.'),('dossiers.create','Documentos','Criar dossiês agro.'),('dossiers.approve','Documentos','Revisar, aprovar e fechar dossiês.'),('certificates.issue','Documentos','Emitir certificados operacionais.'),('certificates.revoke','Documentos','Revogar certificados operacionais.') on conflict(code) do update set module=excluded.module,description=excluded.description;
create table if not exists documents.document_types(id uuid primary key,name varchar(100) not null unique,code varchar(50) not null unique,active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid);
insert into documents.document_types(id,name,code) values
('23000000-0000-0000-0000-000000000001','Laudo de qualidade','QUALITY_REPORT'),('23000000-0000-0000-0000-000000000002','Nota fiscal','INVOICE'),('23000000-0000-0000-0000-000000000003','Contrato comercial','COMMERCIAL_CONTRACT'),('23000000-0000-0000-0000-000000000004','Comprovante de entrega','DELIVERY_PROOF'),('23000000-0000-0000-0000-000000000005','Certificado de rastreabilidade','TRACEABILITY_CERTIFICATE'),('23000000-0000-0000-0000-000000000006','Evidência de campo','FIELD_EVIDENCE'),('23000000-0000-0000-0000-000000000007','Foto georreferenciada','GEO_PHOTO'),('23000000-0000-0000-0000-000000000008','Registro de beneficiamento','PROCESSING_RECORD'),('23000000-0000-0000-0000-000000000009','Relatório técnico','TECHNICAL_REPORT'),('23000000-0000-0000-0000-000000000010','Documento de exportação','EXPORT_DOCUMENT'),('23000000-0000-0000-0000-000000000011','Documento sanitário','SANITARY_DOCUMENT'),('23000000-0000-0000-0000-000000000012','Documento ambiental','ENVIRONMENTAL_DOCUMENT'),('23000000-0000-0000-0000-000000000013','Comprovante financeiro','FINANCIAL_PROOF'),('23000000-0000-0000-0000-000000000014','Documento de logística','LOGISTICS_DOCUMENT'),('23000000-0000-0000-0000-000000000015','Documento de cooperativa','COOPERATIVE_DOCUMENT'),('23000000-0000-0000-0000-000000000016','Documento de RH/SST','RURAL_HR_DOCUMENT'),('23000000-0000-0000-0000-000000000017','Outro','OTHER') on conflict(code) do update set name=excluded.name,active=true;
create table if not exists documents.documents(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),document_type_id uuid not null references documents.document_types(id),name varchar(180) not null,description text,status varchar(20) not null check(status in('ACTIVE','ARCHIVED')),current_version int not null default 1 check(current_version>0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id));
create table if not exists documents.document_versions(id uuid primary key,tenant_id uuid not null,document_id uuid not null,version_number int not null check(version_number>0),original_name varchar(255) not null,storage_key varchar(500) not null unique,extension varchar(12) not null,mime_type varchar(150) not null,size_bytes bigint not null check(size_bytes>0),sha256 char(64) not null check(sha256~'^[0-9a-f]{64}$'),change_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,deleted_at timestamptz,unique(tenant_id,document_id,version_number),foreign key(tenant_id,document_id) references documents.documents(tenant_id,id));
create table if not exists documents.document_links(id uuid primary key,tenant_id uuid not null,document_id uuid not null,entity_type varchar(40) not null,entity_id uuid not null,entity_label varchar(180) not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,deleted_at timestamptz,unique(tenant_id,document_id,entity_type,entity_id),foreign key(tenant_id,document_id) references documents.documents(tenant_id,id));
create table if not exists documents.document_tags(id uuid primary key,tenant_id uuid not null,document_id uuid not null,tag varchar(60) not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,deleted_at timestamptz,unique(tenant_id,document_id,tag),foreign key(tenant_id,document_id) references documents.documents(tenant_id,id));
create table if not exists documents.document_access_logs(id uuid primary key,tenant_id uuid not null,document_id uuid not null,version_id uuid,action varchar(30) not null,accessed_at timestamptz not null default now(),accessed_by uuid not null,foreign key(tenant_id,document_id) references documents.documents(tenant_id,id));
create table if not exists documents.evidences(id uuid primary key,tenant_id uuid not null,document_id uuid not null,origin varchar(30) not null check(origin in('FIELD','PROCESSING','LOGISTICS','FINANCIAL','COMMERCIAL','COMPLIANCE','AUDIT','MOBILE_OFFLINE')),description text not null,event_at timestamptz not null,latitude numeric(9,6) check(latitude between -90 and 90),longitude numeric(9,6) check(longitude between -180 and 180),tags text[] not null default '{}',status varchar(20) not null check(status in('PENDING','VALIDATED','REJECTED','REPLACED','ARCHIVED')),validation_reason text,validated_at timestamptz,validated_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,document_id) references documents.documents(tenant_id,id),check(status<>'REJECTED' or validation_reason is not null));
create table if not exists documents.evidence_validations(id uuid primary key,tenant_id uuid not null,evidence_id uuid not null,status varchar(20) not null check(status in('VALIDATED','REJECTED')),reason text,validated_at timestamptz not null default now(),validated_by uuid not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,foreign key(tenant_id,evidence_id) references documents.evidences(tenant_id,id),check(status<>'REJECTED' or reason is not null));
create table if not exists documents.dossiers(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(180) not null,type varchar(40) not null check(type in('LOT','EXPORT','COMPLIANCE','PROPERTY','SEASON','CUSTOMER','CONTRACT','COOPERATIVE','AUDIT')),status varchar(20) not null check(status in('BUILDING','REVIEWING','APPROVED','REJECTED','CLOSED','ARCHIVED')),entity_type varchar(40),entity_id uuid,notes text,responsible_id uuid not null,opened_at timestamptz not null default now(),closed_at timestamptz,closed_by uuid,rejection_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),check(status<>'REJECTED' or rejection_reason is not null));
create table if not exists documents.dossier_items(id uuid primary key,tenant_id uuid not null,dossier_id uuid not null,item_type varchar(20) not null check(item_type in('DOCUMENT','EVIDENCE','CERTIFICATE')),item_id uuid not null,required boolean not null default false,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,deleted_at timestamptz,unique(tenant_id,dossier_id,item_type,item_id),foreign key(tenant_id,dossier_id) references documents.dossiers(tenant_id,id));
create table if not exists documents.dossier_checklist_items(id uuid primary key,tenant_id uuid not null,dossier_id uuid not null,label varchar(200) not null,required boolean not null default true,completed boolean not null default false,completed_at timestamptz,completed_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,deleted_at timestamptz,foreign key(tenant_id,dossier_id) references documents.dossiers(tenant_id,id));
create table if not exists documents.certificates(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),dossier_id uuid,type varchar(50) not null check(type in('TRACEABILITY','LOT_COMPLIANCE','PROCESSING','ORIGIN','DELIVERY','VALIDATED_EVIDENCE')),public_code varchar(40) not null unique,verification_hash char(64) not null check(verification_hash~'^[0-9a-f]{64}$'),status varchar(20) not null check(status in('ISSUED','REVOKED','EXPIRED')),organization_name varchar(180) not null,entity_type varchar(40) not null,entity_id uuid not null,subject_summary varchar(300) not null,traceability_summary text not null,issued_at timestamptz not null,valid_until timestamptz,issued_by uuid not null,revoked_at timestamptz,revoked_by uuid,revocation_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,dossier_id) references documents.dossiers(tenant_id,id),check(status<>'REVOKED' or revocation_reason is not null));
create table if not exists documents.certificate_events(id uuid primary key,tenant_id uuid not null,certificate_id uuid not null,event_type varchar(30) not null,reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,deleted_at timestamptz,foreign key(tenant_id,certificate_id) references documents.certificates(tenant_id,id));
create table if not exists documents.public_certificate_access_logs(id uuid primary key,certificate_id uuid references documents.certificates(id),code_searched varchar(40) not null,found boolean not null,remote_address_hash char(64) not null,accessed_at timestamptz not null default now());
create index if not exists ix_documents_tenant_status_type on documents.documents(tenant_id,status,document_type_id,updated_at desc) where deleted_at is null;create index if not exists ix_document_versions_tenant_document on documents.document_versions(tenant_id,document_id,version_number desc);create index if not exists ix_document_links_tenant_entity on documents.document_links(tenant_id,entity_type,entity_id) where deleted_at is null;create index if not exists ix_evidences_tenant_status on documents.evidences(tenant_id,status,event_at desc) where deleted_at is null;create index if not exists ix_dossiers_tenant_status_type on documents.dossiers(tenant_id,status,type) where deleted_at is null;create index if not exists ix_certificates_tenant_status on documents.certificates(tenant_id,status,issued_at desc);create unique index if not exists ix_certificates_public_code on documents.certificates(public_code);create index if not exists ix_public_certificate_logs_code on documents.public_certificate_access_logs(code_searched,accessed_at desc);
do $$ declare t text;begin foreach t in array array['documents','document_versions','document_links','document_tags','document_access_logs','evidences','evidence_validations','dossiers','dossier_items','dossier_checklist_items','certificates','certificate_events'] loop execute format('alter table documents.%I enable row level security',t);execute format('alter table documents.%I force row level security',t);execute format('drop policy if exists tenant_isolation on documents.%I',t);execute format('create policy tenant_isolation on documents.%I using (tenant_id=platform.current_tenant_id()) with check (tenant_id=platform.current_tenant_id())',t);end loop;end$$;
insert into platform.schema_versions(version,description,installed_at) values('2.3.0','Sprint 23 - documentos, evidências, dossiês e certificados',now()) on conflict(version) do update set description=excluded.description;
commit;

begin;
create schema if not exists operations;
create table if not exists operations.operational_tasks(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),title varchar(180) not null,description text not null,responsible_id uuid not null,priority varchar(12) not null check(priority in('LOW','MEDIUM','HIGH','CRITICAL')),due_at timestamptz not null,module varchar(50) not null,entity_type varchar(60),entity_id uuid,status varchar(24) not null default 'OPEN' check(status in('OPEN','IN_PROGRESS','WAITING_THIRD_PARTY','COMPLETED','CANCELLED')),completion_description text,cancellation_reason text,completed_at timestamptz,cancelled_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,responsible_id) references identity.users(tenant_id,id),check(status<>'COMPLETED' or completion_description is not null),check(status<>'CANCELLED' or cancellation_reason is not null));
create table if not exists operations.operational_task_events(id uuid primary key,tenant_id uuid not null,task_id uuid not null,event_type varchar(30) not null,notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,foreign key(tenant_id,task_id) references operations.operational_tasks(tenant_id,id));
create table if not exists operations.operational_alerts(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),dedup_key varchar(200) not null,title varchar(180) not null,description text not null,severity varchar(12) not null check(severity in('INFO','ATTENTION','HIGH','CRITICAL')),status varchar(12) not null default 'OPEN' check(status in('OPEN','READ','RESOLVED')),module varchar(50) not null,origin_type varchar(60) not null,origin_id uuid,read_at timestamptz,resolved_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,unique(tenant_id,id));
create unique index if not exists ux_alert_active_dedup on operations.operational_alerts(tenant_id,dedup_key) where status<>'RESOLVED';
create table if not exists operations.operational_alert_events(id uuid primary key,tenant_id uuid not null,alert_id uuid not null,event_type varchar(30) not null,notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,foreign key(tenant_id,alert_id) references operations.operational_alerts(tenant_id,id));
create table if not exists operations.operational_rules(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(180) not null,description text not null,module varchar(50) not null,type varchar(50) not null,condition_json jsonb not null,severity varchar(12) not null check(severity in('INFO','ATTENTION','HIGH','CRITICAL')),action varchar(30) not null check(action in('CREATE_ALERT','CREATE_TASK','REQUIRE_APPROVAL','INTERNAL_NOTIFICATION')),active boolean not null default true,last_executed_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,unique(tenant_id,id),unique(tenant_id,name));
create table if not exists operations.operational_rule_executions(id uuid primary key,tenant_id uuid not null,rule_id uuid not null,status varchar(12) not null check(status in('SUCCESS','FAILED')),generated_count int not null default 0,error_message text,started_at timestamptz not null,finished_at timestamptz not null,created_at timestamptz not null default now(),created_by uuid not null,foreign key(tenant_id,rule_id) references operations.operational_rules(tenant_id,id));
create table if not exists operations.workflow_definitions(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(180) not null,module varchar(50) not null,entity_type varchar(60) not null,segregation_required boolean not null default true,active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,unique(tenant_id,id));
create table if not exists operations.workflow_steps(id uuid primary key,tenant_id uuid not null,definition_id uuid not null,step_order int not null check(step_order>0),name varchar(120) not null,responsible_role varchar(80),responsible_user_id uuid,comment_required boolean not null default false,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,foreign key(tenant_id,definition_id) references operations.workflow_definitions(tenant_id,id),unique(tenant_id,definition_id,step_order));
create table if not exists operations.workflow_instances(id uuid primary key,tenant_id uuid not null,definition_id uuid not null,entity_type varchar(60) not null,entity_id uuid not null,status varchar(16) not null check(status in('OPEN','IN_REVIEW','APPROVED','REJECTED','CANCELLED')),requested_by uuid not null,current_approver_id uuid not null,opened_at timestamptz not null,decided_at timestamptz,decided_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,definition_id) references operations.workflow_definitions(tenant_id,id),foreign key(tenant_id,requested_by) references identity.users(tenant_id,id),foreign key(tenant_id,current_approver_id) references identity.users(tenant_id,id));
create table if not exists operations.workflow_instance_steps(id uuid primary key,tenant_id uuid not null,instance_id uuid not null,step_id uuid not null,status varchar(16) not null default 'PENDING' check(status in('PENDING','IN_REVIEW','APPROVED','REJECTED','SKIPPED')),assigned_to uuid,started_at timestamptz,completed_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,foreign key(tenant_id,instance_id) references operations.workflow_instances(tenant_id,id));
create table if not exists operations.workflow_decisions(id uuid primary key,tenant_id uuid not null,instance_id uuid not null,decision varchar(16) not null check(decision in('APPROVED','REJECTED','CANCELLED')),comment text,decided_by uuid not null,decided_at timestamptz not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,foreign key(tenant_id,instance_id) references operations.workflow_instances(tenant_id,id),check(decision='APPROVED' or comment is not null));
create table if not exists operations.notifications(id uuid primary key,tenant_id uuid not null,user_id uuid not null,type varchar(24) not null check(type in('ALERT','TASK','APPROVAL','COMPLIANCE','FINANCE','COMMERCIAL','LOGISTICS','TRACEABILITY','SYSTEM')),module varchar(50) not null,severity varchar(12) not null check(severity in('INFO','ATTENTION','HIGH','CRITICAL','LOW','MEDIUM')),title varchar(180) not null,message text not null,safe_link varchar(500),read_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,user_id) references identity.users(tenant_id,id));
create table if not exists operations.notification_preferences(id uuid primary key,tenant_id uuid not null,user_id uuid not null,type varchar(24) not null,enabled boolean not null default true,minimum_severity varchar(12) not null default 'INFO',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,unique(tenant_id,user_id,type),foreign key(tenant_id,user_id) references identity.users(tenant_id,id));
create table if not exists operations.communication_outbox(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),channel varchar(16) not null check(channel in('INTERNAL','EMAIL','WHATSAPP','SMS','WEBHOOK')),payload jsonb not null,status varchar(20) not null default 'PENDING' check(status in('PENDING','PROCESSING','SENT','FAILED','NOT_CONFIGURED')),attempts int not null default 0 check(attempts>=0),last_error text,created_at timestamptz not null default now(),processed_at timestamptz,updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid);
create table if not exists operations.calendar_events(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),type varchar(40) not null,title varchar(180) not null,starts_at timestamptz not null,ends_at timestamptz,module varchar(50) not null,responsible_id uuid,priority varchar(12),entity_type varchar(60),entity_id uuid,safe_link varchar(500),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid,deleted_at timestamptz,foreign key(tenant_id,responsible_id) references identity.users(tenant_id,id));
create index if not exists ix_tasks_tenant_status_due on operations.operational_tasks(tenant_id,status,due_at);create index if not exists ix_tasks_responsible on operations.operational_tasks(tenant_id,responsible_id,due_at);create index if not exists ix_tasks_priority on operations.operational_tasks(tenant_id,priority);create index if not exists ix_alerts_status_severity on operations.operational_alerts(tenant_id,status,severity,created_at desc);create index if not exists ix_rules_active on operations.operational_rules(tenant_id,active);create index if not exists ix_rule_executions on operations.operational_rule_executions(tenant_id,rule_id,started_at desc);create index if not exists ix_workflows_status_approver on operations.workflow_instances(tenant_id,status,current_approver_id);create index if not exists ix_notifications_user_read on operations.notifications(tenant_id,user_id,read_at,created_at desc);create index if not exists ix_outbox_status on operations.communication_outbox(tenant_id,status,created_at);create index if not exists ix_calendar_range on operations.calendar_events(tenant_id,starts_at,ends_at);
create or replace function operations.evaluate_operational_rules(p_tenant uuid,p_actor uuid) returns integer language plpgsql security invoker as $$declare n integer:=0;affected integer:=0;r record;begin
 insert into operations.operational_alerts(id,tenant_id,dedup_key,title,description,severity,module,origin_type,origin_id,created_by) select gen_random_uuid(),p_tenant,'LOW_STOCK:'||b.product_id,'Estoque abaixo do mínimo',p.name||' está abaixo do estoque mínimo','HIGH','INVENTORY','PRODUCT',b.product_id,p_actor from inventory.stock_balances b join inventory.products p on p.id=b.product_id and p.tenant_id=b.tenant_id where b.tenant_id=p_tenant and b.available<b.minimum on conflict do nothing;get diagnostics n=row_count;
 insert into operations.operational_alerts(id,tenant_id,dedup_key,title,description,severity,module,origin_type,origin_id,created_by) select gen_random_uuid(),p_tenant,'PAYABLE_DUE:'||id,'Conta a pagar vencendo',supplier_name||' — vencimento '||due_on,'ATTENTION','FINANCE','PAYABLE',id,p_actor from finance.payables where tenant_id=p_tenant and status in('OPEN','PARTIAL') and due_on<=current_date+3 on conflict do nothing;get diagnostics affected=row_count;n:=n+affected;
 insert into operations.operational_alerts(id,tenant_id,dedup_key,title,description,severity,module,origin_type,origin_id,created_by) select gen_random_uuid(),p_tenant,'RECEIVABLE_OVERDUE:'||id,'Conta a receber vencida',customer_name||' — vencimento '||due_on,'HIGH','FINANCE','RECEIVABLE',id,p_actor from finance.receivables where tenant_id=p_tenant and status in('OPEN','PARTIAL') and due_on<current_date on conflict do nothing;get diagnostics affected=row_count;n:=n+affected;
 insert into operations.operational_alerts(id,tenant_id,dedup_key,title,description,severity,module,origin_type,origin_id,created_by) select gen_random_uuid(),p_tenant,'CRITICAL_TASK:'||id,'Tarefa crítica vencida',title,'CRITICAL','TASKS','TASK',id,p_actor from operations.operational_tasks where tenant_id=p_tenant and priority='CRITICAL' and status not in('COMPLETED','CANCELLED') and due_at<now() on conflict do nothing;get diagnostics affected=row_count;n:=n+affected;
 for r in select id from operations.operational_rules where tenant_id=p_tenant and active loop update operations.operational_rules set last_executed_at=now() where id=r.id;insert into operations.operational_rule_executions(id,tenant_id,rule_id,status,generated_count,started_at,finished_at,created_by) values(gen_random_uuid(),p_tenant,r.id,'SUCCESS',n,now(),now(),p_actor);end loop;return n;exception when others then raise warning 'Operational rule evaluation failed for tenant %: %',p_tenant,sqlstate;raise;end$$;
insert into operations.workflow_definitions(id,tenant_id,name,module,entity_type,segregation_required,created_by) select gen_random_uuid(),t.id,x.name,x.module,x.entity_type,true,coalesce((select id from identity.users where tenant_id=t.id order by created_at limit 1),gen_random_uuid()) from tenancy.tenants t cross join(values('Pedido com desconto acima do limite','COMMERCIAL','ORDER'),('Contrato comercial','COMMERCIAL','CONTRACT'),('Dossiê','DOCUMENTS','DOSSIER'),('Certificado','DOCUMENTS','CERTIFICATE'),('Evidência crítica','COMPLIANCE','EVIDENCE'),('Comissão manual','COMMERCIAL','COMMISSION'),('Split','FINANCE','SPLIT'),('Cancelamento de pedido aprovado','COMMERCIAL','ORDER'),('Compra acima do limite','PURCHASING','PURCHASE'),('Ajuste de estoque','INVENTORY','STOCK_ADJUSTMENT'),('Encerramento de não conformidade','COMPLIANCE','NON_CONFORMITY'))x(name,module,entity_type) where exists(select 1 from identity.users where tenant_id=t.id) on conflict do nothing;
do $$declare t text;begin foreach t in array array['operational_tasks','operational_task_events','operational_alerts','operational_alert_events','operational_rules','operational_rule_executions','workflow_definitions','workflow_steps','workflow_instances','workflow_instance_steps','workflow_decisions','notifications','notification_preferences','communication_outbox','calendar_events'] loop execute format('alter table operations.%I enable row level security',t);execute format('drop policy if exists tenant_isolation on operations.%I',t);execute format('create policy tenant_isolation on operations.%I using (tenant_id=platform.current_tenant_id()) with check (tenant_id=platform.current_tenant_id())',t);end loop;end$$;
insert into platform.schema_versions(version,description,installed_at) values('2.4.0','Sprint 24 - tarefas, alertas, workflows e notificações',now()) on conflict(version) do update set description=excluded.description;
commit;
-- Sprint 25: BI executivo, relatórios, mapas operacionais e preferências de UI.
begin;
create schema if not exists bi;
create schema if not exists geo;
create schema if not exists ui;

insert into identity.permissions(code,module,description) values
('bi.read','Inteligência Agro360','Consultar dashboards e relatórios gerenciais.'),
('bi.export','Inteligência Agro360','Exportar relatórios gerenciais autorizados.'),
('bi.manage','Inteligência Agro360','Gerenciar filtros, widgets e definições de relatório.'),
('geo.read','Mapas','Consultar camadas e objetos geográficos operacionais.'),
('geo.manage','Mapas','Gerenciar camadas, áreas e rotas geográficas.')
on conflict(code) do update set module=excluded.module,description=excluded.description;

create table if not exists bi.saved_filters(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), user_id uuid not null,
 name varchar(120) not null, module varchar(40) not null, filters jsonb not null default '{}', is_default boolean not null default false,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), unique(tenant_id,user_id,module,name), check(jsonb_typeof(filters)='object'));
create table if not exists bi.dashboard_widgets(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), code varchar(80) not null, title varchar(160) not null,
 module varchar(40) not null, visualization varchar(20) not null check(visualization in('KPI','LINE','BAR','DONUT','RANKING','TABLE')),
 query_key varchar(100) not null, configuration jsonb not null default '{}', enabled boolean not null default true,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), unique(tenant_id,code), check(jsonb_typeof(configuration)='object'));
create table if not exists bi.report_definitions(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), code varchar(80) not null, name varchar(160) not null,
 module varchar(40) not null, description text, query_key varchar(100) not null, allowed_formats text[] not null default '{CSV}', enabled boolean not null default true,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), unique(tenant_id,code), check(cardinality(allowed_formats)>0));
create table if not exists bi.report_exports(
 id uuid primary key, tenant_id uuid not null, report_definition_id uuid not null, requested_by uuid not null,
 format varchar(10) not null check(format in('CSV','PDF')), status varchar(20) not null check(status in('PENDING','PROCESSING','COMPLETED','FAILED','EXPIRED')),
 filters jsonb not null default '{}', storage_key varchar(500), row_count int check(row_count>=0), error_message varchar(500), expires_at timestamptz,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), foreign key(tenant_id,report_definition_id) references bi.report_definitions(tenant_id,id), check(jsonb_typeof(filters)='object'));
create table if not exists bi.user_dashboard_preferences(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), user_id uuid not null, dashboard varchar(50) not null,
 widget_order jsonb not null default '[]', preferences jsonb not null default '{}',
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), unique(tenant_id,user_id,dashboard), check(jsonb_typeof(widget_order)='array'), check(jsonb_typeof(preferences)='object'));

create table if not exists geo.geo_layers(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), name varchar(140) not null, layer_type varchar(30) not null,
 color char(7) not null default '#43A276' check(color~'^#[0-9A-Fa-f]{6}$'), visible boolean not null default true,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), unique(tenant_id,name));
create table if not exists geo.geo_locations(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), layer_id uuid, entity_type varchar(40) not null, entity_id uuid,
 name varchar(180) not null, latitude numeric(9,6) not null check(latitude between -90 and 90), longitude numeric(9,6) not null check(longitude between -180 and 180),
 metadata jsonb not null default '{}', observed_at timestamptz, status varchar(30) not null default 'ACTIVE',
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), foreign key(tenant_id,layer_id) references geo.geo_layers(tenant_id,id), check(jsonb_typeof(metadata)='object'));
create table if not exists geo.geo_areas(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), layer_id uuid, entity_type varchar(40) not null, entity_id uuid,
 name varchar(180) not null, boundary jsonb not null, area_hectares numeric(16,4) check(area_hectares>=0), status varchar(30) not null default 'ACTIVE',
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), foreign key(tenant_id,layer_id) references geo.geo_layers(tenant_id,id), check(jsonb_typeof(boundary)='object'));
create table if not exists geo.geo_routes(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), layer_id uuid, name varchar(180) not null,
 route_type varchar(30) not null check(route_type in('ROAD','RURAL_ROAD','RIVER','COLLECTION','DELIVERY','FIELD')),
 status varchar(20) not null check(status in('DRAFT','ACTIVE','INACTIVE')), distance_km numeric(14,3) check(distance_km>=0),
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), foreign key(tenant_id,layer_id) references geo.geo_layers(tenant_id,id));
create table if not exists geo.geo_route_points(
 id uuid primary key, tenant_id uuid not null, route_id uuid not null, sequence int not null check(sequence>=0), name varchar(180),
 point_type varchar(30) not null check(point_type in('WAYPOINT','COLLECTION','DELIVERY','OCCURRENCE')),
 latitude numeric(9,6) not null check(latitude between -90 and 90), longitude numeric(9,6) not null check(longitude between -180 and 180), occurred_at timestamptz,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), unique(tenant_id,route_id,sequence), foreign key(tenant_id,route_id) references geo.geo_routes(tenant_id,id));
create table if not exists ui.ui_audit_events(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), user_id uuid, event_type varchar(60) not null,
 route varchar(240) not null, component varchar(100), metadata jsonb not null default '{}', occurred_at timestamptz not null default now(),
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid,
 unique(tenant_id,id), check(jsonb_typeof(metadata)='object'));

create index if not exists ix_bi_saved_filters_tenant_user on bi.saved_filters(tenant_id,user_id,module) where deleted_at is null;
create index if not exists ix_bi_widgets_tenant_module on bi.dashboard_widgets(tenant_id,module,enabled) where deleted_at is null;
create index if not exists ix_bi_reports_tenant_module on bi.report_definitions(tenant_id,module,enabled) where deleted_at is null;
create index if not exists ix_bi_exports_tenant_status_date on bi.report_exports(tenant_id,status,created_at desc) where deleted_at is null;
create index if not exists ix_bi_preferences_tenant_user on bi.user_dashboard_preferences(tenant_id,user_id,dashboard) where deleted_at is null;
create index if not exists ix_geo_locations_tenant_type_status on geo.geo_locations(tenant_id,entity_type,status) where deleted_at is null;
create index if not exists ix_geo_locations_tenant_coordinates on geo.geo_locations(tenant_id,latitude,longitude) where deleted_at is null;
create index if not exists ix_geo_areas_tenant_type_status on geo.geo_areas(tenant_id,entity_type,status) where deleted_at is null;
create index if not exists ix_geo_routes_tenant_type_status on geo.geo_routes(tenant_id,route_type,status) where deleted_at is null;
create index if not exists ix_geo_route_points_tenant_route on geo.geo_route_points(tenant_id,route_id,sequence) where deleted_at is null;
create index if not exists ix_ui_audit_tenant_date_type on ui.ui_audit_events(tenant_id,occurred_at desc,event_type);

do $$ declare item record; begin
 for item in select * from (values ('bi','saved_filters'),('bi','dashboard_widgets'),('bi','report_definitions'),('bi','report_exports'),('bi','user_dashboard_preferences'),('geo','geo_layers'),('geo','geo_locations'),('geo','geo_areas'),('geo','geo_routes'),('geo','geo_route_points'),('ui','ui_audit_events')) as scoped(schema_name,table_name) loop
  execute format('alter table %I.%I enable row level security',item.schema_name,item.table_name);
  execute format('drop policy if exists tenant_isolation on %I.%I',item.schema_name,item.table_name);
  execute format('create policy tenant_isolation on %I.%I using (tenant_id=platform.current_tenant_id()) with check (tenant_id=platform.current_tenant_id())',item.schema_name,item.table_name);
 end loop;
end $$;
commit;

-- Sprint 27: Portal Agro360 externo e Marketplace B2B
begin;
create schema if not exists portal;
create table if not exists portal.portal_profiles(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),code varchar(40) not null,name varchar(100) not null,active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,code));
create table if not exists portal.portal_permissions(id uuid primary key,tenant_id uuid not null,profile_id uuid not null,permission varchar(100) not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,profile_id,permission),foreign key(tenant_id,profile_id) references portal.portal_profiles(tenant_id,id));
create table if not exists portal.portal_external_users(id uuid primary key,tenant_id uuid not null,profile_id uuid not null,name varchar(160) not null,email varchar(254) not null,password_hash text not null,status varchar(20) not null check(status in('PENDING','ACTIVE','BLOCKED','INACTIVE')),terms_accepted_at timestamptz,last_login_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,email),foreign key(tenant_id,profile_id) references portal.portal_profiles(tenant_id,id),check(email=lower(email) and email~'^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$'));
create table if not exists portal.portal_external_user_links(id uuid primary key,tenant_id uuid not null,external_user_id uuid not null,entity_type varchar(40) not null,entity_id uuid not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,external_user_id,entity_type,entity_id),foreign key(tenant_id,external_user_id) references portal.portal_external_users(tenant_id,id));
create table if not exists portal.portal_invitations(id uuid primary key,tenant_id uuid not null,profile_id uuid not null,name varchar(160) not null,email varchar(254) not null,entity_type varchar(40) not null,entity_id uuid not null,entity_label varchar(180) not null,token_hash char(64) not null unique,expires_at timestamptz not null,accepted_at timestamptz,accepted_user_id uuid,revoked_at timestamptz,revoked_by uuid,revoke_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,profile_id) references portal.portal_profiles(tenant_id,id),foreign key(tenant_id,accepted_user_id) references portal.portal_external_users(tenant_id,id),check(email=lower(email) and email~'^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$'),check(accepted_at is null or revoked_at is null),check(revoked_at is null or revoke_reason is not null));
create table if not exists portal.portal_terms(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),version varchar(30) not null,title varchar(180) not null,content text not null,active boolean not null default true,effective_at timestamptz not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,version));
create table if not exists portal.portal_terms_acceptances(id uuid primary key,tenant_id uuid not null,external_user_id uuid not null,term_id uuid not null,accepted_at timestamptz not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),unique(tenant_id,external_user_id,term_id),foreign key(tenant_id,external_user_id) references portal.portal_external_users(tenant_id,id),foreign key(tenant_id,term_id) references portal.portal_terms(tenant_id,id));
create table if not exists portal.portal_dashboard_cards(id uuid primary key,tenant_id uuid not null,profile_id uuid not null,code varchar(60) not null,title varchar(120) not null,position int not null check(position>=0),active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,profile_id,code),foreign key(tenant_id,profile_id) references portal.portal_profiles(tenant_id,id));
create table if not exists portal.portal_announcements(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),title varchar(180) not null,summary varchar(500) not null,content text not null,audience varchar(40) not null,severity varchar(15) not null default 'INFO' check(severity in('INFO','SUCCESS','WARNING','CRITICAL')),status varchar(20) not null check(status in('DRAFT','PUBLISHED','ARCHIVED')),published_at timestamptz,expires_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),check(expires_at is null or published_at is null or expires_at>published_at));
create table if not exists portal.portal_announcement_reads(id uuid primary key,tenant_id uuid not null,announcement_id uuid not null,external_user_id uuid not null,read_at timestamptz not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),unique(tenant_id,announcement_id,external_user_id),foreign key(tenant_id,announcement_id) references portal.portal_announcements(tenant_id,id),foreign key(tenant_id,external_user_id) references portal.portal_external_users(tenant_id,id));
create table if not exists portal.portal_requests(id uuid primary key,tenant_id uuid not null,external_user_id uuid not null,protocol varchar(30) not null,type varchar(40) not null,subject varchar(160) not null,description text not null,status varchar(24) not null check(status in('OPEN','IN_REVIEW','WAITING_RESPONSE','RESOLVED','CANCELLED')),priority varchar(12) not null check(priority in('LOW','MEDIUM','HIGH','CRITICAL')),resolution text,cancellation_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,protocol),foreign key(tenant_id,external_user_id) references portal.portal_external_users(tenant_id,id),check(status<>'RESOLVED' or resolution is not null),check(status<>'CANCELLED' or cancellation_reason is not null));
create table if not exists portal.portal_request_events(id uuid primary key,tenant_id uuid not null,request_id uuid not null,event_type varchar(40) not null,message text not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,request_id) references portal.portal_requests(tenant_id,id));
create table if not exists portal.portal_messages(id uuid primary key,tenant_id uuid not null,external_user_id uuid not null,request_id uuid,author_type varchar(12) not null check(author_type in('INTERNAL','EXTERNAL')),body text not null,read_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,external_user_id) references portal.portal_external_users(tenant_id,id),foreign key(tenant_id,request_id) references portal.portal_requests(tenant_id,id));
create table if not exists portal.marketplace_catalogs(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(160) not null,status varchar(20) not null check(status in('DRAFT','PUBLISHED','ARCHIVED')),valid_from timestamptz,valid_until timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id));
create table if not exists portal.marketplace_catalog_items(id uuid primary key,tenant_id uuid not null,catalog_id uuid not null,product_id uuid,display_name varchar(180) not null,position int not null default 0,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,catalog_id) references portal.marketplace_catalogs(tenant_id,id));
create table if not exists portal.marketplace_listings(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),catalog_item_id uuid,product_name varchar(180) not null,crop varchar(100),harvest varchar(80),region varchar(120),unit varchar(20) not null,available_quantity numeric(18,4) not null check(available_quantity>=0),unit_price numeric(18,4) check(unit_price>0),commercial_terms text not null,status varchar(20) not null check(status in('DRAFT','AVAILABLE','PAUSED','SOLD_OUT','ARCHIVED')),origin_summary varchar(300),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,catalog_item_id) references portal.marketplace_catalog_items(tenant_id,id));
create table if not exists portal.marketplace_listing_certificates(id uuid primary key,tenant_id uuid not null,listing_id uuid not null,certificate_id uuid not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),unique(tenant_id,listing_id,certificate_id),foreign key(tenant_id,listing_id) references portal.marketplace_listings(tenant_id,id),foreign key(tenant_id,certificate_id) references documents.certificates(tenant_id,id));
create table if not exists portal.marketplace_quote_requests(id uuid primary key,tenant_id uuid not null,external_user_id uuid not null,protocol varchar(30) not null,contact_name varchar(160) not null,contact_email varchar(254) not null,notes text,status varchar(20) not null check(status in('REQUESTED','IN_REVIEW','PROPOSED','ACCEPTED','REJECTED','CONVERTED','CANCELLED')),valid_until timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,protocol),foreign key(tenant_id,external_user_id) references portal.portal_external_users(tenant_id,id));
create table if not exists portal.marketplace_quote_request_items(id uuid primary key,tenant_id uuid not null,quote_request_id uuid not null,listing_id uuid not null,quantity numeric(18,4) not null check(quantity>0),unit varchar(20) not null,offered_unit_price numeric(18,4) check(offered_unit_price>0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,quote_request_id) references portal.marketplace_quote_requests(tenant_id,id),foreign key(tenant_id,listing_id) references portal.marketplace_listings(tenant_id,id));
create table if not exists portal.marketplace_quote_events(id uuid primary key,tenant_id uuid not null,quote_request_id uuid not null,event_type varchar(40) not null,notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,quote_request_id) references portal.marketplace_quote_requests(tenant_id,id));
create table if not exists portal.supplier_prequalifications(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),supplier_id uuid not null,status varchar(20) not null check(status in('PENDING','IN_REVIEW','APPROVED','REJECTED','BLOCKED')),notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id));
create table if not exists portal.supplier_document_requirements(id uuid primary key,tenant_id uuid not null,prequalification_id uuid not null,name varchar(160) not null,required boolean not null default true,status varchar(20) not null check(status in('PENDING','SUBMITTED','APPROVED','REJECTED')),due_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,prequalification_id) references portal.supplier_prequalifications(tenant_id,id));
create table if not exists portal.transporter_delivery_updates(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),external_user_id uuid not null,delivery_id uuid not null,status varchar(30) not null check(status in('AWAITING_PICKUP','PICKING_UP','COLLECTED','IN_TRANSIT','DELIVERED','DELIVERY_INCIDENT','CANCELLED')),description text,evidence_document_id uuid,occurred_at timestamptz not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,external_user_id) references portal.portal_external_users(tenant_id,id),foreign key(tenant_id,evidence_document_id) references documents.documents(tenant_id,id),check(status<>'DELIVERY_INCIDENT' or description is not null));
create table if not exists portal.external_document_submissions(id uuid primary key,tenant_id uuid not null,external_user_id uuid not null,document_id uuid not null,entity_type varchar(40) not null,entity_id uuid not null,status varchar(20) not null check(status in('SUBMITTED','IN_REVIEW','APPROVED','REJECTED')),review_notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,external_user_id) references portal.portal_external_users(tenant_id,id),foreign key(tenant_id,document_id) references documents.documents(tenant_id,id));
create table if not exists portal.external_audit_events(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),external_user_id uuid,event_type varchar(60) not null,entity_type varchar(40),entity_id uuid,metadata jsonb not null default '{}',occurred_at timestamptz not null default now(),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),check(jsonb_typeof(metadata)='object'));
create index if not exists ix_portal_users_tenant_profile_status on portal.portal_external_users(tenant_id,profile_id,status) where deleted_at is null;
create index if not exists ix_portal_links_tenant_entity on portal.portal_external_user_links(tenant_id,entity_type,entity_id) where deleted_at is null;
create index if not exists ix_portal_invitations_tenant_status_date on portal.portal_invitations(tenant_id,expires_at,revoked_at,accepted_at) where deleted_at is null;
create index if not exists ix_portal_announcements_tenant_audience_date on portal.portal_announcements(tenant_id,audience,status,published_at desc) where deleted_at is null;
create index if not exists ix_portal_requests_tenant_user_status on portal.portal_requests(tenant_id,external_user_id,status,updated_at desc) where deleted_at is null;
create index if not exists ix_marketplace_listings_filters on portal.marketplace_listings(tenant_id,status,crop,region,unit,unit_price) where deleted_at is null;
create index if not exists ix_marketplace_quotes_tenant_user_status on portal.marketplace_quote_requests(tenant_id,external_user_id,status,created_at desc) where deleted_at is null;
create index if not exists ix_supplier_prequalification_status on portal.supplier_prequalifications(tenant_id,supplier_id,status,updated_at desc) where deleted_at is null;
create index if not exists ix_transporter_updates_delivery_date on portal.transporter_delivery_updates(tenant_id,external_user_id,delivery_id,occurred_at desc);
create index if not exists ix_external_documents_entity_status on portal.external_document_submissions(tenant_id,entity_type,entity_id,status) where deleted_at is null;
create index if not exists ix_external_audit_date on portal.external_audit_events(tenant_id,external_user_id,occurred_at desc);
do $$ declare t text; begin foreach t in array array['portal_profiles','portal_permissions','portal_external_users','portal_external_user_links','portal_invitations','portal_terms','portal_terms_acceptances','portal_dashboard_cards','portal_announcements','portal_announcement_reads','portal_requests','portal_request_events','portal_messages','marketplace_catalogs','marketplace_catalog_items','marketplace_listings','marketplace_listing_certificates','marketplace_quote_requests','marketplace_quote_request_items','marketplace_quote_events','supplier_prequalifications','supplier_document_requirements','transporter_delivery_updates','external_document_submissions','external_audit_events'] loop execute format('alter table portal.%I enable row level security',t);execute format('drop policy if exists tenant_isolation on portal.%I',t);execute format('create policy tenant_isolation on portal.%I using (tenant_id=platform.current_tenant_id()) with check (tenant_id=platform.current_tenant_id())',t);end loop;end $$;
insert into portal.portal_profiles(id,tenant_id,code,name,created_by) select gen_random_uuid(),t.id,p.code,p.name,null from tenancy.tenants t cross join (values ('PRODUCER','Produtor'),('COOPERATIVE_MEMBER','Cooperado'),('B2B_CUSTOMER','Cliente B2B'),('BUYER','Comprador'),('SUPPLIER','Fornecedor'),('TRANSPORTER','Transportador'),('EXTERNAL_REPRESENTATIVE','Representante externo'),('EXTERNAL_AUDITOR','Auditor externo'),('PARTNER_TECHNICIAN','Técnico parceiro')) p(code,name) on conflict(tenant_id,code) do nothing;
insert into portal.portal_terms(id,tenant_id,version,title,content,active,effective_at) select gen_random_uuid(),id,'1.0','Termos de uso do Portal Agro360','Uso restrito às operações autorizadas pela organização. O acesso é pessoal, auditável e sujeito à política de privacidade.',true,now() from tenancy.tenants on conflict(tenant_id,version) do nothing;
insert into platform.schema_versions(version,description,installed_at) values('2.7.0','Sprint 27 - Portal Externo e Marketplace B2B',now()) on conflict(version) do update set description=excluded.description;
commit;

-- Sprint 28: Qualidade agroindustrial e compliance parametrizável
begin;
create schema if not exists quality;
create table if not exists quality.compliance_requirements(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(180) not null,description text not null,area varchar(30) not null check(area in('SANITARY','ENVIRONMENTAL','FISCAL','OHS','QUALITY','EXPORT','TRACEABILITY','LOGISTICS','PROCESSING','COMMERCIAL','DOCUMENTAL')),requirement_type varchar(40) not null,periodicity varchar(30),severity varchar(12) not null check(severity in('LOW','MEDIUM','HIGH','CRITICAL')),status varchar(12) not null check(status in('DRAFT','ACTIVE','INACTIVE')),requires_document boolean not null default false,requires_evidence boolean not null default false,requires_approval boolean not null default false,requires_expiry boolean not null default false,blocks_lot_release boolean not null default false,alert_days int not null default 0 check(alert_days>=0),applicable_entity varchar(40) not null,responsible_id uuid references identity.users(id),normative_reference varchar(300),notes text,valid_until date,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),check(not requires_document or requires_evidence),check(not requires_expiry or valid_until is not null));
create table if not exists quality.compliance_requirement_entities(id uuid primary key,tenant_id uuid not null,requirement_id uuid not null,entity_type varchar(40) not null,entity_id uuid not null,status varchar(20) not null check(status in('PENDING','IN_REVIEW','COMPLIANT','NON_COMPLIANT','EXPIRED','WAIVED')),expires_on date,approved_by uuid,approved_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,requirement_id,entity_type,entity_id),foreign key(tenant_id,requirement_id) references quality.compliance_requirements(tenant_id,id));
create table if not exists quality.compliance_requirement_evidences(id uuid primary key,tenant_id uuid not null,requirement_entity_id uuid not null,document_id uuid,description text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,requirement_entity_id) references quality.compliance_requirement_entities(tenant_id,id));
create table if not exists quality.compliance_requirement_events(id uuid primary key,tenant_id uuid not null,requirement_entity_id uuid not null,event_type varchar(40) not null,details jsonb not null default '{}',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,requirement_entity_id) references quality.compliance_requirement_entities(tenant_id,id));
create table if not exists quality.quality_specifications(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),product_id uuid not null,name varchar(180) not null,version int not null check(version>0),status varchar(12) not null check(status in('DRAFT','ACTIVE','INACTIVE','EXPIRED')),application_type varchar(30) not null,valid_from date not null,valid_until date,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,product_id,name,version),check(valid_until is null or valid_until>=valid_from));
create table if not exists quality.quality_specification_parameters(id uuid primary key,tenant_id uuid not null,specification_id uuid not null,name varchar(120) not null,unit varchar(30),minimum_value numeric(18,6),maximum_value numeric(18,6),target_value numeric(18,6),tolerance numeric(18,6) not null default 0 check(tolerance>=0),required boolean not null default true,critical boolean not null default false,evidence_required boolean not null default false,analysis_method varchar(200),analysis_frequency varchar(80),responsible_id uuid references identity.users(id),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,specification_id) references quality.quality_specifications(tenant_id,id),check(minimum_value is null or maximum_value is null or minimum_value<=maximum_value));
create table if not exists quality.quality_specification_versions(id uuid primary key,tenant_id uuid not null,specification_id uuid not null,version int not null check(version>0),snapshot jsonb not null,change_reason text not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,specification_id) references quality.quality_specifications(tenant_id,id));
create table if not exists quality.quality_inspections(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),inspection_type varchar(30) not null,product_id uuid not null,lot_id uuid,property_id uuid,harvest_id uuid,specification_id uuid,responsible_id uuid not null references identity.users(id),inspected_at timestamptz not null,status varchar(20) not null check(status in('PLANNED','IN_PROGRESS','PENDING_REVIEW','COMPLETED','CANCELLED')),overall_result varchar(30),decision varchar(30),decision_reason text,notes text,completed_at timestamptz,completed_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,specification_id) references quality.quality_specifications(tenant_id,id),check(decision not in('REJECT','BLOCK','QUARANTINE','APPROVE_WITH_RESTRICTION') or decision_reason is not null));
create table if not exists quality.quality_inspection_parameters(id uuid primary key,tenant_id uuid not null,inspection_id uuid not null,specification_parameter_id uuid,name varchar(120) not null,required boolean not null,critical boolean not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,inspection_id) references quality.quality_inspections(tenant_id,id));
create table if not exists quality.quality_inspection_results(id uuid primary key,tenant_id uuid not null,inspection_parameter_id uuid not null,numeric_value numeric(18,6),text_value text,conforming boolean not null,evidence_document_id uuid,notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),unique(tenant_id,inspection_parameter_id),foreign key(tenant_id,inspection_parameter_id) references quality.quality_inspection_parameters(tenant_id,id),check(numeric_value is not null or text_value is not null));
create table if not exists quality.lot_quality_status_history(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),lot_id uuid not null,previous_status varchar(30),new_status varchar(30) not null check(new_status in('AWAITING_INSPECTION','IN_ANALYSIS','APPROVED','APPROVED_WITH_RESTRICTION','BLOCKED','QUARANTINE','REJECTED','RELEASED_FOR_SALE','RELEASED_FOR_SHIPMENT','AUDIT_HOLD','CANCELLED')),reason text,changed_by uuid not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),check(new_status not in('BLOCKED','QUARANTINE','REJECTED','CANCELLED') or reason is not null));
create table if not exists quality.lot_quality_holds(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),lot_id uuid not null,hold_type varchar(24) not null,reason text not null,released_at timestamptz,released_by uuid,release_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),check(released_at is null or release_reason is not null));
create table if not exists quality.non_conformities(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),code varchar(30) not null,title varchar(180) not null,description text not null,type varchar(30) not null,severity varchar(12) not null check(severity in('LOW','MEDIUM','HIGH','CRITICAL')),entity_type varchar(40) not null,entity_id uuid not null,origin varchar(80) not null,responsible_id uuid,opened_at timestamptz not null,due_on date not null,status varchar(30) not null check(status in('OPEN','IN_ANALYSIS','WAITING_ACTION','ACTION_IN_PROGRESS','WAITING_VERIFICATION','RESOLVED','REOPENED','CANCELLED')),root_cause text,immediate_action text,verification_result text,closed_at timestamptz,closed_by uuid,cancellation_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,code),check(severity<>'CRITICAL' or responsible_id is not null),check(status<>'CANCELLED' or cancellation_reason is not null));
create table if not exists quality.non_conformity_events(id uuid primary key,tenant_id uuid not null,non_conformity_id uuid not null,event_type varchar(40) not null,reason text,details jsonb not null default '{}',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,non_conformity_id) references quality.non_conformities(tenant_id,id));
create table if not exists quality.corrective_actions(id uuid primary key,tenant_id uuid not null,non_conformity_id uuid not null,action_type varchar(24) not null check(action_type in('IMMEDIATE','CORRECTIVE','PREVENTIVE')),responsible_id uuid not null references identity.users(id),description text not null,due_on date not null,status varchar(24) not null check(status in('OPEN','IN_PROGRESS','PENDING_VALIDATION','COMPLETED','REJECTED','CANCELLED')),mandatory boolean not null default true,critical boolean not null default false,result text,completed_at timestamptz,validated_by uuid,rejection_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,non_conformity_id) references quality.non_conformities(tenant_id,id),check(status<>'COMPLETED' or result is not null),check(status<>'REJECTED' or rejection_reason is not null));
create table if not exists quality.corrective_action_evidences(id uuid primary key,tenant_id uuid not null,corrective_action_id uuid not null,document_id uuid not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,corrective_action_id) references quality.corrective_actions(tenant_id,id));
create table if not exists quality.compliance_audits(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),audit_type varchar(40) not null,title varchar(180) not null,scope_description text not null,responsible_id uuid not null,start_at timestamptz not null,end_at timestamptz,status varchar(20) not null check(status in('PLANNED','IN_PROGRESS','IN_REVIEW','APPROVED','REJECTED','CLOSED','CANCELLED')),report text,decision_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),check(end_at is null or end_at>=start_at),check(status<>'REJECTED' or decision_reason is not null));
create table if not exists quality.compliance_audit_scopes(id uuid primary key,tenant_id uuid not null,audit_id uuid not null,entity_type varchar(40) not null,entity_id uuid not null,description text not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,audit_id) references quality.compliance_audits(tenant_id,id));
create table if not exists quality.compliance_audit_checklists(id uuid primary key,tenant_id uuid not null,audit_id uuid not null,requirement_id uuid,item varchar(300) not null,required boolean not null default true,status varchar(20) not null check(status in('PENDING','COMPLIANT','NON_COMPLIANT','NOT_APPLICABLE')),notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,audit_id) references quality.compliance_audits(tenant_id,id));
create table if not exists quality.compliance_audit_findings(id uuid primary key,tenant_id uuid not null,audit_id uuid not null,title varchar(180) not null,description text not null,severity varchar(12) not null check(severity in('LOW','MEDIUM','HIGH','CRITICAL')),non_conformity_id uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,audit_id) references quality.compliance_audits(tenant_id,id));
create table if not exists quality.compliance_audit_evidences(id uuid primary key,tenant_id uuid not null,audit_id uuid not null,document_id uuid not null,description text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,audit_id) references quality.compliance_audits(tenant_id,id));
create table if not exists quality.processing_compliance_rules(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),product_id uuid not null,step_name varchar(160) not null,minimum_minutes int check(minimum_minutes>=0),minimum_temperature numeric(8,2),evidence_required boolean not null default false,responsible_required boolean not null default false,checklist_required boolean not null default false,photo_required boolean not null default false,approval_required boolean not null default false,block_without_record boolean not null default true,status varchar(12) not null check(status in('DRAFT','ACTIVE','INACTIVE')),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id));
create table if not exists quality.processing_compliance_records(id uuid primary key,tenant_id uuid not null,rule_id uuid not null,lot_id uuid not null,duration_minutes int check(duration_minutes>=0),temperature numeric(8,2),responsible_id uuid,evidence_document_id uuid,checklist_completed boolean not null default false,recorded_at timestamptz not null,status varchar(20) not null check(status in('PENDING','COMPLIANT','NON_COMPLIANT')),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,rule_id) references quality.processing_compliance_rules(tenant_id,id));
create table if not exists quality.export_readiness_checks(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),dossier_id uuid,lot_id uuid not null,market varchar(80) not null,status varchar(24) not null check(status in('PENDING','READY','BLOCKED','EXPIRED')),evaluated_at timestamptz,evaluated_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id));
create table if not exists quality.export_readiness_items(id uuid primary key,tenant_id uuid not null,check_id uuid not null,requirement_id uuid,label varchar(240) not null,required boolean not null default true,status varchar(20) not null check(status in('PENDING','COMPLIANT','NON_COMPLIANT','NOT_APPLICABLE')),reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,check_id) references quality.export_readiness_checks(tenant_id,id));
create index if not exists ix_quality_requirements_queue on quality.compliance_requirements(tenant_id,status,severity,valid_until) where deleted_at is null;
create index if not exists ix_quality_requirement_entity on quality.compliance_requirement_entities(tenant_id,entity_type,entity_id,status);
create index if not exists ix_quality_spec_product on quality.quality_specifications(tenant_id,product_id,status,valid_until) where deleted_at is null;
create index if not exists ix_quality_inspection_lot on quality.quality_inspections(tenant_id,lot_id,status,inspected_at desc) where deleted_at is null;
create index if not exists ix_quality_lot_history on quality.lot_quality_status_history(tenant_id,lot_id,created_at desc);
create index if not exists ix_quality_nc_queue on quality.non_conformities(tenant_id,status,severity,due_on) where deleted_at is null;
create index if not exists ix_quality_nc_entity on quality.non_conformities(tenant_id,entity_type,entity_id) where deleted_at is null;
create index if not exists ix_quality_actions_queue on quality.corrective_actions(tenant_id,status,due_on) where deleted_at is null;
create index if not exists ix_quality_audits_queue on quality.compliance_audits(tenant_id,status,start_at) where deleted_at is null;
create index if not exists ix_quality_processing_lot on quality.processing_compliance_records(tenant_id,lot_id,status,recorded_at desc) where deleted_at is null;
create index if not exists ix_quality_export_lot on quality.export_readiness_checks(tenant_id,lot_id,status) where deleted_at is null;
do $$ declare t text; begin foreach t in array array['compliance_requirements','compliance_requirement_entities','compliance_requirement_evidences','compliance_requirement_events','quality_specifications','quality_specification_parameters','quality_specification_versions','quality_inspections','quality_inspection_parameters','quality_inspection_results','lot_quality_status_history','lot_quality_holds','non_conformities','non_conformity_events','corrective_actions','corrective_action_evidences','compliance_audits','compliance_audit_scopes','compliance_audit_checklists','compliance_audit_findings','compliance_audit_evidences','processing_compliance_rules','processing_compliance_records','export_readiness_checks','export_readiness_items'] loop execute format('alter table quality.%I enable row level security',t);execute format('drop policy if exists tenant_isolation on quality.%I',t);execute format('create policy tenant_isolation on quality.%I using (tenant_id=platform.current_tenant_id()) with check (tenant_id=platform.current_tenant_id())',t);end loop;end $$;
insert into platform.schema_versions(version,description,installed_at) values('2.8.0','Sprint 28 - Qualidade e Compliance',now()) on conflict(version) do update set description=excluded.description;
commit;

-- Sprint 29 - administracao SaaS, assinaturas, limites, onboarding e white label
begin;
create schema if not exists saas;

alter table saas.plans add column if not exists max_producers int not null default 10 check(max_producers>0);
alter table saas.plans add column if not exists max_customers int not null default 10 check(max_customers>0);
alter table saas.plans add column if not exists max_suppliers int not null default 10 check(max_suppliers>0);
alter table saas.plans add column if not exists max_documents int not null default 100 check(max_documents>0);
alter table saas.plans add column if not exists max_integrations int not null default 1 check(max_integrations>0);
alter table saas.plans add column if not exists max_offline_records_month int not null default 1000 check(max_offline_records_month>0);
alter table saas.plans add column if not exists support_level varchar(40) not null default 'STANDARD';
alter table saas.plans add column if not exists sla_hours int check(sla_hours>0);
alter table saas.plans add column if not exists trial_allowed boolean not null default false;
alter table saas.plans add column if not exists trial_days int not null default 0 check(trial_days>=0);
alter table saas.plans add column if not exists white_label_allowed boolean not null default false;
alter table saas.plans add column if not exists external_portal_allowed boolean not null default false;
alter table saas.plans add column if not exists external_api_allowed boolean not null default false;
alter table saas.plans add column if not exists advanced_compliance_allowed boolean not null default false;
alter table saas.plans add column if not exists created_by uuid;
alter table saas.plans add column if not exists updated_by uuid;
alter table saas.plans add column if not exists deleted_at timestamptz;

create table if not exists saas.tenant_status_events(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), previous_status varchar(20) not null,
 new_status varchar(20) not null check(new_status in('IMPLEMENTING','TRIAL','ACTIVE','SUSPENDED','INACTIVE','CANCELLED')),
 reason varchar(1000) not null check(length(trim(reason))>=5), created_at timestamptz not null default now(), created_by uuid not null);
create index if not exists ix_saas_tenant_status_events on saas.tenant_status_events(tenant_id,created_at desc);

create table if not exists saas.feature_flags(
 id uuid primary key default gen_random_uuid(), code varchar(80) not null unique, name varchar(120) not null, description varchar(500) not null,
 beta boolean not null default false, active boolean not null default true, created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz);
create table if not exists saas.plan_features(
 plan_id uuid not null references saas.plans(id), feature_id uuid not null references saas.feature_flags(id), enabled boolean not null default true,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, primary key(plan_id,feature_id));
create index if not exists ix_saas_plan_features_feature on saas.plan_features(feature_id,plan_id);
create table if not exists saas.plan_limits(
 plan_id uuid not null references saas.plans(id), metric_code varchar(80) not null, limit_value bigint not null check(limit_value>0), block_at_limit boolean not null default true,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, primary key(plan_id,metric_code));

create table if not exists saas.subscriptions(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null references tenancy.tenants(id), plan_id uuid not null references saas.plans(id),
 status varchar(20) not null check(status in('TRIAL','ACTIVE','EXPIRED','SUSPENDED','CANCELLED','NEGOTIATING','COURTESY')),
 cycle varchar(20) not null check(cycle in('MONTHLY','ANNUAL','TRIAL','LICENSE','COURTESY')), starts_on date not null, ends_on date,
 contracted_value numeric(14,2) not null check(contracted_value>=0), discount numeric(14,2) not null default 0 check(discount>=0 and discount<=contracted_value),
 discount_reason varchar(1000), due_day smallint check(due_day between 1 and 28), auto_renew boolean not null default false, contracted_by uuid,
 notes varchar(2000), cancellation_reason varchar(1000), grace_days int not null default 5 check(grace_days>=0), created_at timestamptz not null default now(),
 updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid, deleted_at timestamptz,
 check(ends_on is null or ends_on>starts_on), check(discount=0 or length(trim(coalesce(discount_reason,'')))>=5),
 check(status<>'CANCELLED' or length(trim(coalesce(cancellation_reason,'')))>=5));
create index if not exists ix_saas_subscriptions_tenant_status on saas.subscriptions(tenant_id,status);
create index if not exists ix_saas_subscriptions_plan on saas.subscriptions(plan_id,status);
create table if not exists saas.subscription_events(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null references tenancy.tenants(id), subscription_id uuid not null references saas.subscriptions(id),
 event_type varchar(60) not null, previous_value jsonb not null default '{}', new_value jsonb not null default '{}', reason varchar(1000), created_at timestamptz not null default now(), created_by uuid not null);
create index if not exists ix_saas_subscription_events on saas.subscription_events(tenant_id,subscription_id,created_at desc);

create table if not exists saas.billing_charges(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null references tenancy.tenants(id), subscription_id uuid not null references saas.subscriptions(id),
 competence date not null check(competence=date_trunc('month',competence)::date), due_on date not null, amount numeric(14,2) not null check(amount>0),
 status varchar(20) not null default 'OPEN' check(status in('OPEN','ISSUED','PAID','OVERDUE','CANCELLED')), paid_on date, payment_method varchar(80),
 external_reference varchar(160), notes varchar(2000), cancellation_reason varchar(1000), created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
 created_by uuid not null, updated_by uuid, deleted_at timestamptz, check(status<>'PAID' or paid_on is not null),
 check(status<>'CANCELLED' or length(trim(coalesce(cancellation_reason,'')))>=5));
create unique index if not exists ux_saas_charge_competence on saas.billing_charges(subscription_id,competence) where status<>'CANCELLED' and deleted_at is null;
create index if not exists ix_saas_charges_tenant_status_due on saas.billing_charges(tenant_id,status,due_on);
create table if not exists saas.billing_charge_events(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null references tenancy.tenants(id), charge_id uuid not null references saas.billing_charges(id),
 event_type varchar(50) not null, details jsonb not null default '{}', reason varchar(1000), created_at timestamptz not null default now(), created_by uuid not null);

create table if not exists saas.tenant_feature_flags(
 tenant_id uuid not null references tenancy.tenants(id), feature_id uuid not null references saas.feature_flags(id), enabled boolean not null,
 origin varchar(30) not null check(origin in('PLAN','MANUAL_OVERRIDE','TRIAL','BETA','ADMIN_BLOCK')), reason varchar(1000), expires_at timestamptz,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid, primary key(tenant_id,feature_id),
 check(origin='PLAN' or length(trim(coalesce(reason,'')))>=5));
create index if not exists ix_saas_tenant_features_feature on saas.tenant_feature_flags(feature_id,tenant_id);
alter table saas.usage_metrics add column if not exists active_users bigint not null default 0;
alter table saas.usage_metrics add column if not exists properties bigint not null default 0;
alter table saas.usage_metrics add column if not exists producers bigint not null default 0;
alter table saas.usage_metrics add column if not exists customers bigint not null default 0;
alter table saas.usage_metrics add column if not exists suppliers bigint not null default 0;
alter table saas.usage_metrics add column if not exists documents bigint not null default 0;
alter table saas.usage_metrics add column if not exists integrations bigint not null default 0;
create table if not exists saas.usage_snapshots(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null references tenancy.tenants(id), metric_code varchar(80) not null,
 value bigint not null check(value>=0), limit_value bigint not null check(limit_value>0), measured_at timestamptz not null default now(), created_at timestamptz not null default now(), created_by uuid);
create index if not exists ix_saas_usage_snapshots on saas.usage_snapshots(tenant_id,metric_code,measured_at desc);
create table if not exists saas.limit_overrides(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null references tenancy.tenants(id), metric_code varchar(80) not null, limit_value bigint not null check(limit_value>0),
 reason varchar(1000) not null check(length(trim(reason))>=5), starts_at timestamptz not null default now(), expires_at timestamptz not null,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid, deleted_at timestamptz, check(expires_at>starts_at));
create index if not exists ix_saas_limit_overrides_active on saas.limit_overrides(tenant_id,metric_code,expires_at) where deleted_at is null;

create table if not exists saas.onboarding_templates(
 id uuid primary key default gen_random_uuid(), name varchar(120) not null, active boolean not null default true, created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid);
create table if not exists saas.onboarding_steps(
 id uuid primary key default gen_random_uuid(), template_id uuid not null references saas.onboarding_templates(id), code varchar(80) not null, title varchar(160) not null,
 description varchar(500) not null, position int not null check(position>0), required boolean not null default true, feature_code varchar(80),
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, unique(template_id,code),unique(template_id,position));
create table if not exists saas.tenant_onboarding_progress(
 tenant_id uuid not null references tenancy.tenants(id), step_id uuid not null references saas.onboarding_steps(id), status varchar(20) not null check(status in('PENDING','IN_PROGRESS','COMPLETED','SKIPPED')),
 data jsonb not null default '{}', completed_at timestamptz, completed_by uuid, created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid,
 primary key(tenant_id,step_id), check(status<>'COMPLETED' or (completed_at is not null and completed_by is not null)));
create index if not exists ix_saas_onboarding_pending on saas.tenant_onboarding_progress(tenant_id,status);
create table if not exists saas.tenant_branding(
 tenant_id uuid primary key references tenancy.tenants(id), display_name varchar(160) not null, logo_storage_key varchar(500), logo_content_type varchar(80), logo_size_bytes bigint check(logo_size_bytes between 1 and 2097152),
 primary_color char(7) not null default '#174C3C', secondary_color char(7) not null default '#102A25', accent_color char(7) not null default '#D6A84B',
 contact_email varchar(254), institutional_text varchar(500), public_url varchar(500), show_on_portal boolean not null default true, show_on_documents boolean not null default false,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid,
 check(primary_color~'^#[0-9A-Fa-f]{6}$' and secondary_color~'^#[0-9A-Fa-f]{6}$' and accent_color~'^#[0-9A-Fa-f]{6}$'),
 check(logo_storage_key is null or logo_content_type in('image/png','image/jpeg','image/webp','image/svg+xml')));

create table if not exists saas.admin_audit_events(
 id uuid primary key default gen_random_uuid(), tenant_id uuid references tenancy.tenants(id), actor_id uuid not null, action varchar(100) not null,
 entity_type varchar(80) not null, entity_id uuid, reason varchar(1000), safe_details jsonb not null default '{}', correlation_id varchar(100), ip_hash char(64), created_at timestamptz not null default now());
create index if not exists ix_saas_admin_audit_tenant on saas.admin_audit_events(tenant_id,created_at desc);
create index if not exists ix_saas_admin_audit_action on saas.admin_audit_events(action,created_at desc);
create table if not exists saas.permission_catalog(code varchar(100) primary key, name varchar(160) not null, description varchar(500) not null, critical boolean not null default false, created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid);
create table if not exists saas.role_permissions(tenant_id uuid not null references tenancy.tenants(id), role_id uuid not null, permission_code varchar(100) not null references saas.permission_catalog(code), created_at timestamptz not null default now(), created_by uuid not null, primary key(tenant_id,role_id,permission_code), foreign key(tenant_id,role_id) references identity.roles(tenant_id,id));

insert into saas.feature_flags(code,name,description) values
 ('agriculture','Producao agricola','Planejamento e operacao agricola.'),('livestock','Pecuaria','Gestao pecuaria.'),('inventory','Estoque','Estoque e armazenagem.'),
 ('commercial','Comercial','Vendas e contratos.'),('finance','Financeiro','Gestao financeira.'),('logistics','Logistica','Entregas e transportes.'),
 ('traceability','Rastreabilidade','Lotes e cadeia de custodia.'),('documents','Documentos','Evidencias e documentos.'),('advanced-bi','BI avancado','Indicadores executivos.'),
 ('external-portal','Portal externo','Autosservico B2B.'),('marketplace','Marketplace','Ofertas comerciais.'),('mobile-offline','Mobile e offline','Operacao de campo offline.'),
 ('advanced-compliance','Compliance avancado','Qualidade, auditoria e ESG.'),('export','Exportacao','Exportacao administrativa.'),('external-api','API externa','Integracao por API.'),('white-label','White label','Identidade visual por tenant.')
on conflict(code) do update set name=excluded.name,description=excluded.description;
insert into saas.permission_catalog(code,name,description,critical) values
 ('saas.tenants.manage','Gerenciar tenants','Criar e editar tenants.',true),('saas.tenants.suspend','Suspender tenant','Suspender acesso operacional.',true),
 ('saas.plans.manage','Gerenciar planos','Editar recursos e limites.',true),('saas.subscriptions.manage','Gerenciar assinaturas','Alterar plano, valor e ciclo.',true),
 ('saas.billing.manage','Gerenciar cobrancas','Emitir, baixar manualmente e cancelar.',true),('saas.features.manage','Gerenciar features','Conceder ou bloquear recursos.',true),
 ('saas.limits.override','Conceder override','Alterar limite temporariamente.',true),('saas.branding.manage','Gerenciar white label','Editar identidade visual.',false),
 ('saas.onboarding.manage','Gerenciar onboarding','Reabrir e acompanhar onboarding.',false),('saas.audit.read','Acessar auditoria SaaS','Consultar eventos administrativos.',true),
 ('saas.export','Exportar dados administrativos','Exportar dados autorizados.',true)
on conflict(code) do update set name=excluded.name,description=excluded.description,critical=excluded.critical;

do $$ declare tab text; begin foreach tab in array array['tenant_status_events','subscriptions','subscription_events','billing_charges','billing_charge_events','tenant_feature_flags','usage_metrics','usage_snapshots','limit_overrides','tenant_onboarding_progress','tenant_branding','role_permissions'] loop
 execute format('alter table saas.%I enable row level security',tab);
 execute format('drop policy if exists tenant_isolation on saas.%I',tab);
 execute format('create policy tenant_isolation on saas.%I using (tenant_id=platform.current_tenant_id()) with check (tenant_id=platform.current_tenant_id())',tab);
end loop; end $$;
insert into platform.schema_versions(version,description,installed_at) values('2.9.0','Sprint 29 - Administracao SaaS, planos, assinaturas, onboarding e white label',now()) on conflict(version) do nothing;
commit;
-- Sprint 30 — integrações externas, import/export e fundação fiscal (PostgreSQL 16+)
begin;
create schema if not exists fiscal;

-- O legado integrations.integrations continua compatível; estes nomes formam o contrato canônico novo.
create table if not exists integrations.integration_connectors(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(120) not null,type varchar(32) not null check(type in('ERP','FISCAL','LOGISTICS','FINANCIAL','MARKETPLACE','BI','COOPERATIVE','EXTERNAL_PORTAL','GOVERNMENT','WEBHOOK','EXTERNAL_API','OTHER')),provider varchar(80),endpoint_url varchar(1000),authentication_type varchar(30) not null default 'NONE',status varchar(30) not null default 'NOT_CONFIGURED' check(status in('NOT_CONFIGURED','ACTIVE','INACTIVE','ERROR')),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,name),check(endpoint_url is null or endpoint_url ~ '^https?://'));
create table if not exists integrations.integration_connector_settings(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,connector_id uuid not null,setting_key varchar(100) not null,setting_value jsonb not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,connector_id,setting_key),foreign key(tenant_id,connector_id) references integrations.integration_connectors(tenant_id,id) on delete cascade);
create table if not exists integrations.integration_credentials(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,connector_id uuid not null,secret_reference varchar(500) not null,key_version int not null default 1 check(key_version>0),expires_at timestamptz,revoked_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,connector_id) references integrations.integration_connectors(tenant_id,id));
create table if not exists integrations.integration_outbox(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,connector_id uuid not null,idempotency_key varchar(160) not null,event_type varchar(100) not null,payload jsonb not null,status varchar(20) not null default 'PENDING' check(status in('PENDING','PROCESSING','SENT','FAILED','CANCELLED')),attempts int not null default 0 check(attempts between 0 and 20),available_at timestamptz not null default now(),last_error varchar(1000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,connector_id,idempotency_key),foreign key(tenant_id,connector_id) references integrations.integration_connectors(tenant_id,id));
create table if not exists integrations.integration_inbox(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,connector_id uuid not null,idempotency_key varchar(160) not null,payload_hash char(64) not null,status varchar(20) not null default 'RECEIVED' check(status in('RECEIVED','PROCESSING','PROCESSED','FAILED','IGNORED')),received_at timestamptz not null default now(),processed_at timestamptz,last_error varchar(1000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,connector_id,idempotency_key),foreign key(tenant_id,connector_id) references integrations.integration_connectors(tenant_id,id));
create table if not exists integrations.integration_retry_attempts(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,outbox_id uuid not null,attempt_number int not null check(attempt_number between 1 and 20),status varchar(20) not null,response_code int,error_code varchar(80),safe_error varchar(1000),attempted_at timestamptz not null default now(),next_attempt_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,outbox_id,attempt_number),foreign key(tenant_id,outbox_id) references integrations.integration_outbox(tenant_id,id));

create table if not exists integrations.external_api_applications(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(120) not null,status varchar(20) not null default 'ACTIVE' check(status in('ACTIVE','SUSPENDED','REVOKED')),rate_limit_per_minute int not null default 60 check(rate_limit_per_minute between 1 and 10000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,name));
create table if not exists integrations.external_api_keys(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,application_id uuid not null,key_prefix varchar(20) not null,key_hash char(64) not null unique,status varchar(20) not null default 'ACTIVE' check(status in('ACTIVE','REVOKED','EXPIRED')),expires_at timestamptz,last_used_at timestamptz,revoked_at timestamptz,revoked_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,application_id) references integrations.external_api_applications(tenant_id,id));
create table if not exists integrations.external_api_scopes(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,application_id uuid not null,scope varchar(80) not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,application_id,scope),foreign key(tenant_id,application_id) references integrations.external_api_applications(tenant_id,id));
create table if not exists integrations.external_api_request_logs(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,application_id uuid not null,method varchar(10) not null,path varchar(500) not null,status_code int not null,duration_ms int not null check(duration_ms>=0),correlation_id varchar(100) not null,occurred_at timestamptz not null default now(),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,application_id) references integrations.external_api_applications(tenant_id,id));

alter table integrations.api_keys add column if not exists revoked_at timestamptz, add column if not exists revoked_by uuid;
alter table integrations.webhook_events drop constraint if exists webhook_events_status_check;
alter table integrations.webhook_events add constraint webhook_events_status_check check(status in('PENDING','PROCESSING','SENT','FAILED','CANCELLED'));

create table if not exists integrations.import_jobs(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),entity_type varchar(40) not null,status varchar(20) not null default 'UPLOADED' check(status in('UPLOADED','MAPPING','VALIDATING','READY','PROCESSING','COMPLETED','PARTIAL','FAILED','CANCELLED')),total_rows int not null default 0 check(total_rows>=0),valid_rows int not null default 0 check(valid_rows>=0),error_rows int not null default 0 check(error_rows>=0),progress smallint not null default 0 check(progress between 0 and 100),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,id));
create table if not exists integrations.import_job_files(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,import_job_id uuid not null,file_name varchar(240) not null,content_type varchar(100) not null,storage_reference varchar(500) not null,sha256 char(64) not null,size_bytes bigint not null check(size_bytes>0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,import_job_id) references integrations.import_jobs(tenant_id,id));
create table if not exists integrations.import_job_mappings(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,import_job_id uuid not null,source_column varchar(160) not null,target_field varchar(160) not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,import_job_id,target_field),foreign key(tenant_id,import_job_id) references integrations.import_jobs(tenant_id,id));
create table if not exists integrations.import_job_rows(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,import_job_id uuid not null,row_number int not null check(row_number>0),data jsonb not null,valid boolean not null,errors jsonb not null default '[]',natural_key varchar(300),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,import_job_id,row_number),foreign key(tenant_id,import_job_id) references integrations.import_jobs(tenant_id,id));
create table if not exists integrations.export_jobs(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),entity_type varchar(40) not null,format varchar(10) not null check(format in('CSV','JSON','XLSX','PDF')),filters jsonb not null default '{}',status varchar(20) not null default 'PENDING' check(status in('PENDING','PROCESSING','COMPLETED','FAILED','EXPIRED')),row_count int not null default 0 check(row_count>=0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,id));
create table if not exists integrations.export_job_files(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,export_job_id uuid not null,file_name varchar(240) not null,storage_reference varchar(500) not null,sha256 char(64) not null,size_bytes bigint not null check(size_bytes>=0),expires_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,export_job_id) references integrations.export_jobs(tenant_id,id));

create table if not exists fiscal.fiscal_documents(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),document_type varchar(30) not null check(document_type in('NFE','NFCE','NFSE','CTE','MDFE','PRODUCER_INVOICE','FISCAL_WAYBILL','TRANSPORT_DOCUMENT','EXPORT_DOCUMENT','OTHER')),access_key varchar(44),number varchar(30),series varchar(10),issuer_document varchar(20) not null,recipient_document varchar(20) not null,total_amount numeric(18,2) not null check(total_amount>=0),status varchar(30) not null default 'DRAFT' check(status in('DRAFT','IMPORTED','INTERNALLY_VALIDATED','PENDING_SUBMISSION','SENT_TO_PROVIDER','AUTHORIZED','REJECTED','CANCELLED','DENIED','VOIDED','INTEGRATION_ERROR')),provider_protocol varchar(100),provider_response_hash char(64),rejection_reason varchar(1000),cancellation_reason varchar(1000),order_id uuid,contract_id uuid,lot_id uuid,delivery_id uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id));
create unique index if not exists ux_fiscal_access_key on fiscal.fiscal_documents(tenant_id,access_key) where access_key is not null;
create table if not exists fiscal.fiscal_document_items(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,document_id uuid not null,line_number int not null check(line_number>0),description varchar(300) not null,ncm varchar(8),cfop varchar(4),unit varchar(10) not null,quantity numeric(18,4) not null check(quantity>0),unit_value numeric(18,6) not null check(unit_value>=0),total_value numeric(18,2) not null check(total_value>=0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,document_id,line_number),foreign key(tenant_id,document_id) references fiscal.fiscal_documents(tenant_id,id));
create table if not exists fiscal.fiscal_document_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,document_id uuid not null,event_type varchar(50) not null,from_status varchar(30),to_status varchar(30),safe_message varchar(1000),provider_protocol varchar(100),occurred_at timestamptz not null default now(),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,document_id) references fiscal.fiscal_documents(tenant_id,id));
create table if not exists fiscal.fiscal_document_xml_files(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,document_id uuid not null,storage_reference varchar(500) not null,sha256 char(64) not null,size_bytes bigint not null check(size_bytes>0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,document_id,sha256),foreign key(tenant_id,document_id) references fiscal.fiscal_documents(tenant_id,id));
create table if not exists fiscal.fiscal_drafts(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),order_id uuid not null,status varchar(20) not null default 'DRAFT' check(status in('DRAFT','PENDING','READY','QUEUED','CANCELLED')),total_amount numeric(18,2) not null check(total_amount>=0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,id),unique(tenant_id,order_id));
create table if not exists fiscal.fiscal_draft_items(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,draft_id uuid not null,product_id uuid not null,description varchar(300) not null,quantity numeric(18,4) not null check(quantity>0),unit_value numeric(18,6) not null check(unit_value>=0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,draft_id) references fiscal.fiscal_drafts(tenant_id,id));
create table if not exists fiscal.fiscal_pending_issues(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,draft_id uuid not null,code varchar(80) not null,message varchar(500) not null,blocking boolean not null default true,resolved_at timestamptz,resolved_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,draft_id) references fiscal.fiscal_drafts(tenant_id,id));
create table if not exists fiscal.fiscal_product_settings(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),product_id uuid not null,ncm varchar(8),default_cfop varchar(4),fiscal_unit varchar(10) not null,origin smallint not null check(origin between 0 and 8),internal_code varchar(60) not null,barcode varchar(14),taxation jsonb not null default '{}',notes varchar(1000),active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,product_id),unique(tenant_id,internal_code));
create table if not exists fiscal.fiscal_participant_settings(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),participant_type varchar(20) not null check(participant_type in('CUSTOMER','PRODUCER','SUPPLIER','CARRIER')),participant_id uuid not null,tax_document varchar(20) not null,state_registration varchar(30),municipal_registration varchar(30),taxpayer_type varchar(20) not null,fiscal_address jsonb not null,fiscal_email varchar(254),active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,participant_type,participant_id));
create table if not exists fiscal.fiscal_integration_providers(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(120) not null,environment varchar(20) not null check(environment in('HOMOLOGATION','PRODUCTION')),endpoint_url varchar(1000),credential_reference varchar(500),certificate_reference varchar(500),status varchar(30) not null default 'NOT_CONFIGURED' check(status in('NOT_CONFIGURED','ACTIVE','INACTIVE','ERROR')),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,id),check(endpoint_url is null or endpoint_url ~ '^https://'));

create index if not exists ix_connector_queue on integrations.integration_connectors(tenant_id,status,type,updated_at desc);
create index if not exists ix_outbox_queue_s30 on integrations.integration_outbox(tenant_id,status,available_at);
create index if not exists ix_api_apps_status on integrations.external_api_applications(tenant_id,status,created_at desc);
create index if not exists ix_api_requests_app on integrations.external_api_request_logs(tenant_id,application_id,occurred_at desc);
create index if not exists ix_import_jobs_queue on integrations.import_jobs(tenant_id,status,created_at desc);
create index if not exists ix_export_jobs_queue on integrations.export_jobs(tenant_id,status,created_at desc);
create index if not exists ix_fiscal_documents_queue on fiscal.fiscal_documents(tenant_id,status,document_type,created_at desc);
create index if not exists ix_fiscal_drafts_queue on fiscal.fiscal_drafts(tenant_id,status,created_at desc);
create index if not exists ix_fiscal_issues_open on fiscal.fiscal_pending_issues(tenant_id,draft_id,blocking) where resolved_at is null;

do $$ declare sch text; tab text; begin foreach sch in array array['integrations','fiscal'] loop for tab in select tablename from pg_tables where schemaname=sch and exists(select 1 from information_schema.columns where table_schema=sch and table_name=tablename and column_name='tenant_id') loop execute format('alter table %I.%I enable row level security',sch,tab); execute format('alter table %I.%I force row level security',sch,tab); if not exists(select 1 from pg_policies where schemaname=sch and tablename=tab and policyname=tab||'_tenant') then execute format('create policy %I on %I.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',tab||'_tenant',sch,tab); end if; end loop; end loop; end $$;
insert into identity.permissions(code,module,description) values ('external-api.manage','API Externa','Gerenciar aplicações, chaves e escopos.'),('fiscal.read','Fiscal','Consultar documentos fiscais e XML autorizado.'),('fiscal.write','Fiscal','Importar documentos e preparar rascunhos fiscais.'),('fiscal.xml.download','Fiscal','Baixar XML fiscal protegido.'),('exports.create','Integrações','Gerar exportações de dados do tenant.') on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('3.0.0','Sprint 30 - Integracoes, API externa, webhooks, import/export e fiscal',now()) on conflict(version) do nothing;

-- Sprint 31: inteligencia operacional rastreavel (sem dependencia de IA externa).
create table if not exists intelligence.intelligence_rules(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(160) not null,description varchar(1000) not null,module varchar(60) not null,type varchar(40) not null,condition_definition jsonb not null,severity varchar(12) not null check(severity in('LOW','ATTENTION','HIGH','CRITICAL')),weight numeric(8,2) not null check(weight>0),suggested_action varchar(1000) not null,active boolean not null default true,periodicity_minutes int not null check(periodicity_minutes>0),required_permission varchar(120),creates_task boolean not null default false,creates_workflow boolean not null default false,last_executed_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,name));
create table if not exists intelligence.intelligence_rule_executions(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,rule_id uuid not null,status varchar(16) not null check(status in('RUNNING','SUCCEEDED','FAILED','SKIPPED')),matched_count int not null default 0 check(matched_count>=0),safe_error varchar(1000),started_at timestamptz not null,finished_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,rule_id) references intelligence.intelligence_rules(tenant_id,id));
create table if not exists intelligence.intelligence_recommendations(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),rule_id uuid,module varchar(60) not null,type varchar(40) not null,title varchar(240) not null,reason varchar(1200) not null,suggested_action varchar(1000) not null,severity varchar(12) not null check(severity in('LOW','ATTENTION','HIGH','CRITICAL')),status varchar(16) not null default 'OPEN' check(status in('OPEN','ACCEPTED','REJECTED','IGNORED','ARCHIVED')),entity_type varchar(60) not null,entity_id uuid not null,fingerprint char(64) not null,impact_amount numeric(18,2),due_at timestamptz,assigned_to uuid,decided_at timestamptz,decided_by uuid,decision_reason varchar(1000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,rule_id) references intelligence.intelligence_rules(tenant_id,id));
create unique index if not exists ux_intelligence_recommendation_open on intelligence.intelligence_recommendations(tenant_id,fingerprint) where status='OPEN' and deleted_at is null;
create table if not exists intelligence.intelligence_recommendation_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,recommendation_id uuid not null,event_type varchar(30) not null,reason varchar(1000),task_id uuid,workflow_id uuid,occurred_at timestamptz not null default now(),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,recommendation_id) references intelligence.intelligence_recommendations(tenant_id,id));
create table if not exists intelligence.intelligence_recommendation_sources(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,recommendation_id uuid not null,source_module varchar(60) not null,source_entity_type varchar(60) not null,source_entity_id uuid not null,observed_value varchar(300),reference_value varchar(300),observed_at timestamptz not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,recommendation_id) references intelligence.intelligence_recommendations(tenant_id,id));
create table if not exists intelligence.intelligence_scores(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),entity_type varchar(60) not null,entity_id uuid not null,module varchar(60) not null,operational_score numeric(5,2) not null default 0 check(operational_score between 0 and 100),financial_score numeric(5,2) not null default 0 check(financial_score between 0 and 100),logistics_score numeric(5,2) not null default 0 check(logistics_score between 0 and 100),commercial_score numeric(5,2) not null default 0 check(commercial_score between 0 and 100),documentary_score numeric(5,2) not null default 0 check(documentary_score between 0 and 100),fiscal_score numeric(5,2) not null default 0 check(fiscal_score between 0 and 100),quality_score numeric(5,2) not null default 0 check(quality_score between 0 and 100),traceability_score numeric(5,2) not null default 0 check(traceability_score between 0 and 100),overall_score numeric(5,2) not null check(overall_score between 0 and 100),risk_band varchar(12) not null check(risk_band in('LOW','ATTENTION','HIGH','CRITICAL')),formula_version varchar(30) not null,calculated_at timestamptz not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,entity_type,entity_id));
create table if not exists intelligence.intelligence_score_factors(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,score_id uuid not null,code varchar(100) not null,description varchar(500) not null,observed_value numeric(18,4) not null,reference_value numeric(18,4),weight numeric(8,2) not null check(weight>0),contribution numeric(8,2) not null,source_module varchar(60) not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,score_id) references intelligence.intelligence_scores(tenant_id,id));
create table if not exists intelligence.intelligence_score_history(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,score_id uuid not null,overall_score numeric(5,2) not null check(overall_score between 0 and 100),risk_band varchar(12) not null check(risk_band in('LOW','ATTENTION','HIGH','CRITICAL')),formula_version varchar(30) not null,calculated_at timestamptz not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,score_id) references intelligence.intelligence_scores(tenant_id,id));
create table if not exists intelligence.intelligence_anomalies(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),module varchar(60) not null,type varchar(80) not null,title varchar(240) not null,criterion varchar(1000) not null,observed_value numeric(18,4) not null,reference_value numeric(18,4) not null,severity varchar(12) not null check(severity in('LOW','ATTENTION','HIGH','CRITICAL')),status varchar(16) not null default 'OPEN' check(status in('OPEN','ACKNOWLEDGED','ARCHIVED')),entity_type varchar(60) not null,entity_id uuid not null,fingerprint char(64) not null,detected_at timestamptz not null,archived_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id));
create unique index if not exists ux_intelligence_anomaly_open on intelligence.intelligence_anomalies(tenant_id,fingerprint) where status='OPEN' and deleted_at is null;
create table if not exists intelligence.intelligence_anomaly_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,anomaly_id uuid not null,event_type varchar(30) not null,reason varchar(1000),occurred_at timestamptz not null default now(),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,anomaly_id) references intelligence.intelligence_anomalies(tenant_id,id));
create table if not exists intelligence.intelligence_priority_items(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),source_type varchar(60) not null,source_id uuid not null,module varchar(60) not null,title varchar(240) not null,severity varchar(12) not null check(severity in('LOW','ATTENTION','HIGH','CRITICAL')),priority_score numeric(8,2) not null check(priority_score between 0 and 100),impact_amount numeric(18,2),due_at timestamptz,assigned_to uuid,status varchar(16) not null default 'OPEN' check(status in('OPEN','COMPLETED','ARCHIVED')),action_url varchar(500) not null check(action_url like '/%'),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,source_type,source_id));
create table if not exists intelligence.intelligence_assistant_sessions(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),user_id uuid not null,title varchar(160) not null,status varchar(16) not null default 'ACTIVE' check(status in('ACTIVE','CLOSED','ARCHIVED')),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,id));
create table if not exists intelligence.intelligence_assistant_messages(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,session_id uuid not null,role varchar(12) not null check(role in('USER','ASSISTANT')),intent varchar(80),content varchar(4000) not null,provider_used boolean not null default false,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,session_id) references intelligence.intelligence_assistant_sessions(tenant_id,id));
create table if not exists intelligence.intelligence_assistant_sources(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,message_id uuid not null,source_module varchar(60) not null,entity_type varchar(60),entity_id uuid,display_label varchar(300) not null,action_url varchar(500),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,message_id) references intelligence.intelligence_assistant_messages(tenant_id,id));
create table if not exists intelligence.intelligence_feedback(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),target_type varchar(30) not null,target_id uuid not null,rating smallint check(rating between 1 and 5),comment varchar(1000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null);
create table if not exists intelligence.intelligence_provider_settings(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),provider varchar(60) not null,enabled boolean not null default false,allow_sensitive_data boolean not null default false,credential_reference varchar(500),endpoint_url varchar(1000),model_name varchar(120),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id),check(not enabled or (credential_reference is not null and endpoint_url ~ '^https://')),check(credential_reference is null or credential_reference !~* '(token|password|secret)='));
create table if not exists intelligence.intelligence_query_logs(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),user_id uuid not null,intent varchar(80),query_hash char(64) not null,result_count int not null default 0 check(result_count>=0),permission_result varchar(16) not null check(permission_result in('ALLOWED','DENIED')),provider_used boolean not null default false,occurred_at timestamptz not null default now(),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null);

create index if not exists ix_intel_rules_queue on intelligence.intelligence_rules(tenant_id,active,module,last_executed_at);
create index if not exists ix_intel_rule_exec on intelligence.intelligence_rule_executions(tenant_id,status,started_at desc);
create index if not exists ix_intel_recommendations_queue on intelligence.intelligence_recommendations(tenant_id,status,severity,module,created_at desc);
create index if not exists ix_intel_recommendations_entity on intelligence.intelligence_recommendations(tenant_id,entity_type,entity_id);
create index if not exists ix_intel_scores_risk on intelligence.intelligence_scores(tenant_id,risk_band,module,overall_score desc);
create index if not exists ix_intel_scores_entity on intelligence.intelligence_scores(tenant_id,entity_type,entity_id);
create index if not exists ix_intel_anomalies_queue on intelligence.intelligence_anomalies(tenant_id,status,severity,module,detected_at desc);
create index if not exists ix_intel_anomalies_entity on intelligence.intelligence_anomalies(tenant_id,entity_type,entity_id);
create index if not exists ix_intel_priorities_queue on intelligence.intelligence_priority_items(tenant_id,status,priority_score desc,due_at);
create index if not exists ix_intel_assistant_history on intelligence.intelligence_assistant_sessions(tenant_id,user_id,created_at desc);
create index if not exists ix_intel_query_audit on intelligence.intelligence_query_logs(tenant_id,user_id,occurred_at desc);
do $$ declare tab text; begin for tab in select tablename from pg_tables where schemaname='intelligence' and tablename like 'intelligence_%' loop execute format('alter table intelligence.%I enable row level security',tab); execute format('alter table intelligence.%I force row level security',tab); if not exists(select 1 from pg_policies where schemaname='intelligence' and tablename=tab and policyname=tab||'_tenant') then execute format('create policy %I on intelligence.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',tab||'_tenant',tab); end if; end loop; end $$;
insert into identity.permissions(code,module,description) values ('intelligence.rules.manage','Inteligência','Configurar e auditar regras de inteligência.'),('intelligence.recommendations.decide','Inteligência','Aceitar, recusar ou arquivar recomendações.'),('intelligence.assistant.use','Inteligência','Consultar o assistente interno com fontes rastreáveis.') on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('3.1.0','Sprint 31 - recomendacoes, scores, anomalias, prioridades e assistente interno',now()) on conflict(version) do nothing;

-- Sprint 33: atendimento, customer success, SLA e central de ajuda.
create schema if not exists support;
create sequence if not exists support.ticket_public_code_seq;
create table if not exists support.support_sla_policies(id uuid primary key default gen_random_uuid(),tenant_id uuid references tenancy.tenants(id),name varchar(120) not null,plan varchar(80),contract_type varchar(80),category varchar(60),priority varchar(20) not null check(priority in('LOW','NORMAL','HIGH','CRITICAL')),severity varchar(20) not null check(severity in('LOW','MEDIUM','HIGH','CRITICAL')),first_response_minutes int not null check(first_response_minutes>0),resolution_minutes int not null check(resolution_minutes>0),business_hours jsonb not null default '{"start":"08:00","end":"18:00","days":[1,2,3,4,5]}',escalation jsonb not null default '[]',pause_waiting_customer boolean not null default true,active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id));
create table if not exists support.support_tickets(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),public_code varchar(24) not null default ('SUP-'||to_char(current_date,'YYYY')||'-'||lpad(nextval('support.ticket_public_code_seq')::text,7,'0')),requester_id uuid not null references identity.users(id),requester_profile varchar(40) not null,channel varchar(30) not null check(channel in('WEB','PORTAL','INTERNAL','EMAIL_PROVIDER','API')),category varchar(60) not null,subcategory varchar(80),module varchar(80),title varchar(180) not null,description varchar(4000) not null,priority varchar(20) not null check(priority in('LOW','NORMAL','HIGH','CRITICAL')),severity varchar(20) not null check(severity in('LOW','MEDIUM','HIGH','CRITICAL')),status varchar(30) not null check(status in('OPEN','TRIAGE','IN_PROGRESS','WAITING_CUSTOMER','WAITING_THIRD_PARTY','RESOLVED','CLOSED','CANCELLED','REOPENED')),assignee_id uuid references identity.users(id),area varchar(80),first_response_due_at timestamptz not null,resolution_due_at timestamptz not null,opened_at timestamptz not null default now(),first_responded_at timestamptz,resolved_at timestamptz,closed_at timestamptz,closed_by uuid,cancellation_reason varchar(1000),resolution_response varchar(4000),rating smallint check(rating between 1 and 5),recurring boolean not null default false,sla_paused_at timestamptz,sla_paused_minutes int not null default 0 check(sla_paused_minutes>=0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(public_code),check(status<>'CANCELLED' or cancellation_reason is not null),check(status<>'RESOLVED' or resolution_response is not null));
create table if not exists support.support_ticket_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,ticket_id uuid not null,event_type varchar(40) not null,description varchar(2000),metadata jsonb not null default '{}',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,ticket_id) references support.support_tickets(tenant_id,id));
create table if not exists support.support_ticket_comments(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,ticket_id uuid not null,author_id uuid not null references identity.users(id),body varchar(4000) not null,internal boolean not null default false,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,ticket_id) references support.support_tickets(tenant_id,id));
create table if not exists support.support_ticket_links(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,ticket_id uuid not null,link_type varchar(40) not null,entity_type varchar(80) not null,entity_id uuid not null,label varchar(180) not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,ticket_id) references support.support_tickets(tenant_id,id));
create table if not exists support.support_sla_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,ticket_id uuid not null,policy_id uuid,kind varchar(30) not null,old_due_at timestamptz,new_due_at timestamptz,reason varchar(1000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,ticket_id) references support.support_tickets(tenant_id,id));
create table if not exists support.knowledge_articles(id uuid primary key default gen_random_uuid(),tenant_id uuid references tenancy.tenants(id),slug varchar(220) not null,title varchar(180) not null,content varchar(20000) not null,type varchar(40) not null,module varchar(80),audience varchar(40) not null,status varchar(16) not null default 'DRAFT' check(status in('DRAFT','PUBLISHED','ARCHIVED')),global boolean not null default false,views int not null default 0 check(views>=0),helpful int not null default 0 check(helpful>=0),not_helpful int not null default 0 check(not_helpful>=0),published_at timestamptz,published_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(slug));
create table if not exists support.knowledge_article_versions(id uuid primary key default gen_random_uuid(),tenant_id uuid,article_id uuid not null,version int not null check(version>0),title varchar(180) not null,content varchar(20000) not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,article_id) references support.knowledge_articles(tenant_id,id),unique(article_id,version));
create table if not exists support.knowledge_article_feedback(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,article_id uuid not null,user_id uuid not null,helpful boolean not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(article_id,user_id));
create table if not exists support.implementation_projects(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(160) not null,status varchar(20) not null check(status in('PLANNED','ACTIVE','PAUSED','COMPLETED','CANCELLED')),starts_on date not null,due_on date not null,owner_id uuid references identity.users(id),accepted_at timestamptz,acceptance_reason varchar(1000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),check(due_on>=starts_on));
create table if not exists support.implementation_project_phases(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,project_id uuid not null,name varchar(60) not null,sequence int not null check(sequence>0),status varchar(20) not null check(status in('PENDING','ACTIVE','COMPLETED','REOPENED','SKIPPED')),due_on date not null,mandatory boolean not null default true,checklist jsonb not null default '[]',evidence varchar(2000),completed_at timestamptz,reopened_reason varchar(1000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,project_id) references support.implementation_projects(tenant_id,id),unique(project_id,sequence));
create table if not exists support.implementation_project_tasks(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,project_id uuid not null,phase_id uuid,title varchar(180) not null,status varchar(20) not null,priority varchar(20) not null,responsible_id uuid,due_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,project_id) references support.implementation_projects(tenant_id,id));
create table if not exists support.implementation_project_risks(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,project_id uuid not null,title varchar(180) not null,severity varchar(20) not null check(severity in('LOW','MEDIUM','HIGH','CRITICAL')),status varchar(20) not null default 'OPEN',mitigation varchar(2000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,project_id) references support.implementation_projects(tenant_id,id));
create table if not exists support.implementation_project_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,project_id uuid not null,event_type varchar(40) not null,description varchar(2000) not null,occurred_at timestamptz not null default now(),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,project_id) references support.implementation_projects(tenant_id,id));
create table if not exists support.training_tracks(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(160) not null,profile varchar(80) not null,mandatory boolean not null,active boolean not null,validity_days int not null check(validity_days>0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id));
create table if not exists support.training_track_modules(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,track_id uuid not null,title varchar(180) not null,sequence int not null check(sequence>0),content_url varchar(1000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,track_id) references support.training_tracks(tenant_id,id));
create table if not exists support.training_assignments(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,track_id uuid not null,user_id uuid not null references identity.users(id),status varchar(20) not null check(status in('PENDING','IN_PROGRESS','COMPLETED','OVERDUE')),due_at timestamptz not null,completed_at timestamptz,rating smallint check(rating between 1 and 5),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,track_id) references support.training_tracks(tenant_id,id),unique(tenant_id,id),unique(track_id,user_id));
create table if not exists support.training_completions(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,assignment_id uuid not null,user_id uuid not null,completed_at timestamptz not null,rating smallint check(rating between 1 and 5),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,assignment_id) references support.training_assignments(tenant_id,id));
create table if not exists support.customer_feedback(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),ticket_id uuid,module varchar(80),type varchar(20) not null check(type in('NPS','SUGGESTION','COMPLAINT','PRAISE','TICKET')),score smallint check(score between 0 and 10),message varchar(4000) not null,severity varchar(20) not null check(severity in('LOW','MEDIUM','HIGH','CRITICAL')),status varchar(20) not null check(status in('OPEN','ANSWERED','CONVERTED','ARCHIVED')),response varchar(4000),responded_at timestamptz,responded_by uuid,backlog_item_id uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id));
create table if not exists support.customer_feedback_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,feedback_id uuid not null,event_type varchar(40) not null,description varchar(2000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,feedback_id) references support.customer_feedback(tenant_id,id));
create table if not exists support.product_backlog_items(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),title varchar(180) not null,description varchar(4000) not null,origin varchar(20) not null check(origin in('TICKET','FEEDBACK','IMPLEMENTATION','AUDIT','COMMERCIAL','INTERNAL')),module varchar(80),priority varchar(20) not null check(priority in('LOW','NORMAL','HIGH','CRITICAL')),impact varchar(20) not null,estimated_hours int check(estimated_hours>=0),status varchar(30) not null check(status in('NEW','ANALYSIS','APPROVED','PLANNED','IN_DEVELOPMENT','DELIVERED','REJECTED','ARCHIVED')),decision_reason varchar(2000),decided_by uuid,release_version varchar(40),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),check(status<>'REJECTED' or decision_reason is not null));
create table if not exists support.product_backlog_links(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,backlog_item_id uuid not null,link_type varchar(30) not null,entity_id uuid not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,backlog_item_id) references support.product_backlog_items(tenant_id,id));
create table if not exists support.release_notes(id uuid primary key default gen_random_uuid(),tenant_id uuid references tenancy.tenants(id),version varchar(40) not null,title varchar(180) not null,content varchar(20000) not null,module varchar(80),audience varchar(40) not null,global boolean not null default false,status varchar(16) not null check(status in('DRAFT','PUBLISHED','ARCHIVED')),publish_at timestamptz,published_at timestamptz,published_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id));
create table if not exists support.release_note_reads(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,release_note_id uuid not null,user_id uuid not null,read_at timestamptz not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(release_note_id,user_id));
create table if not exists support.customer_success_metrics(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),period date not null,metric varchar(60) not null,value numeric(18,4) not null,module varchar(80),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,period,metric,module));
create index if not exists ix_support_tickets_queue on support.support_tickets(tenant_id,status,priority,severity,resolution_due_at);
create index if not exists ix_support_tickets_module_assignee on support.support_tickets(tenant_id,module,assignee_id,opened_at desc);
create index if not exists ix_support_events_history on support.support_ticket_events(tenant_id,ticket_id,created_at desc);
create index if not exists ix_support_sla_match on support.support_sla_policies(tenant_id,active,priority,severity,category);
create index if not exists ix_support_articles_search on support.knowledge_articles(tenant_id,status,module,published_at desc);
create index if not exists ix_support_implementation_due on support.implementation_projects(tenant_id,status,due_on);
create index if not exists ix_support_training_due on support.training_assignments(tenant_id,status,due_at);
create index if not exists ix_support_feedback_date on support.customer_feedback(tenant_id,status,type,created_at desc);
create index if not exists ix_support_backlog_queue on support.product_backlog_items(tenant_id,status,priority,module,created_at desc);
create index if not exists ix_support_releases on support.release_notes(tenant_id,status,module,published_at desc);
do $$ declare tab text; begin for tab in select tablename from pg_tables where schemaname='support' loop if exists(select 1 from information_schema.columns where table_schema='support' and table_name=tab and column_name='tenant_id') then execute format('alter table support.%I enable row level security',tab); execute format('alter table support.%I force row level security',tab); if not exists(select 1 from pg_policies where schemaname='support' and tablename=tab and policyname=tab||'_tenant') then execute format('create policy %I on support.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',tab||'_tenant',tab); end if; end if; end loop; end $$;
insert into identity.permissions(code,module,description) values ('support.read','Atendimento e Suporte','Consultar atendimento, ajuda e comunicados.'),('support.write','Atendimento e Suporte','Abrir, responder e atualizar chamados e feedback.'),('support.manage','Atendimento e Suporte','Gerenciar SLA, implantação, treinamento, artigos e backlog.') on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('3.3.0','Sprint 33 - suporte, customer success, SLA, ajuda, implantacao e feedback',now()) on conflict(version) do nothing;

-- Sprint 34: SST Rural. Dados clínicos detalhados não são armazenados.
create schema if not exists sst;
create sequence if not exists sst.incident_code_seq;
create table if not exists sst.sst_job_roles(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(160) not null,description varchar(2000),status varchar(16) not null default 'ACTIVE' check(status in('ACTIVE','INACTIVE')),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,name));
create table if not exists sst.sst_work_areas(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(160) not null,description varchar(2000),status varchar(16) not null default 'ACTIVE' check(status in('ACTIVE','INACTIVE')),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,name));
create table if not exists sst.sst_workers(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(160) not null,document varchar(40),internal_code varchar(40),job_role_id uuid not null,work_area_id uuid not null,property_id uuid,status varchar(24) not null check(status in('ACTIVE','INACTIVE','ON_LEAVE','OUTSOURCED','PENDING')),admission_date date,employment_type varchar(40) not null,contact varchar(160),notes varchar(2000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,job_role_id) references sst.sst_job_roles(tenant_id,id),foreign key(tenant_id,work_area_id) references sst.sst_work_areas(tenant_id,id));
create unique index if not exists ux_sst_worker_document on sst.sst_workers(tenant_id,document) where document is not null and deleted_at is null;
create unique index if not exists ux_sst_worker_code on sst.sst_workers(tenant_id,internal_code) where internal_code is not null and deleted_at is null;
create table if not exists sst.sst_risk_types(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(80) not null,status varchar(16) not null default 'ACTIVE',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,name));
create table if not exists sst.sst_epi_catalog(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(160) not null,category varchar(80) not null,approval_code varchar(60),approval_valid_until date,useful_life_days int check(useful_life_days>0),unit varchar(30) not null,status varchar(16) not null check(status in('ACTIVE','INACTIVE','EXPIRED')),notes varchar(2000),stock_item_id uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id));
create table if not exists sst.sst_training_catalog(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(160) not null,type varchar(80) not null,description varchar(2000),workload_hours numeric(8,2) not null check(workload_hours>0),validity_days int check(validity_days>0),instructor varchar(160),status varchar(16) not null check(status in('DRAFT','ACTIVE','INACTIVE')),evidence_document_id uuid,notes varchar(2000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id));
create table if not exists sst.sst_risks(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(160) not null,description varchar(2000) not null,type varchar(40) not null check(type in('PHYSICAL','CHEMICAL','BIOLOGICAL','ERGONOMIC','ACCIDENT','OPERATIONAL','ENVIRONMENTAL','LOGISTICS','MACHINERY','OPEN_FIELD','PROCESSING','TRANSPORT','STORAGE','OTHER')),severity smallint not null check(severity between 1 and 5),probability smallint not null check(probability between 1 and 5),risk_level smallint not null check(risk_level=severity*probability and risk_level between 1 and 25),work_area_id uuid not null,job_role_id uuid,operation varchar(160),preventive_measure varchar(2000),recommended_epi_id uuid,recommended_training_id uuid,status varchar(16) not null check(status in('ACTIVE','CONTROLLED','INACTIVE')),responsible_id uuid,evidence_document_id uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,work_area_id) references sst.sst_work_areas(tenant_id,id),foreign key(tenant_id,job_role_id) references sst.sst_job_roles(tenant_id,id));
create table if not exists sst.sst_risk_controls(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,risk_id uuid not null,control_type varchar(30) not null,description varchar(2000) not null,status varchar(20) not null,due_on date,responsible_id uuid,evidence_document_id uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,risk_id) references sst.sst_risks(tenant_id,id));
create table if not exists sst.sst_epi_deliveries(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,worker_id uuid not null,epi_id uuid not null,quantity numeric(14,3) not null check(quantity>0),delivered_on date not null,replace_on date,reason varchar(500) not null,responsible_id uuid not null,acceptance varchar(300),evidence_document_id uuid,status varchar(16) not null check(status in('DELIVERED','REPLACED','RETURNED','EXPIRED','CANCELLED')),cancellation_reason varchar(1000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,worker_id) references sst.sst_workers(tenant_id,id),foreign key(tenant_id,epi_id) references sst.sst_epi_catalog(tenant_id,id),check(replace_on is null or replace_on>delivered_on),check(status<>'CANCELLED' or cancellation_reason is not null));
create table if not exists sst.sst_epi_delivery_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,delivery_id uuid not null,event_type varchar(30) not null,description varchar(1000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,delivery_id) references sst.sst_epi_deliveries(tenant_id,id));
create table if not exists sst.sst_training_requirements(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,training_id uuid not null,job_role_id uuid,work_area_id uuid,mandatory boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,training_id) references sst.sst_training_catalog(tenant_id,id));
create table if not exists sst.sst_training_sessions(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,training_id uuid not null,held_on date not null,responsible_id uuid not null,status varchar(16) not null check(status in('PLANNED','COMPLETED','CANCELLED')),cancellation_reason varchar(1000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,id),foreign key(tenant_id,training_id) references sst.sst_training_catalog(tenant_id,id));
create table if not exists sst.sst_training_attendance(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,session_id uuid not null,worker_id uuid not null,performed_on date,valid_until date,result varchar(80),status varchar(16) not null check(status in('PLANNED','COMPLETED','EXPIRED','CANCELLED','PENDING')),notes varchar(2000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,id),foreign key(tenant_id,session_id) references sst.sst_training_sessions(tenant_id,id),foreign key(tenant_id,worker_id) references sst.sst_workers(tenant_id,id));
create table if not exists sst.sst_medical_exam_controls(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,worker_id uuid not null,type varchar(30) not null check(type in('ADMISSION','PERIODIC','RETURN_TO_WORK','ROLE_CHANGE','TERMINATION','OTHER')),scheduled_on date,performed_on date,valid_until date,status varchar(16) not null check(status in('SCHEDULED','COMPLETED','EXPIRED','PENDING','CANCELLED')),administrative_result varchar(24) not null check(administrative_result in('FIT','FIT_WITH_RESTRICTION','UNFIT','PENDING','NOT_INFORMED')),authorized_restriction_summary varchar(500),evidence_document_id uuid,responsible varchar(160),administrative_notes varchar(2000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,worker_id) references sst.sst_workers(tenant_id,id),check(performed_on is null or scheduled_on is null or performed_on>=scheduled_on));
create table if not exists sst.sst_incidents(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,code varchar(30) not null default ('SST-'||to_char(current_date,'YYYY')||'-'||lpad(nextval('sst.incident_code_seq')::text,7,'0')),type varchar(40) not null,occurred_at timestamptz not null,location varchar(300) not null,work_area_id uuid not null,worker_id uuid,description varchar(4000) not null,severity smallint not null check(severity between 1 and 5),probable_cause varchar(2000),immediate_action varchar(2000) not null,responsible_id uuid not null,status varchar(24) not null check(status in('OPEN','INVESTIGATING','AWAITING_ACTION','RESOLVED','CLOSED','CANCELLED','REOPENED')),investigation_required boolean not null,corrective_action_required boolean not null,external_communication_required boolean not null default false,conclusion varchar(4000),transition_reason varchar(1000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,code),foreign key(tenant_id,work_area_id) references sst.sst_work_areas(tenant_id,id),foreign key(tenant_id,worker_id) references sst.sst_workers(tenant_id,id),check(severity<4 or investigation_required));
create table if not exists sst.sst_incident_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,incident_id uuid not null,event_type varchar(30) not null,description varchar(2000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,incident_id) references sst.sst_incidents(tenant_id,id));
create table if not exists sst.sst_incident_investigations(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,incident_id uuid not null,root_cause varchar(4000),contributing_factors varchar(4000),status varchar(20) not null,evidence_document_id uuid,closure_justification varchar(2000),closed_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,id),foreign key(tenant_id,incident_id) references sst.sst_incidents(tenant_id,id));
create table if not exists sst.sst_action_plans(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,incident_id uuid,investigation_id uuid,type varchar(20) not null check(type in('CORRECTIVE','PREVENTIVE')),description varchar(2000) not null,mandatory boolean not null,responsible_id uuid not null,due_on date not null,status varchar(20) not null check(status in('OPEN','IN_PROGRESS','COMPLETED','REJECTED','CANCELLED')),evidence_document_id uuid,rejection_reason varchar(1000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id));
create table if not exists sst.sst_action_plan_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,action_plan_id uuid not null,event_type varchar(30) not null,description varchar(2000),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,action_plan_id) references sst.sst_action_plans(tenant_id,id));
create table if not exists sst.sst_checklist_templates(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,name varchar(160) not null,type varchar(40) not null,status varchar(16) not null default 'ACTIVE' check(status in('ACTIVE','INACTIVE')),critical boolean not null default false,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id));
create table if not exists sst.sst_checklist_items(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,template_id uuid not null,sequence int not null check(sequence>0),question varchar(1000) not null,required boolean not null,evidence_required boolean not null,critical boolean not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,id),foreign key(tenant_id,template_id) references sst.sst_checklist_templates(tenant_id,id));
create table if not exists sst.sst_checklist_runs(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,template_id uuid not null,worker_id uuid,location varchar(300),status varchar(20) not null,result varchar(20),completed_at timestamptz,completed_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,unique(tenant_id,id),foreign key(tenant_id,template_id) references sst.sst_checklist_templates(tenant_id,id),foreign key(tenant_id,worker_id) references sst.sst_workers(tenant_id,id));
create table if not exists sst.sst_checklist_answers(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,run_id uuid not null,item_id uuid not null,answer varchar(1000),evidence_document_id uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,foreign key(tenant_id,run_id) references sst.sst_checklist_runs(tenant_id,id),foreign key(tenant_id,item_id) references sst.sst_checklist_items(tenant_id,id));
create table if not exists sst.sst_compliance_snapshots(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),captured_at timestamptz not null,compliance_percent numeric(5,2) not null check(compliance_percent between 0 and 100),metrics jsonb not null default '{}',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null);
create index if not exists ix_sst_workers_scope on sst.sst_workers(tenant_id,status,work_area_id,job_role_id);
create index if not exists ix_sst_risks_queue on sst.sst_risks(tenant_id,status,risk_level desc,work_area_id,job_role_id);
create index if not exists ix_sst_epi_due on sst.sst_epi_deliveries(tenant_id,status,worker_id,replace_on);
create index if not exists ix_sst_training_due on sst.sst_training_attendance(tenant_id,status,worker_id,valid_until);
create index if not exists ix_sst_exam_due on sst.sst_medical_exam_controls(tenant_id,status,worker_id,valid_until);
create index if not exists ix_sst_incident_queue on sst.sst_incidents(tenant_id,status,severity,work_area_id,occurred_at desc);
create index if not exists ix_sst_actions_due on sst.sst_action_plans(tenant_id,status,due_on,responsible_id);
create index if not exists ix_sst_checklist_runs on sst.sst_checklist_runs(tenant_id,status,result,completed_at desc);
do $$ declare tab text; begin for tab in select tablename from pg_tables where schemaname='sst' loop execute format('alter table sst.%I enable row level security',tab); execute format('alter table sst.%I force row level security',tab); if not exists(select 1 from pg_policies where schemaname='sst' and tablename=tab and policyname=tab||'_tenant') then execute format('create policy %I on sst.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',tab||'_tenant',tab); end if; end loop; end $$;
insert into identity.permissions(code,module,description) values ('sst.read','SST Rural','Consultar segurança operacional.'),('sst.write','SST Rural','Gerenciar SST, EPI, treinamentos e incidentes.'),('sst.medical.read','SST Rural','Consultar controle administrativo ocupacional autorizado.') on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('3.4.0','Sprint 34 - SST Rural, EPI, treinamentos, exames administrativos e incidentes',now()) on conflict(version) do nothing;




-- Sprint 35: Frota e Maquinas (modelo isolado por tenant e auditavel)
create table if not exists fleet.fleet_asset_types(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(100) not null,status varchar(16) not null default 'ACTIVE' check(status in('ACTIVE','INACTIVE')),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,name));
create table if not exists fleet.fleet_operators(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(160) not null,document varchar(40),employment_type varchar(40) not null,role varchar(80) not null,status varchar(16) not null check(status in('ACTIVE','INACTIVE','BLOCKED')),property_id uuid,license_categories varchar(80),license_expires_on date,notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id));
create unique index if not exists uq_fleet_operator_document on fleet.fleet_operators(tenant_id,upper(document)) where document is not null and deleted_at is null;
create table if not exists fleet.fleet_operator_certifications(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,operator_id uuid not null,name varchar(160) not null,category varchar(80),issued_on date,expires_on date,status varchar(16) not null check(status in('VALID','EXPIRED','REVOKED')),training_id uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,operator_id) references fleet.fleet_operators(tenant_id,id));
create table if not exists fleet.fleet_assets(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),internal_code varchar(40) not null,name varchar(160) not null,asset_type_id uuid not null,status varchar(20) not null check(status in('AVAILABLE','OPERATING','MAINTENANCE','UNAVAILABLE','RESERVED','WRITTEN_OFF','SOLD','INACTIVE')),brand varchar(80),model varchar(80),year int check(year between 1900 and 2200),plate varchar(20),serial_number varchar(80),property_id uuid,cost_center_id uuid,odometer numeric(16,2) not null default 0 check(odometer>=0),hour_meter numeric(16,2) not null default 0 check(hour_meter>=0),fuel_capacity numeric(14,3) check(fuel_capacity>=0),main_operator_id uuid,acquired_on date,acquisition_value numeric(18,2) check(acquisition_value>=0),notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,asset_type_id) references fleet.fleet_asset_types(tenant_id,id),foreign key(tenant_id,main_operator_id) references fleet.fleet_operators(tenant_id,id),foreign key(tenant_id,cost_center_id) references finance.cost_centers(tenant_id,id));
create unique index if not exists uq_fleet_assets_code on fleet.fleet_assets(tenant_id,upper(internal_code)) where deleted_at is null;
create unique index if not exists uq_fleet_assets_plate on fleet.fleet_assets(tenant_id,upper(plate)) where plate is not null and deleted_at is null;
create table if not exists fleet.fleet_asset_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,asset_id uuid not null,event_type varchar(40) not null,description text not null,reference_id uuid,metadata jsonb not null default '{}',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,asset_id) references fleet.fleet_assets(tenant_id,id));
create table if not exists fleet.fleet_maintenance_plans(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,asset_id uuid not null,maintenance_type varchar(80) not null,description text not null,periodicity numeric(16,2) not null check(periodicity>0),control_unit varchar(24) not null check(control_unit in('DATE','ODOMETER','HOUR_METER','SEASON','OPERATIONS','MANUAL')),next_execution_at timestamptz,next_meter numeric(16,2) check(next_meter>=0),status varchar(16) not null check(status in('ACTIVE','INACTIVE','COMPLETED')),responsible_id uuid,estimated_cost numeric(18,2) not null default 0 check(estimated_cost>=0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,asset_id) references fleet.fleet_assets(tenant_id,id));
create table if not exists fleet.fleet_maintenance_plan_items(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,plan_id uuid not null,sequence int not null check(sequence>0),description varchar(1000) not null,part_id uuid,planned_quantity numeric(14,3) check(planned_quantity>0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,plan_id) references fleet.fleet_maintenance_plans(tenant_id,id));
create table if not exists fleet.fleet_maintenance_requests(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,asset_id uuid not null,defect_class varchar(80) not null,severity varchar(16) not null check(severity in('LOW','MEDIUM','HIGH','CRITICAL')),problem_description text not null,status varchar(20) not null check(status in('OPEN','DIAGNOSIS','COMPLETED','CANCELLED','REOPENED')),blocks_asset boolean not null default false,diagnosis text,completion_description text,reopen_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,asset_id) references fleet.fleet_assets(tenant_id,id));
create sequence if not exists fleet.work_order_code_seq;
create table if not exists fleet.fleet_work_orders(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,code varchar(40) not null default ('OS-'||lpad(nextval('fleet.work_order_code_seq')::text,8,'0')),asset_id uuid not null,maintenance_request_id uuid,maintenance_plan_id uuid,type varchar(30) not null check(type in('PREVENTIVE','CORRECTIVE','INSPECTION','LUBRICATION','TIRES','TECHNICAL_REFUELING','EXTERNAL_SERVICE','REVISION','OTHER')),priority varchar(16) not null check(priority in('LOW','MEDIUM','HIGH','CRITICAL')),requester_id uuid,responsible_id uuid,opened_at timestamptz not null default now(),due_at timestamptz,started_at timestamptz,completed_at timestamptz,description text not null,diagnosis text,services_performed text,cancellation_reason text,status varchar(24) not null check(status in('OPEN','PLANNED','IN_PROGRESS','WAITING_PART','WAITING_VENDOR','COMPLETED','CANCELLED','REOPENED')),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,code),foreign key(tenant_id,asset_id) references fleet.fleet_assets(tenant_id,id));
create table if not exists fleet.fleet_work_order_items(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,work_order_id uuid not null,description varchar(1000) not null,completed boolean not null default false,completed_at timestamptz,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,work_order_id) references fleet.fleet_work_orders(tenant_id,id));
create table if not exists fleet.fleet_work_order_parts(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,work_order_id uuid not null,product_id uuid,description varchar(200) not null,quantity numeric(14,3) not null check(quantity>0),unit_cost numeric(18,2) not null check(unit_cost>=0),stock_movement_id uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,work_order_id) references fleet.fleet_work_orders(tenant_id,id));
create table if not exists fleet.fleet_work_order_costs(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,work_order_id uuid not null,cost_type varchar(30) not null,value numeric(18,2) not null check(value>0),supplier_id uuid,payable_id uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,work_order_id) references fleet.fleet_work_orders(tenant_id,id));
create table if not exists fleet.fleet_fuel_types(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(100) not null,unit varchar(10) not null default 'L',status varchar(16) not null check(status in('ACTIVE','INACTIVE')),inventory_product_id uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,name));
create table if not exists fleet.fleet_refuelings(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,asset_id uuid not null,operator_id uuid,fuel_type_id uuid not null,quantity numeric(16,3) not null check(quantity>0),unit_price numeric(18,4) not null check(unit_price>=0),total_value numeric(18,2) not null check(total_value>=0),occurred_at timestamptz not null,odometer numeric(16,2) check(odometer>=0),hour_meter numeric(16,2) check(hour_meter>=0),location varchar(160),property_id uuid,cost_center_id uuid,evidence_document_id uuid,notes text,status varchar(16) not null check(status in('ACTIVE','CANCELLED')),cancellation_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,asset_id) references fleet.fleet_assets(tenant_id,id),foreign key(tenant_id,operator_id) references fleet.fleet_operators(tenant_id,id),foreign key(tenant_id,fuel_type_id) references fleet.fleet_fuel_types(tenant_id,id));
create table if not exists fleet.fleet_lubrications(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,asset_id uuid not null,operator_id uuid,lubricant varchar(160) not null,quantity numeric(16,3) not null check(quantity>0),unit_price numeric(18,4) not null check(unit_price>=0),occurred_at timestamptz not null,odometer numeric(16,2),hour_meter numeric(16,2),notes text,status varchar(16) not null default 'ACTIVE',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,asset_id) references fleet.fleet_assets(tenant_id,id));
create table if not exists fleet.fleet_tires(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),internal_code varchar(40) not null,kind varchar(30) not null,brand varchar(80),model varchar(80),serial_number varchar(80),status varchar(20) not null check(status in('AVAILABLE','MOUNTED','MAINTENANCE','WRITTEN_OFF')),asset_id uuid,position varchar(40),wear_percent numeric(5,2) not null default 0 check(wear_percent between 0 and 100),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,internal_code),foreign key(tenant_id,asset_id) references fleet.fleet_assets(tenant_id,id));
create table if not exists fleet.fleet_tire_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,tire_id uuid not null,asset_id uuid,event_type varchar(30) not null,position varchar(40),wear_percent numeric(5,2),reason text,occurred_at timestamptz not null default now(),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,tire_id) references fleet.fleet_tires(tenant_id,id));
create table if not exists fleet.fleet_downtime_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,asset_id uuid not null,type varchar(30) not null,started_at timestamptz not null,ended_at timestamptz check(ended_at is null or ended_at>=started_at),reason text not null,responsible_id uuid not null,operational_impact text,season_id uuid,field_id uuid,route_id uuid,status varchar(16) not null check(status in('OPEN','CLOSED','CANCELLED')),closing_description text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,asset_id) references fleet.fleet_assets(tenant_id,id));
create table if not exists fleet.fleet_operational_costs(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,asset_id uuid not null,cost_type varchar(30) not null,value numeric(18,2) not null check(value>0),occurred_on date not null,property_id uuid,season_id uuid,field_id uuid,operation_id uuid,route_id uuid,cost_center_id uuid,product_id uuid,customer_id uuid,order_id uuid,origin_type varchar(30) not null,origin_id uuid not null,status varchar(16) not null check(status in('ACTIVE','CANCELLED')),cancellation_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,origin_type,origin_id),foreign key(tenant_id,asset_id) references fleet.fleet_assets(tenant_id,id));
create table if not exists fleet.fleet_cost_allocations(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,operational_cost_id uuid not null,allocation_type varchar(30) not null,reference_id uuid not null,percent numeric(7,4) not null check(percent>0 and percent<=100),value numeric(18,2) not null check(value>0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,operational_cost_id) references fleet.fleet_operational_costs(tenant_id,id));
create table if not exists fleet.fleet_availability_snapshots(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,asset_id uuid not null,snapshot_date date not null,period_minutes int not null check(period_minutes>0),downtime_minutes int not null check(downtime_minutes>=0),availability_percent numeric(5,2) not null check(availability_percent between 0 and 100),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,asset_id,snapshot_date),foreign key(tenant_id,asset_id) references fleet.fleet_assets(tenant_id,id));
create index if not exists ix_fleet_assets_scope on fleet.fleet_assets(tenant_id,status,asset_type_id,main_operator_id,cost_center_id);
create index if not exists ix_fleet_plans_due on fleet.fleet_maintenance_plans(tenant_id,status,asset_id,next_execution_at,next_meter);
create index if not exists ix_fleet_work_orders_queue on fleet.fleet_work_orders(tenant_id,status,asset_id,type,priority,due_at);
create index if not exists ix_fleet_refuelings_date on fleet.fleet_refuelings(tenant_id,asset_id,operator_id,occurred_at desc,cost_center_id);
create index if not exists ix_fleet_downtime_date on fleet.fleet_downtime_events(tenant_id,status,asset_id,type,started_at desc);
create index if not exists ix_fleet_costs_dimensions on fleet.fleet_operational_costs(tenant_id,status,asset_id,cost_type,occurred_on,cost_center_id,season_id,field_id,route_id);
do $$ declare tab text; begin for tab in select tablename from pg_tables where schemaname='fleet' and tablename like 'fleet_%' loop execute format('alter table fleet.%I enable row level security',tab); execute format('alter table fleet.%I force row level security',tab); if not exists(select 1 from pg_policies where schemaname='fleet' and tablename=tab and policyname=tab||'_tenant') then execute format('create policy %I on fleet.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',tab||'_tenant',tab); end if; end loop; end $$;
insert into identity.permissions(code,module,description) values ('fleet.read','Frota e Maquinas','Consultar frota, manutencao e disponibilidade.'),('fleet.write','Frota e Maquinas','Gerenciar ativos, operadores e abastecimentos.'),('maintenance.read','Frota e Maquinas','Consultar manutencoes e ordens de servico.'),('maintenance.write','Frota e Maquinas','Gerenciar manutencoes e ordens de servico.'),('fleet.meter.override','Frota e Maquinas','Justificar e auditar reducao de medidores.') on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('3.5.0','Sprint 35 - Frota, maquinas, manutencao, combustivel e custos operacionais',now()) on conflict(version) do nothing;
commit;
begin;
create schema if not exists finance;
-- Classificacao gerencial complementar ao plano legado.
create table if not exists finance.finance_account_categories(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),code varchar(40) not null,name varchar(160) not null,account_type varchar(24) not null check(account_type in('REVENUE','EXPENSE','DIRECT_COST','INDIRECT_COST','INVESTMENT','TAX','TRANSFER','ADJUSTMENT','OTHER')),parent_id uuid,active boolean not null default true,show_in_dre boolean not null default true,show_in_cashflow boolean not null default true,requires_cost_center boolean not null default false,notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,parent_id) references finance.finance_account_categories(tenant_id,id));
create unique index if not exists uq_fin_account_category_code on finance.finance_account_categories(tenant_id,upper(code)) where deleted_at is null;
create table if not exists finance.finance_cost_centers(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),code varchar(40) not null,name varchar(160) not null,application varchar(30) not null check(application in('PROPERTY','SEASON','PLOT','CROP','PRODUCT','LOT','FLEET','ROUTE','PROCESSING','COMMERCIAL','ADMINISTRATION','SST','COMPLIANCE','OTHER')),active boolean not null default true,notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id));
create unique index if not exists uq_fin_cost_center_code on finance.finance_cost_centers(tenant_id,upper(code)) where deleted_at is null;
create table if not exists finance.finance_cost_center_links(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,cost_center_id uuid not null,link_type varchar(30) not null,reference_id uuid not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,cost_center_id) references finance.finance_cost_centers(tenant_id,id),unique(tenant_id,cost_center_id,link_type,reference_id));
create table if not exists finance.finance_budget_plans(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(160) not null,budget_type varchar(24) not null check(budget_type in('ANNUAL','MONTHLY','SEASON','PROPERTY','PLOT','CROP','COST_CENTER','PROJECT')),period_start date not null,period_end date not null,season_id uuid,property_id uuid,plot_id uuid,crop_id uuid,cost_center_id uuid,responsible_id uuid not null,status varchar(16) not null default 'DRAFT' check(status in('DRAFT','SUBMITTED','APPROVED','REJECTED','EXPIRED')),notes text,approved_at timestamptz,approved_by uuid,rejection_reason text,current_version integer not null default 1 check(current_version>0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),check(period_end>=period_start),check(status<>'APPROVED' or (approved_at is not null and approved_by is not null)),check(status<>'REJECTED' or length(trim(rejection_reason))>=3));
create table if not exists finance.finance_budget_items(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,budget_plan_id uuid not null,category_id uuid not null,cost_center_id uuid,month date,planned_amount numeric(18,2) not null check(planned_amount>0),notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,budget_plan_id) references finance.finance_budget_plans(tenant_id,id),foreign key(tenant_id,category_id) references finance.finance_account_categories(tenant_id,id));
create table if not exists finance.finance_budget_versions(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,budget_plan_id uuid not null,version integer not null check(version>0),snapshot jsonb not null,reason text not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,budget_plan_id) references finance.finance_budget_plans(tenant_id,id),unique(tenant_id,budget_plan_id,version));
-- Eventos, conciliacao e rateio complementam os titulos reais existentes.
create table if not exists finance.finance_payable_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,payable_id uuid not null,event_type varchar(30) not null,reason text,metadata jsonb not null default '{}',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(payable_id) references finance.payables(id));
create table if not exists finance.finance_receivable_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,receivable_id uuid not null,event_type varchar(30) not null,reason text,metadata jsonb not null default '{}',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(receivable_id) references finance.receivables(id));
create table if not exists finance.finance_reconciliations(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,settlement_id uuid not null references finance.settlements(id),reference varchar(160) not null,status varchar(16) not null check(status in('RECONCILED','REVERSED')),reconciled_at timestamptz not null,reconciled_by uuid not null,reversal_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,settlement_id));
create table if not exists finance.finance_cost_allocations(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,source_type varchar(30) not null,source_id uuid not null,cost_center_id uuid not null,percentage numeric(7,4) not null check(percentage>0 and percentage<=100),amount numeric(18,2) not null check(amount>=0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,source_type,source_id,cost_center_id));
create or replace function finance.validate_allocation_total() returns trigger language plpgsql as $$ declare total numeric; begin select coalesce(sum(percentage),0) into total from finance.finance_cost_allocations where tenant_id=new.tenant_id and source_type=new.source_type and source_id=new.source_id and deleted_at is null; if total>100 then raise exception 'Rateio excede 100%%'; end if; return new; end $$;
drop trigger if exists trg_finance_allocation_total on finance.finance_cost_allocations; create constraint trigger trg_finance_allocation_total after insert or update on finance.finance_cost_allocations deferrable initially deferred for each row execute function finance.validate_allocation_total();
create table if not exists finance.finance_recurring_entries(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,entry_kind varchar(16) not null check(entry_kind in('PAYABLE','RECEIVABLE')),frequency varchar(16) not null check(frequency in('WEEKLY','MONTHLY','QUARTERLY','YEARLY')),next_due_on date not null,ends_on date,template jsonb not null,active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,check(ends_on is null or ends_on>=next_due_on));
create table if not exists finance.finance_cashflow_snapshots(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,snapshot_date date not null,period_kind varchar(12) not null,opening_balance numeric(18,2) not null,expected_in numeric(18,2) not null,expected_out numeric(18,2) not null,actual_in numeric(18,2) not null,actual_out numeric(18,2) not null,closing_balance numeric(18,2) not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,snapshot_date,period_kind));
create table if not exists finance.finance_dre_snapshots(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,period_start date not null,period_end date not null,filters jsonb not null default '{}',lines jsonb not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,check(period_end>=period_start));
create table if not exists finance.finance_profitability_snapshots(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,dimension_type varchar(30) not null,dimension_id uuid,period_start date not null,period_end date not null,revenue numeric(18,2) not null,direct_cost numeric(18,2) not null,indirect_cost numeric(18,2) not null,margin numeric(18,2) not null,margin_percent numeric(9,4),metrics jsonb not null default '{}',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,check(period_end>=period_start));
create table if not exists finance.finance_audit_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,entity_type varchar(40) not null,entity_id uuid not null,action varchar(40) not null,reason text,changed_fields jsonb not null default '{}',occurred_at timestamptz not null default now(),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz);
create index if not exists ix_fin_budget_scope on finance.finance_budget_plans(tenant_id,status,period_start,period_end,cost_center_id,season_id,property_id,plot_id);
create index if not exists ix_fin_budget_items on finance.finance_budget_items(tenant_id,budget_plan_id,category_id,cost_center_id,month);
create index if not exists ix_fin_reconcile_status on finance.finance_reconciliations(tenant_id,status,reconciled_at);
create index if not exists ix_fin_profitability on finance.finance_profitability_snapshots(tenant_id,dimension_type,dimension_id,period_start,period_end);
create index if not exists ix_fin_audit_entity on finance.finance_audit_events(tenant_id,entity_type,entity_id,occurred_at desc);
do $$ declare tab text; begin foreach tab in array array['finance_account_categories','finance_cost_centers','finance_cost_center_links','finance_budget_plans','finance_budget_items','finance_budget_versions','finance_payable_events','finance_receivable_events','finance_reconciliations','finance_cost_allocations','finance_recurring_entries','finance_cashflow_snapshots','finance_dre_snapshots','finance_profitability_snapshots','finance_audit_events'] loop execute format('alter table finance.%I enable row level security',tab); execute format('alter table finance.%I force row level security',tab); if not exists(select 1 from pg_policies where schemaname='finance' and tablename=tab and policyname=tab||'_tenant') then execute format('create policy %I on finance.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',tab||'_tenant',tab); end if; end loop; end $$;
insert into identity.permissions(code,module,description) values ('finance.settle','Financeiro e Controladoria','Registrar baixas financeiras manuais.'),('finance.reconcile','Financeiro e Controladoria','Conciliar ou desconciliar baixas manualmente.'),('finance.export','Financeiro e Controladoria','Exportar relatorios financeiros.'),('finance.budget.approve','Financeiro e Controladoria','Aprovar e versionar orcamentos.') on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('3.6.0','Sprint 36 - Financeiro, controladoria, DRE, caixa, orcamento e rentabilidade',now()) on conflict(version) do nothing;
commit;
begin;
create schema if not exists procurement;
create sequence if not exists procurement.document_number_seq;
create table if not exists procurement.suppliers(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),legal_name varchar(200) not null,trade_name varchar(200),tax_document varchar(20),state_registration varchar(30),supplier_type varchar(40) not null,main_category varchar(50) not null,email varchar(254),phone varchar(30),address text,city varchar(100),state varchar(60),country varchar(80) not null default 'Brasil',main_contact varchar(160),payment_terms varchar(160),average_delivery_days int not null default 0 check(average_delivery_days>=0),status varchar(20) not null check(status in('ACTIVE','INACTIVE','BLOCKED','UNDER_REVIEW','APPROVED','REJECTED')),rejection_reason text,homologated_at timestamptz,homologated_by uuid,notes text,tags text[] not null default '{}',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),check(status<>'REJECTED' or length(trim(rejection_reason))>=3),check(status<>'APPROVED' or (homologated_at is not null and homologated_by is not null)));
create unique index if not exists uq_proc_supplier_tax on procurement.suppliers(tenant_id,tax_document) where tax_document is not null and deleted_at is null;
create table if not exists procurement.supplier_contacts(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,supplier_id uuid not null,name varchar(160) not null,role varchar(100),email varchar(254),phone varchar(30),is_primary boolean not null default false,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,supplier_id) references procurement.suppliers(tenant_id,id));
create table if not exists procurement.supplier_homologations(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,supplier_id uuid not null,status varchar(20) not null check(status in('REQUESTED','APPROVED','REJECTED','EXPIRED','CANCELLED')),valid_until date,criteria jsonb not null default '{}',reason text,decided_at timestamptz,decided_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,supplier_id) references procurement.suppliers(tenant_id,id),check(status<>'REJECTED' or length(trim(reason))>=3),check(status<>'APPROVED' or (valid_until is not null and decided_at is not null and decided_by is not null)));
create table if not exists procurement.supplier_documents(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,supplier_id uuid not null,homologation_id uuid,document_id uuid not null,document_type varchar(60) not null,valid_until date,status varchar(20) not null default 'PENDING',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,supplier_id) references procurement.suppliers(tenant_id,id),foreign key(homologation_id) references procurement.supplier_homologations(id));
create table if not exists procurement.item_catalog(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(200) not null,internal_code varchar(50) not null,category varchar(50) not null,unit varchar(20) not null,item_type varchar(16) not null check(item_type in('MATERIAL','SERVICE','ASSET')),description text,active boolean not null default true,minimum_stock numeric(18,4) check(minimum_stock>=0),cost_center_id uuid,managerial_account_id uuid,related_product_id uuid,requires_lot boolean not null default false,requires_expiry boolean not null default false,requires_document boolean not null default false,requires_inspection boolean not null default false,requires_approved_supplier boolean not null default false,notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,internal_code));
create table if not exists procurement.requisitions(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),number varchar(30) not null,requester_id uuid not null,origin varchar(60) not null,cost_center_id uuid,property_id uuid,justification text not null,priority varchar(12) not null check(priority in('LOW','MEDIUM','HIGH','URGENT')),needed_on date not null,status varchar(30) not null check(status in('DRAFT','OPEN','QUOTING','AWAITING_APPROVAL','APPROVED','REJECTED','CONVERTED','CANCELLED','PARTIALLY_FULFILLED','FULFILLED')),cancel_reason text,rejection_reason text,approved_at timestamptz,approved_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,number),check(priority<>'URGENT' or length(trim(justification))>=3));
create table if not exists procurement.requisition_items(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,requisition_id uuid not null,catalog_item_id uuid not null,quantity numeric(18,4) not null check(quantity>0),unit varchar(20) not null,notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,requisition_id) references procurement.requisitions(tenant_id,id),foreign key(tenant_id,catalog_item_id) references procurement.item_catalog(tenant_id,id));
create table if not exists procurement.quotations(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),number varchar(30) not null,requisition_id uuid,status varchar(24) not null check(status in('DRAFT','SENT','PARTIAL','RESPONDED','ANALYSIS','APPROVED','REJECTED','CANCELLED','EXPIRED')),valid_until date,decision_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,number),foreign key(tenant_id,requisition_id) references procurement.requisitions(tenant_id,id));
create table if not exists procurement.quotation_suppliers(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,quotation_id uuid not null,supplier_id uuid not null,status varchar(20) not null default 'PENDING',delivery_days int check(delivery_days>=0),payment_terms varchar(160),proposal_valid_until date,freight numeric(18,2) not null default 0 check(freight>=0),taxes numeric(18,2) not null default 0 check(taxes>=0),document_id uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,quotation_id) references procurement.quotations(tenant_id,id),foreign key(tenant_id,supplier_id) references procurement.suppliers(tenant_id,id),unique(tenant_id,quotation_id,supplier_id));
create table if not exists procurement.quotation_items(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,quotation_id uuid not null,catalog_item_id uuid not null,quantity numeric(18,4) not null check(quantity>0),unit varchar(20) not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,quotation_id) references procurement.quotations(tenant_id,id),foreign key(tenant_id,catalog_item_id) references procurement.item_catalog(tenant_id,id));
create table if not exists procurement.quotation_responses(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,quotation_item_id uuid not null,quotation_supplier_id uuid not null,unit_price numeric(18,4) not null check(unit_price>0),discount numeric(18,2) not null default 0 check(discount>=0),total numeric(18,2) not null check(total>=0),available boolean not null default true,notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(quotation_item_id) references procurement.quotation_items(id),foreign key(quotation_supplier_id) references procurement.quotation_suppliers(id));
create table if not exists procurement.quotation_decisions(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,quotation_id uuid not null,quotation_item_id uuid,quotation_supplier_id uuid not null,selected_total numeric(18,2) not null check(selected_total>=0),lowest_total numeric(18,2) not null check(lowest_total>=0),justification text,decided_at timestamptz not null default now(),decided_by uuid not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,quotation_id) references procurement.quotations(tenant_id,id),check(selected_total<=lowest_total or length(trim(justification))>=3));
create table if not exists procurement.purchase_orders(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),number varchar(30) not null,supplier_id uuid not null,requisition_id uuid,quotation_id uuid,requester_id uuid,cost_center_id uuid,property_id uuid,payment_terms varchar(160) not null,delivery_on date not null,delivery_address text not null,freight numeric(18,2) not null default 0 check(freight>=0),taxes numeric(18,2) not null default 0 check(taxes>=0),total numeric(18,2) not null check(total>0),status varchar(30) not null check(status in('DRAFT','AWAITING_APPROVAL','APPROVED','SENT','PARTIALLY_RECEIVED','RECEIVED','DIVERGENT','CANCELLED','CLOSED')),approved_at timestamptz,approved_by uuid,cancel_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,number),foreign key(tenant_id,supplier_id) references procurement.suppliers(tenant_id,id));
create table if not exists procurement.purchase_order_items(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,purchase_order_id uuid not null,catalog_item_id uuid not null,quantity numeric(18,4) not null check(quantity>0),received_quantity numeric(18,4) not null default 0 check(received_quantity>=0),unit varchar(20) not null,unit_price numeric(18,4) not null check(unit_price>0),discount numeric(18,2) not null default 0 check(discount>=0),total numeric(18,2) not null check(total>=0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,purchase_order_id) references procurement.purchase_orders(tenant_id,id),foreign key(tenant_id,catalog_item_id) references procurement.item_catalog(tenant_id,id));
create table if not exists procurement.purchase_order_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,purchase_order_id uuid not null,event_type varchar(40) not null,comment text,metadata jsonb not null default '{}',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,purchase_order_id) references procurement.purchase_orders(tenant_id,id));
create table if not exists procurement.receipts(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),number varchar(30) not null,purchase_order_id uuid not null,received_at timestamptz not null,responsible_id uuid not null,invoice_document varchar(100),status varchar(20) not null check(status in('PENDING','PARTIAL','RECEIVED','DIVERGENT','REJECTED','CANCELLED')),rejection_reason text,excess_justification text,stock_integration_status varchar(20) not null default 'PENDING' check(stock_integration_status in('PENDING','COMPLETED','FAILED','NOT_APPLICABLE')),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,number),foreign key(tenant_id,purchase_order_id) references procurement.purchase_orders(tenant_id,id));
create table if not exists procurement.receipt_items(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,receipt_id uuid not null,purchase_order_item_id uuid not null,quantity numeric(18,4) not null check(quantity>0),supplier_lot varchar(100),expires_on date,quality_status varchar(20) not null default 'NOT_REQUIRED',notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,receipt_id) references procurement.receipts(tenant_id,id),foreign key(tenant_id,purchase_order_item_id) references procurement.purchase_order_items(tenant_id,id));
create table if not exists procurement.receipt_divergences(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,receipt_id uuid not null,receipt_item_id uuid,divergence_type varchar(20) not null check(divergence_type in('QUANTITY','PRICE','QUALITY','DOCUMENT','OTHER')),description text not null,status varchar(20) not null default 'OPEN',resolution text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,receipt_id) references procurement.receipts(tenant_id,id),check(length(trim(description))>=3));
create table if not exists procurement.approval_policies(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),name varchar(160) not null,min_value numeric(18,2) not null default 0,max_value numeric(18,2),cost_center_id uuid,category varchar(50),critical boolean not null default false,separation_of_duties boolean not null default true,active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,check(max_value is null or max_value>=min_value));
create table if not exists procurement.approval_requests(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,policy_id uuid,entity_type varchar(30) not null,entity_id uuid not null,requester_id uuid not null,status varchar(20) not null check(status in('NOT_REQUIRED','PENDING','ANALYSIS','APPROVED','REJECTED','CANCELLED')),amount numeric(18,2) not null check(amount>=0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,policy_id) references procurement.approval_policies(tenant_id,id));
create table if not exists procurement.approval_decisions(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,approval_request_id uuid not null,decision varchar(16) not null check(decision in('APPROVED','REJECTED')),comment text,decided_at timestamptz not null default now(),decided_by uuid not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,approval_request_id) references procurement.approval_requests(tenant_id,id),check(decision<>'REJECTED' or length(trim(comment))>=3));
create table if not exists procurement.budget_checks(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,entity_type varchar(30) not null,entity_id uuid not null,budget_id uuid,cost_center_id uuid,amount numeric(18,2) not null check(amount>=0),available_amount numeric(18,2),outside_budget boolean not null,checked_at timestamptz not null default now(),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz);
create table if not exists procurement.report_exports(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),report_type varchar(40) not null,filters jsonb not null default '{}',format varchar(10) not null check(format='CSV'),row_count int not null default 0,exported_at timestamptz not null default now(),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz);
create table if not exists procurement.audit_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),entity_type varchar(40) not null,entity_id uuid not null,action varchar(40) not null,changed_fields jsonb not null default '{}',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz);
create index if not exists ix_proc_supplier_scope on procurement.suppliers(tenant_id,status,main_category,legal_name);
create index if not exists ix_proc_homologation_due on procurement.supplier_homologations(tenant_id,status,valid_until,supplier_id);
create index if not exists ix_proc_catalog_scope on procurement.item_catalog(tenant_id,active,category,cost_center_id);
create index if not exists ix_proc_requisition_queue on procurement.requisitions(tenant_id,status,priority,needed_on,cost_center_id);
create index if not exists ix_proc_quotation_queue on procurement.quotations(tenant_id,status,valid_until,created_at);
create index if not exists ix_proc_order_queue on procurement.purchase_orders(tenant_id,status,supplier_id,delivery_on,cost_center_id);
create index if not exists ix_proc_receipt_queue on procurement.receipts(tenant_id,status,purchase_order_id,received_at);
create index if not exists ix_proc_divergence_queue on procurement.receipt_divergences(tenant_id,status,divergence_type,created_at);
do $$ declare tab text; begin for tab in select tablename from pg_tables where schemaname='procurement' loop execute format('alter table procurement.%I enable row level security',tab); execute format('alter table procurement.%I force row level security',tab); if not exists(select 1 from pg_policies where schemaname='procurement' and tablename=tab and policyname=tab||'_tenant') then execute format('create policy %I on procurement.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',tab||'_tenant',tab); end if; end loop; end $$;
insert into identity.permissions(code,module,description) values ('purchasing.request','Compras e Suprimentos','Criar requisicoes internas.'),('purchasing.receive','Compras e Suprimentos','Receber e conferir pedidos.'),('purchasing.homologate','Compras e Suprimentos','Homologar fornecedores.'),('purchasing.export','Compras e Suprimentos','Exportar relatorios de compras.') on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('3.7.0','Sprint 37 - Compras, suprimentos, fornecedores e recebimento',now()) on conflict(version) do nothing;
commit;

-- Sprint 38: PCP, beneficiamento e rastreabilidade industrial
begin;
create schema if not exists production;
create sequence if not exists production.order_number_seq;
alter table inventory.stock_lots add column if not exists quality_status varchar(16) not null default 'APPROVED' check(quality_status in('PENDING','APPROVED','BLOCKED','REJECTED'));
create table if not exists production.industrial_plants(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),code varchar(40) not null,name varchar(160) not null,address text,active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,code));
create table if not exists production.production_lines(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,plant_id uuid not null,code varchar(40) not null,name varchar(160) not null,capacity numeric(18,4) check(capacity>0),unit varchar(20),active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,code),foreign key(tenant_id,plant_id) references production.industrial_plants(tenant_id,id));
create table if not exists production.work_centers(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,line_id uuid not null,code varchar(40) not null,name varchar(160) not null,process_type varchar(60) not null,active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,code),foreign key(tenant_id,line_id) references production.production_lines(tenant_id,id));
create table if not exists production.production_equipment(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,work_center_id uuid not null,asset_id uuid,code varchar(40) not null,name varchar(160) not null,hourly_cost numeric(18,4) not null default 0 check(hourly_cost>=0),active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,work_center_id) references production.work_centers(tenant_id,id));
create table if not exists production.production_shifts(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,plant_id uuid not null,name varchar(100) not null,starts_at time not null,ends_at time not null,active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,plant_id) references production.industrial_plants(tenant_id,id));
create table if not exists production.production_teams(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,shift_id uuid not null,name varchar(120) not null,leader_id uuid,active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,shift_id) references production.production_shifts(tenant_id,id));
create table if not exists production.production_products(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),inventory_product_id uuid,code varchar(50) not null,name varchar(180) not null,product_type varchar(20) not null check(product_type in('FINISHED','SEMI_FINISHED','RAW_MATERIAL','PACKAGING')),unit varchar(20) not null,requires_lot boolean not null default true,requires_expiry boolean not null default false,amazon_origin_required boolean not null default false,export_documents_required boolean not null default false,active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,code));
create table if not exists production.production_recipes(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,code varchar(40) not null,name varchar(160) not null,finished_product_id uuid not null,active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,code),foreign key(tenant_id,finished_product_id) references production.production_products(tenant_id,id));
create table if not exists production.production_recipe_versions(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,recipe_id uuid not null,version int not null check(version>0),base_quantity numeric(18,4) not null check(base_quantity>0),unit varchar(20) not null,expected_yield numeric(9,4) not null check(expected_yield>0),expected_loss numeric(9,4) not null default 0 check(expected_loss between 0 and 100),standard_minutes int check(standard_minutes>=0),instructions text not null,status varchar(16) not null check(status in('DRAFT','APPROVED','INACTIVE')),technical_responsible_id uuid,valid_from date,valid_until date,approved_at timestamptz,approved_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,recipe_id,version),foreign key(tenant_id,recipe_id) references production.production_recipes(tenant_id,id),check(valid_until is null or valid_from is null or valid_until>=valid_from),check(status<>'APPROVED' or approved_at is not null));
create table if not exists production.production_recipe_items(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,recipe_version_id uuid not null,material_id uuid not null,quantity numeric(18,4) not null check(quantity>0),unit varchar(20) not null,loss_percent numeric(9,4) not null default 0 check(loss_percent between 0 and 100),sequence int not null default 1 check(sequence>0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,recipe_version_id) references production.production_recipe_versions(tenant_id,id),foreign key(tenant_id,material_id) references production.production_products(tenant_id,id));
create table if not exists production.production_steps(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,process_type varchar(60) not null,code varchar(40) not null,name varchar(160) not null,critical boolean not null default false,minimum_minutes int check(minimum_minutes>=0),minimum_temperature numeric(8,2),maximum_temperature numeric(8,2),mandatory_for_tucupi boolean not null default false,requires_evidence boolean not null default false,active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,code),check(maximum_temperature is null or minimum_temperature is null or maximum_temperature>=minimum_temperature));
create table if not exists production.production_orders(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,number varchar(30) not null,plant_id uuid not null,line_id uuid not null,finished_product_id uuid not null,recipe_version_id uuid not null,planned_batch varchar(100) not null,planned_quantity numeric(18,4) not null check(planned_quantity>0),produced_quantity numeric(18,4) not null default 0 check(produced_quantity>=0),consumed_quantity numeric(18,4) not null default 0 check(consumed_quantity>=0),unit varchar(20) not null,planned_at timestamptz not null,priority varchar(12) not null check(priority in('LOW','MEDIUM','HIGH','URGENT')),responsible_id uuid not null,demand_origin varchar(80) not null,sales_order_id uuid,requires_reservation boolean not null default false,stock_reserved boolean not null default false,status varchar(24) not null check(status in('DRAFT','PLANNED','AWAITING_MATERIALS','RELEASED','IN_PRODUCTION','PAUSED','IN_QUALITY','COMPLETED','BLOCKED','CANCELLED','CLOSED')),real_yield numeric(12,4) not null default 0 check(real_yield>=0),notes text,cancel_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,number),unique(tenant_id,planned_batch),foreign key(tenant_id,plant_id) references production.industrial_plants(tenant_id,id),foreign key(tenant_id,line_id) references production.production_lines(tenant_id,id),foreign key(tenant_id,finished_product_id) references production.production_products(tenant_id,id),foreign key(tenant_id,recipe_version_id) references production.production_recipe_versions(tenant_id,id),check(status<>'CANCELLED' or length(trim(cancel_reason))>=3));
create table if not exists production.production_order_events(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,order_id uuid not null,from_status varchar(24),to_status varchar(24) not null,reason text,metadata jsonb not null default '{}',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,order_id) references production.production_orders(tenant_id,id));
create table if not exists production.production_step_records(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,order_id uuid not null,step_id uuid not null,operator_id uuid not null,line_id uuid not null,equipment_id uuid,started_at timestamptz not null,ended_at timestamptz not null,produced_quantity numeric(18,4) not null check(produced_quantity>=0),consumed_quantity numeric(18,4) not null check(consumed_quantity>=0),loss_quantity numeric(18,4) not null default 0 check(loss_quantity>=0),scrap_quantity numeric(18,4) not null default 0 check(scrap_quantity>=0),loss_reason text,notes text,input_batch varchar(100),output_batch varchar(100) not null,temperature numeric(8,2),evidence_reference varchar(300),critical boolean not null,conformity_status varchar(20) not null check(conformity_status in('PENDING','CONFORMING','NON_CONFORMING')),status varchar(16) not null check(status in('OPEN','COMPLETED','VOIDED')),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,order_id) references production.production_orders(tenant_id,id),foreign key(tenant_id,step_id) references production.production_steps(tenant_id,id),check(ended_at>=started_at),check(loss_quantity=0 or length(trim(loss_reason))>=3));
create table if not exists production.production_material_consumptions(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,order_id uuid not null,material_id uuid not null,stock_lot_id uuid not null,quantity numeric(18,4) not null check(quantity>0),unit varchar(20) not null,returned_quantity numeric(18,4) not null default 0 check(returned_quantity>=0),justification text,status varchar(16) not null check(status in('PENDING','POSTED','REVERSED')),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,order_id) references production.production_orders(tenant_id,id),foreign key(tenant_id,material_id) references production.production_products(tenant_id,id),foreign key(tenant_id,stock_lot_id) references inventory.stock_lots(tenant_id,id));
create table if not exists production.production_batches(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,order_id uuid not null,product_id uuid not null,batch_number varchar(100) not null,quantity numeric(18,4) not null check(quantity>0),unit varchar(20) not null,manufactured_at timestamptz not null,expires_on date,quality_status varchar(16) not null default 'PENDING' check(quality_status in('PENDING','APPROVED','BLOCKED','REJECTED')),original_batch_id uuid,released_at timestamptz,released_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,batch_number),foreign key(tenant_id,order_id) references production.production_orders(tenant_id,id),foreign key(tenant_id,product_id) references production.production_products(tenant_id,id),foreign key(original_batch_id) references production.production_batches(id),check(quality_status<>'APPROVED' or (released_at is not null and released_by is not null)));
create table if not exists production.production_batch_traceability(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,batch_id uuid not null,relation_type varchar(30) not null check(relation_type in('RAW_MATERIAL','SUPPLIER','PURCHASE','RECEIPT','STEP','OPERATOR','EQUIPMENT','REPORT','DOCUMENT','EVIDENCE','LOSS','REWORK','SALE_ORDER','SHIPMENT')),source_batch varchar(100),source_entity varchar(60),source_id uuid,source_reference varchar(300),metadata jsonb not null default '{}',created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,batch_id) references production.production_batches(tenant_id,id));
create table if not exists production.production_losses(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,order_id uuid not null,step_record_id uuid,batch_id uuid,loss_type varchar(20) not null check(loss_type in('TECHNICAL','SCRAP','DISPOSAL','REPROCESS')),quantity numeric(18,4) not null check(quantity>0),unit varchar(20) not null,reason varchar(300) not null,responsible_id uuid not null,original_batch_id uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,order_id) references production.production_orders(tenant_id,id),foreign key(original_batch_id) references production.production_batches(id));
create table if not exists production.production_stoppages(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,line_id uuid not null,order_id uuid,equipment_id uuid,type varchar(20) not null check(type in('PLANNED','UNPLANNED','MAINTENANCE')),reason text not null,critical boolean not null default false,started_at timestamptz not null,ended_at timestamptz not null,duration_minutes int not null check(duration_minutes>0),responsible_id uuid not null,estimated_impact numeric(18,2) not null default 0 check(estimated_impact>=0),corrective_action text,status varchar(16) not null check(status in('OPEN','COMPLETED','CANCELLED')),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,line_id) references production.production_lines(tenant_id,id),foreign key(tenant_id,order_id) references production.production_orders(tenant_id,id),check(ended_at>started_at),check(length(trim(reason))>=3));
create table if not exists production.production_quality_checks(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,order_id uuid not null,batch_id uuid not null,step_id uuid,sample_reference varchar(100),status varchar(16) not null check(status in('PENDING','APPROVED','BLOCKED','REJECTED')),reason text,report_reference varchar(300),evidence_reference varchar(300),mandatory boolean not null default true,decided_at timestamptz,decided_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,foreign key(tenant_id,order_id) references production.production_orders(tenant_id,id),foreign key(tenant_id,batch_id) references production.production_batches(tenant_id,id),check(status not in('BLOCKED','REJECTED') or length(trim(reason))>=3),check(status='PENDING' or (decided_at is not null and decided_by is not null)));
create table if not exists production.production_costs(id uuid primary key default gen_random_uuid(),tenant_id uuid not null,order_id uuid not null,batch_id uuid,planned_cost numeric(18,4) not null default 0 check(planned_cost>=0),real_cost numeric(18,4) not null default 0 check(real_cost>=0),material_cost numeric(18,4) not null default 0 check(material_cost>=0),loss_cost numeric(18,4) not null default 0 check(loss_cost>=0),labor_cost numeric(18,4) not null default 0 check(labor_cost>=0),machine_cost numeric(18,4) not null default 0 check(machine_cost>=0),unit_cost numeric(18,6) not null default 0 check(unit_cost>=0),calculated_at timestamptz not null default now(),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,order_id) references production.production_orders(tenant_id,id));
create table if not exists production.production_reports_exports(id uuid primary key default gen_random_uuid(),tenant_id uuid not null references tenancy.tenants(id),report_type varchar(40) not null,filters jsonb not null default '{}',row_count int not null default 0 check(row_count>=0),exported_at timestamptz not null default now(),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid not null,updated_by uuid not null,deleted_at timestamptz);
create index if not exists ix_prod_orders_queue on production.production_orders(tenant_id,status,planned_at,line_id,finished_product_id);
create index if not exists ix_prod_orders_batch on production.production_orders(tenant_id,planned_batch);
create index if not exists ix_prod_recipes_status on production.production_recipe_versions(tenant_id,status,recipe_id,valid_until);
create index if not exists ix_prod_records_order on production.production_step_records(tenant_id,order_id,step_id,started_at);
create index if not exists ix_prod_consumption_order on production.production_material_consumptions(tenant_id,order_id,material_id,stock_lot_id);
create index if not exists ix_prod_batches_scope on production.production_batches(tenant_id,quality_status,batch_number,product_id,manufactured_at);
create index if not exists ix_prod_trace_batch on production.production_batch_traceability(tenant_id,batch_id,relation_type,source_batch);
create index if not exists ix_prod_losses_scope on production.production_losses(tenant_id,order_id,loss_type,created_at);
create index if not exists ix_prod_stoppages_scope on production.production_stoppages(tenant_id,line_id,status,started_at);
create index if not exists ix_prod_quality_scope on production.production_quality_checks(tenant_id,status,order_id,batch_id);
create index if not exists ix_prod_cost_scope on production.production_costs(tenant_id,order_id,batch_id,calculated_at);
do $$ declare tab text;begin for tab in select tablename from pg_tables where schemaname='production' loop execute format('alter table production.%I enable row level security',tab);execute format('alter table production.%I force row level security',tab);if not exists(select 1 from pg_policies where schemaname='production' and tablename=tab and policyname=tab||'_tenant') then execute format('create policy %I on production.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',tab||'_tenant',tab);end if;end loop;end $$;
insert into identity.permissions(code,module,description) values ('production.read','Produção Agroindustrial','Consultar PCP, lotes e rastreabilidade.'),('production.write','Produção Agroindustrial','Cadastrar estrutura, formulações e ordens.'),('production.release','Produção Agroindustrial','Aprovar formulações e liberar, pausar, concluir ou bloquear ordens.'),('production.operate','Produção Agroindustrial','Registrar apontamentos, consumos, perdas e paradas.'),('production.quality','Produção Agroindustrial','Bloquear, reprovar e liberar lotes.'),('production.cancel','Produção Agroindustrial','Cancelar ordens com motivo.'),('production.export','Produção Agroindustrial','Exportar relatórios industriais.') on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('3.8.0','Sprint 38 - PCP, beneficiamento, qualidade e rastreabilidade industrial',now()) on conflict(version) do nothing;
commit;
begin;
create schema if not exists export_trading;
create table export_trading.incoterms(code varchar(3) primary key, name text not null, seller_responsibilities text not null, buyer_responsibilities text not null, active boolean not null default true);
insert into export_trading.incoterms values
('EXW','Ex Works','Disponibilizar mercadoria','Coleta, exportação, frete e seguro',true),('FCA','Free Carrier','Entregar ao transportador e desembaraçar exportação','Frete principal e importação',true),('FAS','Free Alongside Ship','Entregar ao lado do navio','Carregamento, frete, seguro e importação',true),('FOB','Free On Board','Carregar a bordo','Frete, seguro e importação',true),('CFR','Cost and Freight','Frete até destino','Risco após embarque e seguro',true),('CIF','Cost Insurance and Freight','Frete e seguro até destino','Importação',true),('CPT','Carriage Paid To','Transporte até destino','Risco após entrega ao transportador',true),('CIP','Carriage and Insurance Paid To','Transporte e seguro','Importação',true),('DAP','Delivered at Place','Entrega no destino','Descarga e importação',true),('DPU','Delivered at Place Unloaded','Entrega descarregada','Importação',true),('DDP','Delivered Duty Paid','Entrega e importação','Recebimento',true) on conflict do nothing;
create table export_trading.customers(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(200) not null,country char(2) not null,city varchar(120),tax_document varchar(80),main_contact varchar(160),email varchar(254),phone varchar(40),language varchar(10) not null default 'pt-BR',currency char(3) not null,commercial_terms text,preferred_incoterm varchar(3) references export_trading.incoterms(code),status varchar(20) not null check(status in('ACTIVE','INACTIVE','BLOCKED','UNDER_REVIEW','APPROVED','REJECTED')),risk varchar(20) not null,rejection_reason text,notes text,tags text[] not null default '{}',approved_at timestamptz,approved_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,check(status<>'REJECTED' or nullif(trim(rejection_reason),'') is not null),unique(tenant_id,id));
create table export_trading.trade_partners(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),name varchar(200) not null,type varchar(20) not null check(type in('IMPORTER','TRADING','BROKER','FREIGHT_AGENT')),country char(2) not null,email varchar(254),phone varchar(40),status varchar(20) not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id));
create table export_trading.contracts(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),number varchar(50) not null,customer_id uuid not null,trade_partner_id uuid,currency char(3) not null,total numeric(20,2) not null check(total>0),incoterm varchar(3) not null references export_trading.incoterms(code),origin_port varchar(160) not null,destination_port varchar(160) not null,destination_country char(2) not null,contract_date date not null,expected_shipment_date date not null,payment_terms text not null,status varchar(30) not null check(status in('DRAFT','UNDER_REVIEW','APPROVED','AWAITING_DOCUMENTS','AWAITING_PRODUCTION','AWAITING_SHIPMENT','SHIPPED','DELIVERED','CANCELLED','CLOSED')),notes text,cancellation_reason text,approved_at timestamptz,approved_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,number),unique(tenant_id,id),foreign key(tenant_id,customer_id) references export_trading.customers(tenant_id,id),foreign key(tenant_id,trade_partner_id) references export_trading.trade_partners(tenant_id,id),check(status<>'CANCELLED' or nullif(trim(cancellation_reason),'') is not null));
create table export_trading.contract_items(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),contract_id uuid not null,product_id uuid not null references inventory.products(id),lot_id uuid references storage.lots(id),quantity numeric(20,6) not null check(quantity>0),unit varchar(20) not null,unit_price numeric(20,6) not null check(unit_price>0),total numeric(20,2) not null check(total>0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,contract_id) references export_trading.contracts(tenant_id,id));
create table export_trading.shipments(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),number varchar(50) not null,contract_id uuid not null,expected_date date not null,actual_date date,origin_terminal varchar(160) not null,destination varchar(200) not null,carrier varchar(160),freight_agent varchar(160),status varchar(30) not null check(status in('PLANNED','AWAITING_DOCUMENTS','AWAITING_RELEASE','RELEASED','IN_TRANSIT','SHIPPED','DELIVERED','PENDING','CANCELLED')),notes text,cancellation_reason text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,number),unique(tenant_id,id),foreign key(tenant_id,contract_id) references export_trading.contracts(tenant_id,id));
create table export_trading.shipment_items(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),shipment_id uuid not null,contract_item_id uuid not null,lot_id uuid not null references storage.lots(id),quantity numeric(20,6) not null check(quantity>0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,foreign key(tenant_id,shipment_id) references export_trading.shipments(tenant_id,id),foreign key(tenant_id,contract_item_id) references export_trading.contract_items(tenant_id,id));
create table export_trading.documents(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),contract_id uuid,shipment_id uuid,type varchar(40) not null check(type in('PROFORMA','COMMERCIAL_INVOICE','PACKING_LIST','ORIGIN_CERTIFICATE','SANITARY_CERTIFICATE','PHYTOSANITARY_CERTIFICATE','QUALITY_REPORT','ENVIRONMENTAL','TRACEABILITY','BILL_OF_LADING','EVIDENCE')),storage_document_id uuid references documents.files(id),number varchar(100),issued_on date,expires_on date,required boolean not null default false,status varchar(20) not null check(status in('PENDING','APPROVED','REJECTED','EXPIRED')),rejection_reason text,approved_at timestamptz,approved_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,check(status<>'REJECTED' or nullif(trim(rejection_reason),'') is not null));
create table export_trading.currency_rates(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),contract_id uuid not null,source_currency char(3) not null,base_currency char(3) not null,rate numeric(20,8) not null check(rate>0),rate_date date not null,foreign_value numeric(20,2) not null,base_value numeric(20,2) not null,estimated_variation numeric(20,2),notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,foreign key(tenant_id,contract_id) references export_trading.contracts(tenant_id,id));
create table export_trading.costs(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),contract_id uuid not null,shipment_id uuid,category varchar(30) not null check(category in('INTERNAL_FREIGHT','INTERNATIONAL_FREIGHT','INSURANCE','BROKER','PORT_FEES','DOCUMENTATION','INSPECTION','STORAGE','COMMISSION','OTHER')),currency char(3) not null,amount numeric(20,2) not null check(amount>=0),exchange_rate numeric(20,8) check(exchange_rate>0),amount_base numeric(20,2) not null check(amount_base>=0),incurred_on date,notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,foreign key(tenant_id,contract_id) references export_trading.contracts(tenant_id,id));
create table export_trading.compliance_checks(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),contract_id uuid,shipment_id uuid,rule varchar(160) not null,status varchar(20) not null,blocks_shipment boolean not null default false,evidence_document_id uuid,checked_at timestamptz,checked_by uuid,notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid);
create table export_trading.traceability(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),contract_id uuid not null,shipment_id uuid not null,lot_id uuid not null,production_order_id uuid,receipt_id uuid,supplier_id uuid,document_id uuid,destination text not null,created_at timestamptz not null default now(),created_by uuid);
create table export_trading.events(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),entity_type varchar(30) not null,entity_id uuid not null,event_type varchar(40) not null,data jsonb not null default '{}',created_at timestamptz not null default now(),created_by uuid);
create index ix_export_customers_tenant_status on export_trading.customers(tenant_id,status,country) where deleted_at is null; create index ix_export_contracts_tenant_status_date on export_trading.contracts(tenant_id,status,contract_date,destination_country) where deleted_at is null; create index ix_export_contract_items_product_lot on export_trading.contract_items(tenant_id,product_id,lot_id); create index ix_export_shipments_tenant_status_date on export_trading.shipments(tenant_id,status,expected_date); create index ix_export_documents_pending on export_trading.documents(tenant_id,status,expires_on) where required; create index ix_export_costs_contract on export_trading.costs(tenant_id,contract_id,category); create index ix_export_trace_lot on export_trading.traceability(tenant_id,lot_id,contract_id); create index ix_export_events_entity on export_trading.events(tenant_id,entity_type,entity_id,created_at desc);
do $$ declare tab text; begin for tab in select tablename from pg_tables where schemaname='export_trading' and tablename<>'incoterms' loop execute format('alter table export_trading.%I enable row level security',tab); execute format('alter table export_trading.%I force row level security',tab); if not exists(select 1 from pg_policies where schemaname='export_trading' and tablename=tab and policyname=tab||'_tenant') then execute format('create policy %I on export_trading.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',tab||'_tenant',tab); end if; end loop; end $$;
insert into identity.permissions(code,module,description) values ('export.read','Exportação e Trading','Consultar exportação internacional.'),('export.write','Exportação e Trading','Operar clientes, contratos e embarques.'),('export.approve','Exportação e Trading','Aprovar e cancelar contratos.'),('export.reports','Exportação e Trading','Exportar relatórios CSV.') on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('3.9.0','Sprint 39 - Exportacao e Trading',now()) on conflict(version) do nothing;
commit;
begin;
create table fiscal_operations(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),code varchar(30) not null,name varchar(160) not null,type varchar(30) not null check(type in('INTERNAL_SALE','INTERSTATE_SALE','EXPORT','SALE_RETURN','PURCHASE_RETURN','SHIPMENT','RETURN','BONUS','SAMPLE','UNIT_TRANSFER','PURCHASE','IMPORT_ENTRY','SERVICE','OTHER')),purpose varchar(80) not null,suggested_cfop char(4),moves_stock boolean not null default false,generates_financial boolean not null default false,requires_transport boolean not null default false,requires_document boolean not null default true,requires_cost_center boolean not null default false,active boolean not null default true,notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,code),check(suggested_cfop is null or suggested_cfop~'^[0-9]{4}$'));
create table fiscal_operation_rules(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),operation_id uuid not null,product_id uuid,category_id uuid,origin_state char(2) not null,destination_state char(2) not null,cfop char(4) not null,cst varchar(4),csosn varchar(4),icms_rate numeric(7,4),pis_rate numeric(7,4),cofins_rate numeric(7,4),iss_rate numeric(7,4),base_reduction numeric(7,4),active boolean not null default true,valid_from date not null,valid_until date,notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),foreign key(tenant_id,operation_id) references fiscal_operations(tenant_id,id),foreign key(tenant_id,product_id) references inventory.products(tenant_id,id),check(cfop~'^[0-9]{4}$' and (valid_until is null or valid_until>=valid_from)),check(icms_rate between 0 and 100 and pis_rate between 0 and 100 and cofins_rate between 0 and 100 and iss_rate between 0 and 100 and base_reduction between 0 and 100));
create table fiscal_series(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),type varchar(30) not null,series varchar(20) not null,next_number bigint not null default 1 check(next_number>0),active boolean not null default true,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,type,series),unique(tenant_id,id));
create table fiscal_invoice_drafts(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),internal_number varchar(50) not null,customer_id uuid not null,operation_id uuid not null,sale_order_id uuid,export_contract_id uuid,payment_terms text,carrier_id uuid,delivery_address text,freight numeric(20,2) not null default 0,insurance numeric(20,2) not null default 0,other_expenses numeric(20,2) not null default 0,items_total numeric(20,2) not null,informed_taxes numeric(20,2) not null default 0,total numeric(20,2) not null,status varchar(30) not null check(status in('DRAFT','UNDER_REVIEW','AWAITING_DOCUMENT','DOCUMENT_PENDING','EXTERNALLY_ISSUED','EXTERNALLY_AUTHORIZED','REJECTED','CANCELLED','FINANCIALLY_INTEGRATED','CLOSED')),status_reason text,notes text,confirmed_at timestamptz,confirmed_by uuid,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,internal_number),foreign key(tenant_id,customer_id) references crm.customers(tenant_id,id),foreign key(tenant_id,operation_id) references fiscal_operations(tenant_id,id),check(freight>=0 and insurance>=0 and other_expenses>=0 and items_total>=0 and informed_taxes>=0 and total>=0),check(status not in('REJECTED','CANCELLED') or nullif(trim(status_reason),'') is not null));
create table fiscal_invoice_items(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),invoice_id uuid not null,product_id uuid not null,lot_id uuid,unit varchar(20) not null,quantity numeric(20,6) not null,unit_price numeric(20,6) not null,discount numeric(20,2) not null default 0,informed_taxes numeric(20,2) not null default 0,total numeric(20,2) not null,is_service boolean not null default false,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,foreign key(tenant_id,invoice_id) references fiscal_invoice_drafts(tenant_id,id),foreign key(tenant_id,product_id) references inventory.products(tenant_id,id),check(quantity>0 and unit_price>0 and discount>=0 and discount<=quantity*unit_price and informed_taxes>=0 and total>=0));
create table fiscal_invoice_installments(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),invoice_id uuid not null,due_date date not null,amount numeric(20,2) not null check(amount>0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,foreign key(tenant_id,invoice_id) references fiscal_invoice_drafts(tenant_id,id));
create table fiscal_documents(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),type varchar(30) not null check(type in('NFE','NFSE','NFCE','CTE','MDFE','PRODUCER_NOTE','ENTRY','EXPORT_DOCUMENT','OTHER')),number varchar(40) not null,series varchar(20) not null,access_key char(44),external_protocol varchar(100),issued_at timestamptz not null,authorized_at timestamptz,customer_id uuid,supplier_id uuid,operation_id uuid not null,status varchar(35) not null check(status in('PROVIDER_PENDING','NOT_CONFIGURED','AWAITING_EXTERNAL_ISSUANCE','EXTERNALLY_ISSUED','EXTERNALLY_AUTHORIZED','REJECTED','CANCELLED')),status_reason text,total numeric(20,2) not null check(total>=0),document_reference text,notes text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,deleted_at timestamptz,unique(tenant_id,id),unique(tenant_id,type,series,number),unique(tenant_id,access_key),foreign key(tenant_id,operation_id) references fiscal_operations(tenant_id,id),check(access_key is null or access_key~'^[0-9]{44}$'),check(status not in('REJECTED','CANCELLED') or nullif(trim(status_reason),'') is not null));
create table fiscal_document_items(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),document_id uuid not null,product_id uuid,description text not null,quantity numeric(20,6) not null check(quantity>0),unit_price numeric(20,6) not null check(unit_price>=0),total numeric(20,2) not null check(total>=0),created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,foreign key(tenant_id,document_id) references fiscal_documents(tenant_id,id));
create table fiscal_document_events(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),entity_type varchar(30) not null,entity_id uuid not null,event_type varchar(40) not null,data jsonb not null default '{}',created_at timestamptz not null default now(),created_by uuid);
create table fiscal_purchase_checks(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),purchase_order_id uuid not null,receipt_id uuid,supplier_id uuid not null,fiscal_document_id uuid not null,expected_total numeric(20,2) not null,received_total numeric(20,2) not null,status varchar(20) not null check(status in('PENDING','DIVERGENT','APPROVED','REJECTED')),justification text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,fiscal_document_id) references fiscal_documents(tenant_id,id),check(expected_total>=0 and received_total>=0));
create table fiscal_purchase_check_items(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),purchase_check_id uuid not null,product_id uuid not null,expected_quantity numeric(20,6) not null,received_quantity numeric(20,6) not null,expected_value numeric(20,2) not null,received_value numeric(20,2) not null,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,foreign key(tenant_id,purchase_check_id) references fiscal_purchase_checks(tenant_id,id));
create table fiscal_divergences(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),purchase_check_id uuid not null,type varchar(30) not null,expected_value numeric(20,6),received_value numeric(20,6),status varchar(20) not null check(status in('OPEN','JUSTIFIED','RESOLVED')),justification text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,foreign key(tenant_id,purchase_check_id) references fiscal_purchase_checks(tenant_id,id));
create table fiscal_tax_summaries(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),document_id uuid not null,tax_type varchar(20) not null,tax_base numeric(20,2) not null,tax_rate numeric(7,4),tax_value numeric(20,2) not null,retained boolean not null default false,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid,foreign key(tenant_id,document_id) references fiscal_documents(tenant_id,id),check(tax_base>=0 and tax_value>=0 and tax_rate between 0 and 100));
create table fiscal_stock_movements(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),invoice_id uuid,document_id uuid,product_id uuid not null,lot_id uuid,type varchar(30) not null,quantity numeric(20,6) not null check(quantity>0),status varchar(30) not null,message text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid);
create table fiscal_financial_integrations(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),type varchar(20) not null,reference_id uuid not null,status varchar(30) not null check(status in('PENDING','INTEGRATED','FAILED','NOT_CONFIGURED','CANCELLED')),message text,created_at timestamptz not null default now(),updated_at timestamptz not null default now(),created_by uuid,updated_by uuid);
create table fiscal_reports_exports(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),report varchar(60) not null,filters jsonb not null default '{}',row_count integer not null default 0,created_at timestamptz not null default now(),created_by uuid);
create index ix_fiscal_operations_tenant_status on fiscal_operations(tenant_id,active,name) where deleted_at is null;create index ix_fiscal_rules_tenant_operation_product on fiscal_operation_rules(tenant_id,operation_id,product_id,origin_state,destination_state,valid_from) where deleted_at is null;create index ix_fiscal_invoices_tenant_status_date on fiscal_invoice_drafts(tenant_id,status,created_at,customer_id,operation_id) where deleted_at is null;create index ix_fiscal_documents_tenant_status_date on fiscal_documents(tenant_id,status,issued_at,customer_id,supplier_id) where deleted_at is null;create index ix_fiscal_checks_tenant_status on fiscal_purchase_checks(tenant_id,status,created_at,supplier_id);create index ix_fiscal_divergences_open on fiscal_divergences(tenant_id,status,created_at);create index ix_fiscal_stock_status on fiscal_stock_movements(tenant_id,status,created_at,product_id);create index ix_fiscal_financial_status on fiscal_financial_integrations(tenant_id,status,created_at);
do $$ declare tab text; begin for tab in select tablename from pg_tables where schemaname='public' and tablename like 'fiscal_%' loop execute format('alter table %I enable row level security',tab);execute format('alter table %I force row level security',tab);execute format('create policy %I on %I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',tab||'_tenant',tab);end loop;end $$;
insert into identity.permissions(code,module,description) values('fiscal.read','Fiscal e Faturamento','Consultar dados fiscais.'),('fiscal.write','Fiscal e Faturamento','Cadastrar operações, regras e documentos.'),('fiscal.approve','Fiscal e Faturamento','Confirmar faturamentos e conferências.'),('fiscal.reports','Fiscal e Faturamento','Exportar relatórios fiscais.') on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('4.0.0','Sprint 40 - Fiscal e Faturamento',now()) on conflict(version) do nothing;
commit;
