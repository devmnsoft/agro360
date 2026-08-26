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
