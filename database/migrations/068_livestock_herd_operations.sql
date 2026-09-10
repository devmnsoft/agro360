begin;

-- Pecuária integrada: cadastros, controle individual vs quantidade, movimentações,
-- ordens de manejo, pesagens, restrições, alimentação, reserva comercial e custos.
-- Idempotente. Não altera checksums de migrations anteriores.

alter table agro360.livestock_herds
    add column if not exists control_mode varchar(16) not null default 'INDIVIDUAL',
    add column if not exists facility_id uuid,
    add column if not exists notes text;
do $$ begin
    alter table agro360.livestock_herds
        add constraint ck_herds_control_mode check (control_mode in ('INDIVIDUAL', 'QUANTITY'));
exception when duplicate_object then null;
end $$;

alter table agro360.livestock_animals
    add column if not exists origin_type varchar(30),
    add column if not exists origin_notes varchar(500),
    add column if not exists birth_date_estimated boolean not null default false,
    add column if not exists notes text,
    add column if not exists facility_id uuid,
    add column if not exists handling_lot_id uuid,
    add column if not exists inactivated_at timestamptz,
    add column if not exists inactivated_reason varchar(240),
    add column if not exists reservation_id uuid;
do $$ begin
    alter table agro360.livestock_animals
        add constraint ck_animals_origin_type check (
            origin_type is null or origin_type in ('PURCHASE', 'BIRTH', 'TRANSFER_IN', 'INVENTORY', 'OTHER'));
exception when duplicate_object then null;
end $$;

create table if not exists agro360.livestock_species (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    code varchar(40) not null,
    name varchar(80) not null,
    active boolean not null default true,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz,
    updated_by uuid,
    unique (tenant_id, id),
    unique (tenant_id, code)
);

create table if not exists agro360.livestock_categories (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    species_code varchar(40) not null,
    code varchar(40) not null,
    name varchar(80) not null,
    sex varchar(20),
    active boolean not null default true,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz,
    updated_by uuid,
    unique (tenant_id, id),
    unique (tenant_id, species_code, code)
);

create table if not exists agro360.livestock_breeds (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    species_code varchar(40) not null,
    code varchar(40) not null,
    name varchar(80) not null,
    active boolean not null default true,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz,
    updated_by uuid,
    unique (tenant_id, id),
    unique (tenant_id, species_code, code)
);

create table if not exists agro360.livestock_facilities (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    farm_id uuid not null,
    name varchar(120) not null,
    kind varchar(30) not null check (kind in ('PASTURE', 'PADDOCK', 'CORRAL', 'BARN', 'PEN', 'OTHER')),
    paddock_id uuid references agro360.livestock_paddocks(id),
    capacity_head integer check (capacity_head is null or capacity_head >= 0),
    status varchar(20) not null default 'AVAILABLE' check (status in ('AVAILABLE', 'IN_USE', 'INACTIVE')),
    notes text,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz,
    updated_by uuid,
    deleted_at timestamptz,
    unique (tenant_id, id),
    unique (tenant_id, farm_id, name),
    foreign key (tenant_id, farm_id) references agro360.geo_farms(tenant_id, id)
);

create table if not exists agro360.livestock_movement_reasons (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    code varchar(40) not null,
    name varchar(120) not null,
    direction varchar(12) not null check (direction in ('ENTRY', 'EXIT', 'TRANSFER', 'ADJUST')),
    active boolean not null default true,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique (tenant_id, id),
    unique (tenant_id, code)
);

create table if not exists agro360.livestock_handling_types (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    code varchar(40) not null,
    name varchar(120) not null,
    active boolean not null default true,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique (tenant_id, id),
    unique (tenant_id, code)
);

create table if not exists agro360.livestock_health_protocols (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    name varchar(160) not null,
    source varchar(240) not null,
    version_label varchar(40) not null,
    valid_from date not null,
    valid_until date,
    purpose varchar(40) not null check (purpose in ('MILK', 'SLAUGHTER', 'SALE', 'OPERATIONAL', 'DEMO')),
    withdrawal_days integer not null default 0 check (withdrawal_days >= 0),
    notes text,
    demo_only boolean not null default false,
    active boolean not null default true,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique (tenant_id, id),
    unique (tenant_id, name, version_label)
);

create table if not exists agro360.livestock_handling_lots (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    farm_id uuid not null,
    name varchar(120) not null,
    purpose varchar(80) not null,
    status varchar(20) not null default 'OPEN' check (status in ('OPEN', 'CLOSED')),
    notes text,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz,
    updated_by uuid,
    closed_at timestamptz,
    unique (tenant_id, id),
    unique (tenant_id, farm_id, name),
    foreign key (tenant_id, farm_id) references agro360.geo_farms(tenant_id, id)
);

create table if not exists agro360.livestock_handling_lot_members (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    lot_id uuid not null,
    animal_id uuid not null,
    added_on date not null,
    removed_on date,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique (tenant_id, lot_id, animal_id, added_on),
    foreign key (tenant_id, lot_id) references agro360.livestock_handling_lots(tenant_id, id),
    foreign key (tenant_id, animal_id) references agro360.livestock_animals(tenant_id, id)
);

create table if not exists agro360.livestock_animal_identifiers (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    animal_id uuid not null,
    kind varchar(20) not null check (kind in ('TAG', 'RFID', 'SISBOV', 'INTERNAL')),
    value varchar(120) not null,
    assigned_on date not null,
    retired_on date,
    reason varchar(240),
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique (tenant_id, id),
    foreign key (tenant_id, animal_id) references agro360.livestock_animals(tenant_id, id)
);
create unique index if not exists ux_livestock_active_identifier
    on agro360.livestock_animal_identifiers (tenant_id, kind, lower(value))
    where retired_on is null;

create table if not exists agro360.livestock_herd_movements (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    herd_id uuid not null,
    movement_kind varchar(20) not null check (movement_kind in ('ENTRY', 'EXIT', 'TRANSFER', 'ADJUST', 'RECONCILE')),
    quantity integer not null check (quantity > 0),
    occurred_on date not null,
    reason_code varchar(40) not null,
    origin_notes varchar(500),
    responsible_id uuid,
    from_farm_id uuid,
    to_farm_id uuid,
    from_facility_id uuid,
    to_facility_id uuid,
    idempotency_key varchar(160),
    notes text,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique (tenant_id, id),
    foreign key (tenant_id, herd_id) references agro360.livestock_herds(tenant_id, id)
);
create unique index if not exists ux_livestock_herd_movements_idempotency
    on agro360.livestock_herd_movements (tenant_id, idempotency_key)
    where idempotency_key is not null;

alter table agro360.livestock_animal_movements
    add column if not exists movement_kind varchar(20) not null default 'TRANSFER',
    add column if not exists reason_code varchar(40),
    add column if not exists responsible_id uuid,
    add column if not exists from_herd_id uuid,
    add column if not exists to_herd_id uuid,
    add column if not exists from_facility_id uuid,
    add column if not exists to_facility_id uuid,
    add column if not exists reversed_by uuid,
    add column if not exists reversal_of uuid;
do $$ begin
    alter table agro360.livestock_animal_movements
        add constraint ck_animal_movements_kind check (
            movement_kind in ('ENTRY', 'TRANSFER', 'LOT_CHANGE', 'LOCATION_CHANGE', 'EXIT', 'ADJUST', 'REVERSAL'));
exception when duplicate_object then null;
end $$;

create table if not exists agro360.livestock_weighings (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    farm_id uuid not null,
    scope varchar(16) not null check (scope in ('INDIVIDUAL', 'COLLECTIVE')),
    animal_id uuid,
    herd_id uuid,
    handling_lot_id uuid,
    weighed_at timestamptz not null,
    weight_kg numeric(12,3) not null check (weight_kg > 0),
    unit varchar(16) not null default 'kg',
    converted_from_unit varchar(16),
    conversion_factor numeric(18,8),
    head_count integer check (head_count is null or head_count > 0),
    method varchar(40),
    equipment varchar(80),
    source varchar(20) not null default 'MANUAL' check (source in ('MANUAL', 'IMPORT')),
    responsible_id uuid,
    notes text,
    review_required boolean not null default false,
    review_justification text,
    previous_weight_kg numeric(12,3),
    corrected_from_id uuid,
    idempotency_key varchar(160),
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique (tenant_id, id),
    check ((scope = 'INDIVIDUAL' and animal_id is not null and herd_id is null)
        or (scope = 'COLLECTIVE' and animal_id is null and (herd_id is not null or handling_lot_id is not null))),
    foreign key (tenant_id, farm_id) references agro360.geo_farms(tenant_id, id)
);
create unique index if not exists ux_livestock_weighings_idempotency
    on agro360.livestock_weighings (tenant_id, idempotency_key)
    where idempotency_key is not null;
create index if not exists ix_livestock_weighings_timeline
    on agro360.livestock_weighings (tenant_id, animal_id, weighed_at desc);

create table if not exists agro360.livestock_weight_limits (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    species_code varchar(40) not null,
    min_kg numeric(12,3) not null check (min_kg > 0),
    max_kg numeric(12,3) not null check (max_kg > min_kg),
    unique (tenant_id, species_code)
);

create table if not exists agro360.livestock_handling_orders (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    farm_id uuid not null,
    handling_type varchar(40) not null,
    location_id uuid,
    facility_id uuid,
    planned_on date not null,
    responsible_id uuid not null,
    team_notes varchar(500),
    instructions text,
    priority varchar(12) not null default 'MEDIUM' check (priority in ('LOW', 'MEDIUM', 'HIGH', 'CRITICAL')),
    status varchar(20) not null default 'DRAFT'
        check (status in ('DRAFT', 'SCHEDULED', 'RELEASED', 'IN_PROGRESS', 'PAUSED', 'COMPLETED', 'CANCELLED')),
    planned_head_count integer not null default 0 check (planned_head_count >= 0),
    attended_head_count integer not null default 0 check (attended_head_count >= 0),
    not_attended_head_count integer not null default 0 check (not_attended_head_count >= 0),
    blocked_head_count integer not null default 0 check (blocked_head_count >= 0),
    work_task_id uuid,
    mass_atomic boolean not null default false,
    notes text,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz,
    updated_by uuid,
    unique (tenant_id, id),
    foreign key (tenant_id, farm_id) references agro360.geo_farms(tenant_id, id)
);

create table if not exists agro360.livestock_handling_order_items (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    order_id uuid not null,
    animal_id uuid,
    herd_id uuid,
    planned boolean not null default true,
    outcome varchar(20) not null default 'PLANNED'
        check (outcome in ('PLANNED', 'ATTENDED', 'NOT_ATTENDED', 'BLOCKED', 'FAILED')),
    outcome_reason varchar(240),
    unique (tenant_id, id),
    check (animal_id is not null or herd_id is not null),
    foreign key (tenant_id, order_id) references agro360.livestock_handling_orders(tenant_id, id)
);
create unique index if not exists ux_handling_order_animal
    on agro360.livestock_handling_order_items (tenant_id, order_id, animal_id)
    where animal_id is not null;

create table if not exists agro360.livestock_handling_order_materials (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    order_id uuid not null,
    product_id uuid not null,
    warehouse_id uuid,
    planned_quantity numeric(14,4) not null check (planned_quantity > 0),
    unit varchar(16) not null,
    consumed_quantity numeric(14,4) not null default 0 check (consumed_quantity >= 0),
    foreign key (tenant_id, order_id) references agro360.livestock_handling_orders(tenant_id, id)
);

create table if not exists agro360.livestock_restrictions (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    animal_id uuid,
    herd_id uuid,
    purpose varchar(40) not null check (purpose in ('MILK', 'SLAUGHTER', 'SALE', 'OPERATIONAL')),
    protocol_id uuid,
    source_event_id uuid,
    reason varchar(240) not null,
    started_on date not null,
    expected_until date,
    released_on date,
    release_authority varchar(120),
    release_criteria varchar(240),
    status varchar(16) not null default 'ACTIVE' check (status in ('ACTIVE', 'RELEASED', 'EXPIRED')),
    demo_only boolean not null default false,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique (tenant_id, id),
    check (animal_id is not null or herd_id is not null)
);
create index if not exists ix_livestock_restrictions_active
    on agro360.livestock_restrictions (tenant_id, animal_id, purpose)
    where status = 'ACTIVE';

alter table agro360.livestock_feedings
    add column if not exists planned_quantity numeric(14,4),
    add column if not exists supplied_quantity numeric(14,4),
    add column if not exists returned_quantity numeric(14,4) not null default 0,
    add column if not exists lost_quantity numeric(14,4) not null default 0,
    add column if not exists consumed_quantity numeric(14,4),
    add column if not exists product_id uuid,
    add column if not exists lot_number varchar(100),
    add column if not exists unit varchar(16),
    add column if not exists return_reusable boolean,
    add column if not exists responsible_id uuid,
    add column if not exists idempotency_key varchar(160);
create unique index if not exists ux_livestock_feedings_idempotency
    on agro360.livestock_feedings (tenant_id, idempotency_key)
    where idempotency_key is not null;

create table if not exists agro360.livestock_sale_reservations (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    farm_id uuid not null,
    animal_id uuid,
    herd_id uuid,
    quantity integer not null default 1 check (quantity > 0),
    status varchar(24) not null default 'ACTIVE'
        check (status in ('ACTIVE', 'CANCELLED', 'EXIT_CONFIRMED', 'FINANCIAL_PENDING', 'FINANCIAL_CONFIRMED')),
    reserved_on date not null,
    buyer_name varchar(160),
    notes text,
    sale_id uuid,
    receivable_id uuid,
    idempotency_key varchar(160),
    created_at timestamptz not null default now(),
    created_by uuid not null,
    cancelled_at timestamptz,
    cancelled_by uuid,
    unique (tenant_id, id),
    check (animal_id is not null or herd_id is not null),
    foreign key (tenant_id, farm_id) references agro360.geo_farms(tenant_id, id)
);
create unique index if not exists ux_livestock_active_animal_reservation
    on agro360.livestock_sale_reservations (tenant_id, animal_id)
    where animal_id is not null and status = 'ACTIVE';
create unique index if not exists ux_livestock_reservations_idempotency
    on agro360.livestock_sale_reservations (tenant_id, idempotency_key)
    where idempotency_key is not null;

create table if not exists agro360.livestock_cost_allocations (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    farm_id uuid not null,
    animal_id uuid,
    herd_id uuid,
    source_type varchar(60) not null,
    source_id uuid not null,
    category varchar(40) not null check (category in (
        'ACQUISITION', 'FEEDING', 'MATERIAL', 'SERVICE', 'HANDLING', 'LABOR', 'EQUIPMENT', 'LOSS', 'REVENUE')),
    nature varchar(20) not null check (nature in ('REALIZED', 'COMMITMENT', 'ESTIMATE')),
    amount numeric(18,4) not null,
    currency char(3) not null default 'BRL',
    occurred_on date not null,
    basis varchar(80),
    period_start date,
    period_end date,
    notes text,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique (tenant_id, id)
);
create unique index if not exists ux_livestock_cost_allocations_source
    on agro360.livestock_cost_allocations (
        tenant_id, source_type, source_id, category,
        coalesce(animal_id, '00000000-0000-0000-0000-000000000000'::uuid),
        coalesce(herd_id, '00000000-0000-0000-0000-000000000000'::uuid));

do $$ begin
    alter table agro360.livestock_animals
        add constraint fk_animals_facility foreign key (facility_id) references agro360.livestock_facilities(id);
exception when duplicate_object then null;
end $$;
do $$ begin
    alter table agro360.livestock_animals
        add constraint fk_animals_handling_lot foreign key (tenant_id, handling_lot_id)
            references agro360.livestock_handling_lots(tenant_id, id);
exception when duplicate_object then null;
end $$;

select agro360.platform_enable_tenant_rls('agro360.livestock_species');
select agro360.platform_enable_tenant_rls('agro360.livestock_categories');
select agro360.platform_enable_tenant_rls('agro360.livestock_breeds');
select agro360.platform_enable_tenant_rls('agro360.livestock_facilities');
select agro360.platform_enable_tenant_rls('agro360.livestock_movement_reasons');
select agro360.platform_enable_tenant_rls('agro360.livestock_handling_types');
select agro360.platform_enable_tenant_rls('agro360.livestock_health_protocols');
select agro360.platform_enable_tenant_rls('agro360.livestock_handling_lots');
select agro360.platform_enable_tenant_rls('agro360.livestock_handling_lot_members');
select agro360.platform_enable_tenant_rls('agro360.livestock_animal_identifiers');
select agro360.platform_enable_tenant_rls('agro360.livestock_herd_movements');
select agro360.platform_enable_tenant_rls('agro360.livestock_weighings');
select agro360.platform_enable_tenant_rls('agro360.livestock_weight_limits');
select agro360.platform_enable_tenant_rls('agro360.livestock_handling_orders');
select agro360.platform_enable_tenant_rls('agro360.livestock_handling_order_items');
select agro360.platform_enable_tenant_rls('agro360.livestock_handling_order_materials');
select agro360.platform_enable_tenant_rls('agro360.livestock_restrictions');
select agro360.platform_enable_tenant_rls('agro360.livestock_sale_reservations');
select agro360.platform_enable_tenant_rls('agro360.livestock_cost_allocations');
select agro360.platform_enable_tenant_rls('agro360.livestock_pastures');
select agro360.platform_enable_tenant_rls('agro360.livestock_paddocks');
select agro360.platform_enable_tenant_rls('agro360.livestock_paddock_movements');
select agro360.platform_enable_tenant_rls('agro360.livestock_animal_movements');
select agro360.platform_enable_tenant_rls('agro360.livestock_handling_events');
select agro360.platform_enable_tenant_rls('agro360.livestock_health_events');
select agro360.platform_enable_tenant_rls('agro360.livestock_nutrition_plans');
select agro360.platform_enable_tenant_rls('agro360.livestock_feedings');

insert into agro360.platform_schema_versions(version, description, installed_at)
values ('6.8.0', 'Pecuária integrada: rebanho, manejos, pesagens, alimentação, reservas e custos', now())
on conflict (version) do update set description = excluded.description;

commit;

-- Dados demonstrativos da Fazenda Santa Clara. Reexecutável, sem senha/MFA e sem
-- apresentar parâmetros como orientação veterinária.
begin;
select set_config('app.tenant_id', '30000000-0000-0000-0000-000000000001', true);

insert into agro360.geo_farms (id, tenant_id, organization_id, name, state, municipality, total_area_ha, useful_area_ha, registration_number, created_by)
values
    ('30000000-0000-0000-0000-000000000010', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000002',
     'Fazenda Santa Clara - Sede', 'PA', 'Paragominas', 1200, 980, 'SC-SEDE-001', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-0000-0000-000000000013', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000002',
     'Retiro Boa Vista', 'PA', 'Paragominas', 420, 360, 'SC-RETIRO-001', '30000000-0000-0000-0000-000000000003')
on conflict (id) do update set name = excluded.name, deleted_at = null;

insert into agro360.livestock_species (id, tenant_id, code, name, created_by) values
    ('30000000-0000-4000-8000-000000000101', '30000000-0000-0000-0000-000000000001', 'BOVINE', 'Bovino', '30000000-0000-0000-0000-000000000003')
on conflict (tenant_id, code) do nothing;
insert into agro360.livestock_categories (id, tenant_id, species_code, code, name, sex, created_by) values
    ('30000000-0000-4000-8000-000000000102', '30000000-0000-0000-0000-000000000001', 'BOVINE', 'STEER', 'Boi magro', 'M', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000103', '30000000-0000-0000-0000-000000000001', 'BOVINE', 'COW', 'Matriz', 'F', '30000000-0000-0000-0000-000000000003')
on conflict (tenant_id, species_code, code) do nothing;
insert into agro360.livestock_breeds (id, tenant_id, species_code, code, name, created_by) values
    ('30000000-0000-4000-8000-000000000104', '30000000-0000-0000-0000-000000000001', 'BOVINE', 'NELORE', 'Nelore', '30000000-0000-0000-0000-000000000003')
on conflict (tenant_id, species_code, code) do nothing;

insert into agro360.livestock_movement_reasons (id, tenant_id, code, name, direction, created_by) values
    ('30000000-0000-4000-8000-000000000110', '30000000-0000-0000-0000-000000000001', 'PURCHASE', 'Compra e recebimento', 'ENTRY', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000111', '30000000-0000-0000-0000-000000000001', 'BIRTH', 'Nascimento', 'ENTRY', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000112', '30000000-0000-0000-0000-000000000001', 'INTERNAL_TRANSFER', 'Transferência interna', 'TRANSFER', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000113', '30000000-0000-0000-0000-000000000001', 'SALE', 'Venda e expedição', 'EXIT', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000114', '30000000-0000-0000-0000-000000000001', 'DEATH', 'Morte', 'EXIT', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000115', '30000000-0000-0000-0000-000000000001', 'DISCARD', 'Descarte justificado', 'EXIT', '30000000-0000-0000-0000-000000000003')
on conflict (tenant_id, code) do nothing;
insert into agro360.livestock_handling_types (id, tenant_id, code, name, created_by) values
    ('30000000-0000-4000-8000-000000000116', '30000000-0000-0000-0000-000000000001', 'WEIGHING', 'Pesagem', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000117', '30000000-0000-0000-0000-000000000001', 'VACCINATION', 'Vacinação cadastrada', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000118', '30000000-0000-0000-0000-000000000001', 'SORTING', 'Apartação', '30000000-0000-0000-0000-000000000003')
on conflict (tenant_id, code) do nothing;

insert into agro360.livestock_pastures (id, tenant_id, farm_id, name, area_hectares, forage_type, status, created_by)
values ('30000000-0000-4000-8000-000000000120', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000010',
        'Pasto Capim Mombaça', 48, 'Mombaça', 'IN_USE', '30000000-0000-0000-0000-000000000003')
on conflict (tenant_id, farm_id, name) do nothing;
insert into agro360.livestock_paddocks (id, tenant_id, pasture_id, name, area_hectares, capacity_au, status, rest_days, occupation_days, created_by)
values
    ('30000000-0000-4000-8000-000000000121', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000120',
     'Piquete 1', 12, 40, 'IN_USE', 28, 7, '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000122', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000120',
     'Piquete 2', 12, 40, 'RESTING', 28, 7, '30000000-0000-0000-0000-000000000003')
on conflict (tenant_id, pasture_id, name) do nothing;

insert into agro360.livestock_facilities (id, tenant_id, farm_id, name, kind, paddock_id, capacity_head, status, created_by)
values
    ('30000000-0000-4000-8000-000000000130', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000010',
     'Curral central', 'CORRAL', null, 80, 'AVAILABLE', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000131', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000010',
     'Piquete 1 - cocho', 'PADDOCK', '30000000-0000-4000-8000-000000000121', 40, 'IN_USE', '30000000-0000-0000-0000-000000000003')
on conflict (tenant_id, farm_id, name) do nothing;

insert into agro360.livestock_herds (id, tenant_id, farm_id, name, species, category, head_count, status, control_mode, facility_id, created_by, version)
values
    ('30000000-0000-4000-8000-000000000140', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000010',
     'Lote Nelore identificado', 'BOVINE', 'STEER', 0, 'ACTIVE', 'INDIVIDUAL', '30000000-0000-4000-8000-000000000131',
     '30000000-0000-0000-0000-000000000003', 1),
    ('30000000-0000-4000-8000-000000000141', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000010',
     'Lote coletivo de recria', 'BOVINE', 'STEER', 40, 'ACTIVE', 'QUANTITY', '30000000-0000-4000-8000-000000000131',
     '30000000-0000-0000-0000-000000000003', 1)
on conflict (id) do update set control_mode = excluded.control_mode, facility_id = excluded.facility_id, deleted_at = null;

insert into agro360.livestock_handling_lots (id, tenant_id, farm_id, name, purpose, status, created_by)
values ('30000000-0000-4000-8000-000000000142', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000010',
        'Lote de manejo 2026-09', 'Apartação e pesagem', 'OPEN', '30000000-0000-0000-0000-000000000003')
on conflict (tenant_id, farm_id, name) do nothing;

insert into agro360.livestock_animals (
    id, tenant_id, farm_id, herd_id, tag, species, breed, sex, birth_date, birth_date_estimated,
    status, category, paddock_id, facility_id, handling_lot_id, origin_type, origin_notes, notes,
    current_weight_kg, last_weight_date, created_at, created_by, version)
values
    ('30000000-0000-4000-8000-000000000150', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000010',
     '30000000-0000-4000-8000-000000000140', 'SC-N-1001', 'BOVINE', 'Nelore', 'M', '2024-03-12', false,
     1, 'STEER', '30000000-0000-4000-8000-000000000121', '30000000-0000-4000-8000-000000000131',
     '30000000-0000-4000-8000-000000000142', 'PURCHASE', 'Compra demonstrativa leilão fictício SC-2024', 'Animal demonstrativo Santa Clara',
     318.5, '2026-08-20', timestamptz '2024-08-01 12:00:00+00', '30000000-0000-0000-0000-000000000003', 1),
    ('30000000-0000-4000-8000-000000000151', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000010',
     '30000000-0000-4000-8000-000000000140', 'SC-N-1002', 'BOVINE', 'Nelore', 'M', '2024-04-02', true,
     1, 'STEER', '30000000-0000-4000-8000-000000000121', '30000000-0000-4000-8000-000000000131',
     '30000000-0000-4000-8000-000000000142', 'PURCHASE', 'Compra demonstrativa leilão fictício SC-2024', 'Data de nascimento estimada',
     302.0, '2026-08-20', timestamptz '2024-08-01 12:00:00+00', '30000000-0000-0000-0000-000000000003', 1),
    ('30000000-0000-4000-8000-000000000152', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000013',
     '30000000-0000-4000-8000-000000000140', 'SC-N-1003', 'BOVINE', 'Nelore', 'F', '2023-11-18', false,
     1, 'COW', null, '30000000-0000-4000-8000-000000000130',
     '30000000-0000-4000-8000-000000000142', 'PURCHASE', 'Transferida internamente para o Retiro Boa Vista', 'Matriz demonstrativa',
     412.0, '2026-08-20', timestamptz '2024-08-01 12:00:00+00', '30000000-0000-0000-0000-000000000003', 1),
    ('30000000-0000-4000-8000-000000000153', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000010',
     '30000000-0000-4000-8000-000000000140', 'SC-N-1004', 'BOVINE', 'Nelore', 'M', '2024-05-09', false,
     1, 'STEER', '30000000-0000-4000-8000-000000000121', '30000000-0000-4000-8000-000000000131',
     '30000000-0000-4000-8000-000000000142', 'PURCHASE', 'Compra demonstrativa leilão fictício SC-2024', 'Restrição operacional demonstrativa ativa',
     335.0, '2026-09-01', timestamptz '2024-08-01 12:00:00+00', '30000000-0000-0000-0000-000000000003', 1)
on conflict (id) do update set
    farm_id = excluded.farm_id, herd_id = excluded.herd_id, status = excluded.status,
    origin_type = excluded.origin_type, birth_date_estimated = excluded.birth_date_estimated,
    facility_id = excluded.facility_id, handling_lot_id = excluded.handling_lot_id, deleted_at = null;

update agro360.livestock_herds
set head_count = (select count(*) from agro360.livestock_animals a
                  where a.tenant_id = agro360.livestock_herds.tenant_id
                    and a.herd_id = agro360.livestock_herds.id
                    and a.status in (1, 2, 6)
                    and a.deleted_at is null)
where id = '30000000-0000-4000-8000-000000000140'
  and control_mode = 'INDIVIDUAL';

insert into agro360.livestock_animal_identifiers (id, tenant_id, animal_id, kind, value, assigned_on, created_by)
select gen_random_uuid(), tenant_id, id, 'TAG', tag, birth_date, created_by
from agro360.livestock_animals
where tenant_id = '30000000-0000-0000-0000-000000000001'
  and id in (
    '30000000-0000-4000-8000-000000000150', '30000000-0000-4000-8000-000000000151',
    '30000000-0000-4000-8000-000000000152', '30000000-0000-4000-8000-000000000153')
  and not exists (
    select 1 from agro360.livestock_animal_identifiers i
    where i.tenant_id = agro360.livestock_animals.tenant_id and i.animal_id = agro360.livestock_animals.id
      and i.kind = 'TAG' and i.retired_on is null);

insert into agro360.livestock_handling_lot_members (id, tenant_id, lot_id, animal_id, added_on, created_by)
select gen_random_uuid(), '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000142', id, '2026-08-01',
       '30000000-0000-0000-0000-000000000003'
from agro360.livestock_animals
where tenant_id = '30000000-0000-0000-0000-000000000001'
  and id in (
    '30000000-0000-4000-8000-000000000150', '30000000-0000-4000-8000-000000000151',
    '30000000-0000-4000-8000-000000000152', '30000000-0000-4000-8000-000000000153')
on conflict (tenant_id, lot_id, animal_id, added_on) do nothing;

insert into agro360.livestock_animal_events (id, tenant_id, animal_id, event_type, occurred_on, data, cost_amount, created_at, created_by)
values
    ('30000000-0000-4000-8000-000000000160', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000150',
     'ENTRY', '2024-08-01', '{"originType":"PURCHASE","reason":"PURCHASE","demo":true}'::jsonb, 4200, '2024-08-01 12:00:00+00',
     '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000161', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000151',
     'ENTRY', '2024-08-01', '{"originType":"PURCHASE","reason":"PURCHASE","demo":true}'::jsonb, 3900, '2024-08-01 12:00:00+00',
     '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000162', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000152',
     'ENTRY', '2024-08-01', '{"originType":"PURCHASE","reason":"PURCHASE","demo":true}'::jsonb, 5100, '2024-08-01 12:00:00+00',
     '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000163', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000153',
     'ENTRY', '2024-08-01', '{"originType":"PURCHASE","reason":"PURCHASE","demo":true}'::jsonb, 4400, '2024-08-01 12:00:00+00',
     '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.livestock_animal_movements (
    id, tenant_id, animal_id, from_farm_id, to_farm_id, from_paddock_id, to_paddock_id, moved_on, notes,
    movement_kind, reason_code, responsible_id, created_at, created_by)
values (
    '30000000-0000-4000-8000-000000000170', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000152',
    '30000000-0000-0000-0000-000000000010', '30000000-0000-0000-0000-000000000013',
    '30000000-0000-4000-8000-000000000121', null, '2026-07-15',
    'Transferência interna demonstrativa entre propriedades do mesmo cliente',
    'TRANSFER', 'INTERNAL_TRANSFER', '30000000-0000-0000-0000-000000000003',
    '2026-07-15 12:00:00+00', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.livestock_herd_movements (
    id, tenant_id, herd_id, movement_kind, quantity, occurred_on, reason_code, origin_notes, responsible_id, to_farm_id, created_by)
values (
    '30000000-0000-4000-8000-000000000171', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000141',
    'ENTRY', 40, '2024-09-10', 'PURCHASE', 'Entrada coletiva demonstrativa — controle por quantidade, sem indivíduos',
    '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000010', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.livestock_weighings (
    id, tenant_id, farm_id, scope, animal_id, herd_id, head_count, weighed_at, weight_kg, unit, method, source, responsible_id, notes, created_at, created_by)
values
    ('30000000-0000-4000-8000-000000000180', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000010',
     'INDIVIDUAL', '30000000-0000-4000-8000-000000000150', null, null, '2026-07-20 09:00:00+00', 305.0, 'kg', 'BALANCA', 'MANUAL',
     '30000000-0000-0000-0000-000000000003', 'Pesagem demonstrativa inicial', '2026-07-20 09:05:00+00', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000181', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000010',
     'INDIVIDUAL', '30000000-0000-4000-8000-000000000150', null, null, '2026-08-20 09:10:00+00', 318.5, 'kg', 'BALANCA', 'MANUAL',
     '30000000-0000-0000-0000-000000000003', 'Pesagem demonstrativa de acompanhamento', '2026-08-20 09:15:00+00', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000182', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000010',
     'COLLECTIVE', null, '30000000-0000-4000-8000-000000000141', 40, '2026-08-21 08:00:00+00', 12800, 'kg', 'BALANCA_COLETIVA', 'MANUAL',
     '30000000-0000-0000-0000-000000000003', 'Pesagem coletiva do lote de quantidade — não gera pesos individuais',
     '2026-08-21 08:05:00+00', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.livestock_weight_limits (id, tenant_id, species_code, min_kg, max_kg)
values ('30000000-0000-4000-8000-000000000183', '30000000-0000-0000-0000-000000000001', 'BOVINE', 40, 1200)
on conflict (tenant_id, species_code) do nothing;

insert into agro360.livestock_handling_orders (
    id, tenant_id, farm_id, handling_type, facility_id, planned_on, responsible_id, instructions, priority, status,
    planned_head_count, attended_head_count, not_attended_head_count, blocked_head_count, mass_atomic, notes, created_by)
values (
    '30000000-0000-4000-8000-000000000190', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000010',
    'SORTING', '30000000-0000-4000-8000-000000000130', '2026-09-02', '30000000-0000-0000-0000-000000000003',
    'Apartação demonstrativa. Processamento em massa aceita resultado parcial.', 'MEDIUM', 'IN_PROGRESS',
    4, 2, 1, 1, false, 'Ordem parcialmente executada de homologação', '30000000-0000-0000-0000-000000000003')
on conflict (id) do update set status = excluded.status, attended_head_count = excluded.attended_head_count,
    not_attended_head_count = excluded.not_attended_head_count, blocked_head_count = excluded.blocked_head_count;

insert into agro360.livestock_handling_order_items (id, tenant_id, order_id, animal_id, planned, outcome, outcome_reason)
values
    ('30000000-0000-4000-8000-000000000191', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000190',
     '30000000-0000-4000-8000-000000000150', true, 'ATTENDED', null),
    ('30000000-0000-4000-8000-000000000192', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000190',
     '30000000-0000-4000-8000-000000000151', true, 'ATTENDED', null),
    ('30000000-0000-4000-8000-000000000193', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000190',
     '30000000-0000-4000-8000-000000000152', true, 'NOT_ATTENDED', 'Não apresentado no curral'),
    ('30000000-0000-4000-8000-000000000194', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000190',
     '30000000-0000-4000-8000-000000000153', true, 'BLOCKED', 'Restrição operacional demonstrativa')
on conflict (id) do update set outcome = excluded.outcome, outcome_reason = excluded.outcome_reason;

insert into agro360.operations_operational_tasks (
    id, tenant_id, title, description, responsible_id, priority, due_at, module, entity_type, entity_id, status, created_by)
values (
    '30000000-0000-4000-8000-000000000195', '30000000-0000-0000-0000-000000000001',
    'Apartação lote de manejo 2026-09', 'Tarefa operacional vinculada à ordem de manejo pecuária.',
    '30000000-0000-0000-0000-000000000003', 'MEDIUM', timestamptz '2026-09-02 12:00:00+00',
    'LIVESTOCK', 'HANDLING_ORDER', '30000000-0000-4000-8000-000000000190', 'IN_PROGRESS',
    '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;
update agro360.livestock_handling_orders
set work_task_id = '30000000-0000-4000-8000-000000000195'
where id = '30000000-0000-4000-8000-000000000190';

insert into agro360.livestock_restrictions (
    id, tenant_id, animal_id, purpose, reason, started_on, expected_until, status, demo_only, created_by)
values (
    '30000000-0000-4000-8000-000000000200', '30000000-0000-0000-0000-000000000001',
    '30000000-0000-4000-8000-000000000153', 'SALE',
    'Restrição operacional demonstrativa de observação pós-recebimento. Não é protocolo veterinário.',
    '2026-08-25', '2026-09-25', 'ACTIVE', true, '30000000-0000-0000-0000-000000000003')
on conflict (id) do update set status = 'ACTIVE', demo_only = true;

insert into agro360.inventory_products (id, tenant_id, sku, name, category, base_unit, requires_lot, is_perishable, created_by)
values (
    '30000000-0000-4000-8000-000000000210', '30000000-0000-0000-0000-000000000001',
    'FEED-SC-001', 'Ração de recria (demonstrativa)', 'FEED', 'kg', true, false,
    '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;
insert into agro360.inventory_warehouses (id, tenant_id, farm_id, code, name, type, created_by)
values (
    '30000000-0000-4000-8000-000000000211', '30000000-0000-0000-0000-000000000001',
    '30000000-0000-0000-0000-000000000010', 'DEP-FEED-SC', 'Depósito de ração Santa Clara', 'FEED',
    '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;
insert into agro360.inventory_stock_lots (id, tenant_id, warehouse_id, product_id, lot_number, quantity, quality_status)
values (
    '30000000-0000-4000-8000-000000000212', '30000000-0000-0000-0000-000000000001',
    '30000000-0000-4000-8000-000000000211', '30000000-0000-4000-8000-000000000210', 'LOTE-FEED-SC-01', 1800, 'APPROVED')
on conflict (tenant_id, warehouse_id, product_id, lot_number) do nothing;
insert into agro360.inventory_stock_balances (id, tenant_id, warehouse_id, product_id, unit, available, reserved, minimum, average_cost)
values (
    '30000000-0000-4000-8000-000000000213', '30000000-0000-0000-0000-000000000001',
    '30000000-0000-4000-8000-000000000211', '30000000-0000-4000-8000-000000000210', 'kg', 1800, 0, 200, 1.85)
on conflict (id) do nothing;

insert into agro360.livestock_nutrition_plans (id, tenant_id, farm_id, herd_id, name, starts_on, ends_on, status, created_by)
values (
    '30000000-0000-4000-8000-000000000220', '30000000-0000-0000-0000-000000000001',
    '30000000-0000-0000-0000-000000000010', '30000000-0000-4000-8000-000000000141',
    'Plano alimentar recria (demonstrativo)', '2026-08-01', '2026-12-31', 'ACTIVE',
    '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;
insert into agro360.livestock_nutrition_plan_items (id, tenant_id, plan_id, product_id, quantity_per_day, unit, unit_cost)
select '30000000-0000-4000-8000-000000000221', '30000000-0000-0000-0000-000000000001',
       '30000000-0000-4000-8000-000000000220', '30000000-0000-4000-8000-000000000210', 8, 'kg', 1.85
where not exists (
    select 1 from agro360.livestock_nutrition_plan_items
    where id = '30000000-0000-4000-8000-000000000221');

insert into agro360.livestock_feedings (
    id, tenant_id, plan_id, warehouse_id, supplied_on, head_count, total_cost, cost_per_head, notes, created_by,
    planned_quantity, supplied_quantity, returned_quantity, lost_quantity, consumed_quantity, product_id, lot_number, unit, responsible_id)
values (
    '30000000-0000-4000-8000-000000000222', '30000000-0000-0000-0000-000000000001',
    '30000000-0000-4000-8000-000000000220', '30000000-0000-4000-8000-000000000211',
    '2026-09-01', 40, 592.00, 14.80, 'Fornecimento demonstrativo com consumo de estoque',
    '30000000-0000-0000-0000-000000000003', 320, 320, 0, 0, 320,
    '30000000-0000-4000-8000-000000000210', 'LOTE-FEED-SC-01', 'kg', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.inventory_stock_movements (
    id, tenant_id, warehouse_id, product_id, movement_type, quantity, unit, unit_cost, total_cost, lot_number,
    reference_type, reference_id, balance_after, average_cost_after, balance_version, occurred_at, created_by)
select '30000000-0000-4000-8000-000000000223', '30000000-0000-0000-0000-000000000001',
       '30000000-0000-4000-8000-000000000211', '30000000-0000-4000-8000-000000000210',
       'CONSUMPTION', 320, 'kg', 1.85, 592.00, 'LOTE-FEED-SC-01',
       'FEEDING', '30000000-0000-4000-8000-000000000222', 1480, 1.85, 1,
       timestamptz '2026-09-01 16:00:00+00', '30000000-0000-0000-0000-000000000003'
where not exists (select 1 from agro360.inventory_stock_movements where id = '30000000-0000-4000-8000-000000000223');
update agro360.inventory_stock_balances
set available = 1480, version = greatest(version, 1), updated_at = now()
where id = '30000000-0000-4000-8000-000000000213' and available >= 1480;
update agro360.inventory_stock_lots
set quantity = 1480
where id = '30000000-0000-4000-8000-000000000212' and quantity >= 1480;

insert into agro360.livestock_sale_reservations (
    id, tenant_id, farm_id, animal_id, quantity, status, reserved_on, buyer_name, notes, created_by)
values (
    '30000000-0000-4000-8000-000000000230', '30000000-0000-0000-0000-000000000001',
    '30000000-0000-0000-0000-000000000010', '30000000-0000-4000-8000-000000000150',
    1, 'ACTIVE', '2026-09-05', 'Frigorífico Demonstrativo Ltda',
    'Reserva comercial demonstrativa. Não representa venda concluída nem pagamento recebido.',
    '30000000-0000-0000-0000-000000000003')
on conflict (id) do update set status = 'ACTIVE', animal_id = excluded.animal_id;
update agro360.livestock_animals
set reservation_id = '30000000-0000-4000-8000-000000000230', status = 6
where id = '30000000-0000-4000-8000-000000000150';
update agro360.livestock_animals
set reservation_id = null, status = 1
where id = '30000000-0000-4000-8000-000000000153' and reservation_id = '30000000-0000-4000-8000-000000000230';

insert into agro360.cost_entries (
    id, tenant_id, farm_id, animal_id, source_type, source_id, category, amount, currency, occurred_on, created_at, created_by)
values
    ('30000000-0000-4000-8000-000000000240', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000010',
     '30000000-0000-4000-8000-000000000150', 'ANIMAL_EVENT', '30000000-0000-4000-8000-000000000160',
     'ANIMAL_ACQUISITION', 4200, 'BRL', '2024-08-01', '2024-08-01 12:00:00+00', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000241', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000010',
     null, 'FEEDING', '30000000-0000-4000-8000-000000000222',
     'ANIMAL_FEED', 592, 'BRL', '2026-09-01', '2026-09-01 16:00:00+00', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;
update agro360.cost_entries
set herd_id = '30000000-0000-4000-8000-000000000141'
where id = '30000000-0000-4000-8000-000000000241';

insert into agro360.livestock_cost_allocations (
    id, tenant_id, farm_id, animal_id, herd_id, source_type, source_id, category, nature, amount, occurred_on, basis, created_by)
values
    ('30000000-0000-4000-8000-000000000242', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000010',
     '30000000-0000-4000-8000-000000000150', '30000000-0000-4000-8000-000000000140',
     'ANIMAL_EVENT', '30000000-0000-4000-8000-000000000160', 'ACQUISITION', 'REALIZED', 4200, '2024-08-01',
     'Custo de aquisição do animal identificado', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000243', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000010',
     null, '30000000-0000-4000-8000-000000000141',
     'FEEDING', '30000000-0000-4000-8000-000000000222', 'FEEDING', 'REALIZED', 592, '2026-09-01',
     'Consumo de ração do lote coletivo (40 cabeças)', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

commit;
