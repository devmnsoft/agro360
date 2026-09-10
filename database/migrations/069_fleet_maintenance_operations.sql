begin;

-- Frota operacional: cadastro, medidores, preventiva, OS, disponibilidade,
-- peças, abastecimento interno/externo e custos. Idempotente.

alter table agro360.fleet_asset_types
    add column if not exists kind varchar(20) not null default 'MACHINE';
do $$ begin
    alter table agro360.fleet_asset_types
        add constraint ck_fleet_asset_type_kind check (kind in ('MACHINE','VEHICLE','IMPLEMENT','STATIONARY'));
exception when duplicate_object then null;
end $$;

alter table agro360.fleet_assets
    add column if not exists cadastral_status varchar(20) not null default 'ACTIVE',
    add column if not exists ownership varchar(20) not null default 'OWNED',
    add column if not exists energy_source varchar(40),
    add column if not exists commissioned_on date,
    add column if not exists deleted_at timestamptz,
    add column if not exists deleted_by uuid,
    add column if not exists farm_id uuid,
    add column if not exists code varchar(60),
    add column if not exists asset_type varchar(60);
do $$ begin
    alter table agro360.fleet_assets
        add constraint ck_fleet_cadastral_status check (cadastral_status in ('ACTIVE','INACTIVE','WRITTEN_OFF','SOLD'));
exception when duplicate_object then null;
end $$;
do $$ begin
    alter table agro360.fleet_assets
        add constraint ck_fleet_ownership check (ownership in ('OWNED','LEASED','RENTED','THIRD_PARTY'));
exception when duplicate_object then null;
end $$;
update agro360.fleet_assets set code = coalesce(code, internal_code) where code is null and internal_code is not null;
update agro360.fleet_assets set internal_code = coalesce(internal_code, code) where internal_code is null and code is not null;

alter table agro360.fleet_work_orders drop constraint if exists fleet_work_orders_status_check;
alter table agro360.fleet_work_orders
    add column if not exists blocks_asset boolean not null default false,
    add column if not exists estimated_cost numeric(18,2) not null default 0,
    add column if not exists meter_kind varchar(20),
    add column if not exists meter_reading numeric(16,2),
    add column if not exists plan_version int,
    add column if not exists supplier_id uuid,
    add column if not exists inspection_result varchar(20),
    add column if not exists work_task_id uuid,
    add column if not exists idempotency_key varchar(160);
alter table agro360.fleet_work_orders
    add constraint fleet_work_orders_status_check
    check (status in ('OPEN','PLANNED','PENDING_APPROVAL','IN_PROGRESS','PAUSED','WAITING_PART','WAITING_VENDOR','INSPECTION','COMPLETED','CANCELLED','REOPENED'));
create unique index if not exists ux_fleet_work_orders_idempotency
    on agro360.fleet_work_orders (tenant_id, idempotency_key)
    where idempotency_key is not null;

alter table agro360.fleet_maintenance_plans
    add column if not exists due_policy varchar(20) not null default 'FIRST_CRITERION',
    add column if not exists date_interval_days integer,
    add column if not exists hour_interval numeric(16,2),
    add column if not exists km_interval numeric(16,2),
    add column if not exists version_no integer not null default 1,
    add column if not exists category_kind varchar(20),
    add column if not exists advance_mode varchar(20) not null default 'FROM_EXECUTION';
do $$ begin
    alter table agro360.fleet_maintenance_plans
        add constraint ck_fleet_plan_due_policy check (due_policy in ('FIRST_CRITERION','CALENDAR_FIXED','METER_FIXED'));
exception when duplicate_object then null;
end $$;

alter table agro360.fleet_refuelings
    add column if not exists source varchar(16) not null default 'EXTERNAL',
    add column if not exists warehouse_id uuid,
    add column if not exists product_id uuid,
    add column if not exists lot_number varchar(100),
    add column if not exists tank_full boolean not null default false,
    add column if not exists idempotency_key varchar(160),
    add column if not exists stock_movement_id uuid;
do $$ begin
    alter table agro360.fleet_refuelings
        add constraint ck_fleet_refuel_source check (source in ('INTERNAL','EXTERNAL'));
exception when duplicate_object then null;
end $$;
create unique index if not exists ux_fleet_refuelings_idempotency
    on agro360.fleet_refuelings (tenant_id, idempotency_key)
    where idempotency_key is not null;

alter table agro360.fleet_work_order_parts
    add column if not exists warehouse_id uuid,
    add column if not exists reserved_quantity numeric(14,3) not null default 0,
    add column if not exists delivered_quantity numeric(14,3) not null default 0,
    add column if not exists consumed_quantity numeric(14,3) not null default 0,
    add column if not exists returned_quantity numeric(14,3) not null default 0,
    add column if not exists lost_quantity numeric(14,3) not null default 0,
    add column if not exists unit varchar(16),
    add column if not exists reusable boolean,
    add column if not exists idempotency_key varchar(160);

create table if not exists agro360.fleet_asset_meters (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    asset_id uuid not null,
    meter_kind varchar(20) not null check (meter_kind in ('ODOMETER','HOUR_METER','ENGINE_HOURS')),
    unit varchar(16) not null,
    enabled boolean not null default true,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique (tenant_id, asset_id, meter_kind),
    foreign key (tenant_id, asset_id) references agro360.fleet_assets(tenant_id, id)
);

create table if not exists agro360.fleet_meter_readings (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    asset_id uuid not null,
    meter_kind varchar(20) not null,
    occurred_at timestamptz not null,
    physical_value numeric(16,2) not null check (physical_value >= 0),
    operational_accumulated numeric(16,2) not null check (operational_accumulated >= 0),
    unit varchar(16) not null,
    origin varchar(20) not null default 'MANUAL' check (origin in ('MANUAL','IMPORT','WORK_ORDER','REFUELING','RESET')),
    is_reset boolean not null default false,
    responsible_id uuid,
    justification text,
    evidence_document_id uuid,
    corrected_from_id uuid,
    idempotency_key varchar(160),
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique (tenant_id, id),
    foreign key (tenant_id, asset_id) references agro360.fleet_assets(tenant_id, id)
);
create unique index if not exists ux_fleet_readings_idempotency
    on agro360.fleet_meter_readings (tenant_id, idempotency_key)
    where idempotency_key is not null;
create index if not exists ix_fleet_readings_timeline
    on agro360.fleet_meter_readings (tenant_id, asset_id, meter_kind, occurred_at desc);

create table if not exists agro360.fleet_operational_blocks (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    asset_id uuid not null,
    kind varchar(30) not null check (kind in ('MAINTENANCE','INSPECTION','RESERVATION','SAFETY','ADMIN')),
    reason varchar(500) not null,
    dispensable boolean not null default false,
    work_order_id uuid,
    started_at timestamptz not null default now(),
    ended_at timestamptz,
    status varchar(16) not null default 'ACTIVE' check (status in ('ACTIVE','RELEASED','EXPIRED')),
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique (tenant_id, id),
    foreign key (tenant_id, asset_id) references agro360.fleet_assets(tenant_id, id)
);
create index if not exists ix_fleet_blocks_active
    on agro360.fleet_operational_blocks (tenant_id, asset_id)
    where status = 'ACTIVE';

create table if not exists agro360.fleet_asset_reservations (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    asset_id uuid not null,
    purpose varchar(40) not null check (purpose in ('AGRICULTURE','PRODUCTION','LOGISTICS','MAINTENANCE','OTHER')),
    starts_at timestamptz not null,
    ends_at timestamptz not null,
    reference_type varchar(40),
    reference_id uuid,
    status varchar(16) not null default 'ACTIVE' check (status in ('ACTIVE','CANCELLED','CONSUMED')),
    notes text,
    idempotency_key varchar(160),
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique (tenant_id, id),
    check (ends_at > starts_at),
    foreign key (tenant_id, asset_id) references agro360.fleet_assets(tenant_id, id)
);
create unique index if not exists ux_fleet_reservations_idempotency
    on agro360.fleet_asset_reservations (tenant_id, idempotency_key)
    where idempotency_key is not null;
create index if not exists ix_fleet_reservations_window
    on agro360.fleet_asset_reservations (tenant_id, asset_id, starts_at, ends_at)
    where status = 'ACTIVE';

create table if not exists agro360.fleet_work_order_inspections (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    work_order_id uuid not null,
    result varchar(20) not null check (result in ('APPROVED','REJECTED','PENDING')),
    checklist jsonb not null default '[]',
    blocking_failures int not null default 0,
    inspector_id uuid,
    notes text,
    occurred_at timestamptz not null default now(),
    created_at timestamptz not null default now(),
    created_by uuid not null,
    foreign key (tenant_id, work_order_id) references agro360.fleet_work_orders(tenant_id, id)
);

create table if not exists agro360.fleet_work_order_time_logs (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    work_order_id uuid not null,
    technician_id uuid not null,
    started_at timestamptz not null,
    ended_at timestamptz,
    minutes_effective integer,
    notes text,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    check (ended_at is null or ended_at >= started_at),
    foreign key (tenant_id, work_order_id) references agro360.fleet_work_orders(tenant_id, id)
);

create table if not exists agro360.fleet_removed_parts (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    work_order_id uuid not null,
    product_id uuid,
    description varchar(200) not null,
    quantity numeric(14,3) not null check (quantity > 0),
    condition varchar(20) not null check (condition in ('REUSABLE','REPAIR','SCRAP')),
    created_at timestamptz not null default now(),
    created_by uuid not null,
    foreign key (tenant_id, work_order_id) references agro360.fleet_work_orders(tenant_id, id)
);

select agro360.platform_enable_tenant_rls('agro360.fleet_asset_meters');
select agro360.platform_enable_tenant_rls('agro360.fleet_meter_readings');
select agro360.platform_enable_tenant_rls('agro360.fleet_operational_blocks');
select agro360.platform_enable_tenant_rls('agro360.fleet_asset_reservations');
select agro360.platform_enable_tenant_rls('agro360.fleet_work_order_inspections');
select agro360.platform_enable_tenant_rls('agro360.fleet_work_order_time_logs');
select agro360.platform_enable_tenant_rls('agro360.fleet_removed_parts');
select agro360.platform_enable_tenant_rls('agro360.fleet_assets');
select agro360.platform_enable_tenant_rls('agro360.fleet_work_orders');
select agro360.platform_enable_tenant_rls('agro360.fleet_refuelings');
select agro360.platform_enable_tenant_rls('agro360.fleet_maintenance_plans');

insert into agro360.platform_schema_versions(version, description, installed_at)
values ('6.9.0', 'Frota: medidores, preventiva, OS, disponibilidade, peças e abastecimento', now())
on conflict (version) do update set description = excluded.description;

commit;

-- Dados demonstrativos Santa Clara. Reexecutável, sem senha/MFA.
begin;
select set_config('app.tenant_id', '30000000-0000-0000-0000-000000000001', true);

insert into agro360.fleet_asset_types (id, tenant_id, name, kind, status, created_by, updated_by)
values
    ('30000000-0000-4000-8000-000000000301', '30000000-0000-0000-0000-000000000001', 'Trator', 'MACHINE', 'ACTIVE', '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000302', '30000000-0000-0000-0000-000000000001', 'Implemento', 'IMPLEMENT', 'ACTIVE', '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003')
on conflict (tenant_id, name) do update set kind = excluded.kind, status = 'ACTIVE', deleted_at = null;

insert into agro360.fleet_fuel_types (id, tenant_id, name, unit, status, created_by, updated_by)
values ('30000000-0000-4000-8000-000000000303', '30000000-0000-0000-0000-000000000001', 'Diesel S10 (demonstrativo)', 'L', 'ACTIVE', '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003')
on conflict (tenant_id, name) do nothing;

insert into agro360.fleet_operators (id, tenant_id, name, employment_type, role, status, created_by, updated_by)
values ('30000000-0000-4000-8000-000000000304', '30000000-0000-0000-0000-000000000001', 'Operador demonstrativo Santa Clara', 'CLT', 'OPERADOR', 'ACTIVE', '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003')
on conflict (id) do update set status = 'ACTIVE', deleted_at = null;

insert into agro360.inventory_products (id, tenant_id, sku, name, category, base_unit, requires_lot, is_perishable, created_by)
values
    ('30000000-0000-4000-8000-000000000305', '30000000-0000-0000-0000-000000000001', 'FUEL-SC-DSL', 'Diesel S10 demonstrativo', 'FUEL', 'l', true, false, '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000306', '30000000-0000-0000-0000-000000000001', 'PART-SC-FIL', 'Filtro de óleo demonstrativo', 'PARTS', 'unit', true, false, '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.inventory_warehouses (id, tenant_id, farm_id, code, name, type, created_by)
values ('30000000-0000-4000-8000-000000000307', '30000000-0000-0000-0000-000000000001', '30000000-0000-0000-0000-000000000010', 'DEP-FUEL-SC', 'Depósito de combustível Santa Clara', 'FUEL', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.inventory_stock_balances (id, tenant_id, warehouse_id, product_id, unit, available, reserved, minimum, average_cost)
values
    ('30000000-0000-4000-8000-000000000308', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000307', '30000000-0000-4000-8000-000000000305', 'l', 800, 0, 80, 6.10),
    ('30000000-0000-4000-8000-000000000309', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000307', '30000000-0000-4000-8000-000000000306', 'unit', 4, 1, 1, 85.00)
on conflict (id) do nothing;

insert into agro360.fleet_assets (
    id, tenant_id, internal_code, code, name, asset_type_id, asset_type, status, cadastral_status, ownership,
    brand, model, year, plate, serial_number, property_id, farm_id, odometer, hour_meter, fuel_capacity,
    energy_source, main_operator_id, acquired_on, commissioned_on, notes, created_by, updated_by)
values
    ('30000000-0000-4000-8000-000000000310', '30000000-0000-0000-0000-000000000001', 'SC-TR-01', 'SC-TR-01',
     'Trator demonstrativo disponível', '30000000-0000-4000-8000-000000000301', 'Trator', 'AVAILABLE', 'ACTIVE', 'OWNED',
     'Valtra', 'A750', 2022, 'SCD1A23', 'TR-SC-750-01', '30000000-0000-0000-0000-000000000010', '30000000-0000-0000-0000-000000000010',
     18420, 3120, 280, 'DIESEL', '30000000-0000-4000-8000-000000000304', '2022-06-01', '2022-06-15',
     'Equipamento demonstrativo disponível para operação agrícola.', '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000311', '30000000-0000-0000-0000-000000000001', 'SC-IMP-01', 'SC-IMP-01',
     'Grade demonstrativa em manutenção', '30000000-0000-4000-8000-000000000302', 'Implemento', 'MAINTENANCE', 'ACTIVE', 'OWNED',
     'Baldan', 'GTA 32', 2019, null, 'IMP-SC-32-01', '30000000-0000-0000-0000-000000000010', '30000000-0000-0000-0000-000000000010',
     0, 980, null, null, '30000000-0000-4000-8000-000000000304', '2019-11-10', '2019-12-01',
     'Implemento sem placa. Em manutenção demonstrativa.', '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003')
on conflict (id) do update set
    status = excluded.status, cadastral_status = 'ACTIVE', notes = excluded.notes, deleted_at = null;

insert into agro360.fleet_asset_meters (id, tenant_id, asset_id, meter_kind, unit, enabled, created_by)
values
    ('30000000-0000-4000-8000-000000000320', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000310', 'ODOMETER', 'km', true, '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000321', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000310', 'HOUR_METER', 'h', true, '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000322', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000311', 'HOUR_METER', 'h', true, '30000000-0000-0000-0000-000000000003')
on conflict (tenant_id, asset_id, meter_kind) do nothing;

insert into agro360.fleet_meter_readings (id, tenant_id, asset_id, meter_kind, occurred_at, physical_value, operational_accumulated, unit, origin, is_reset, responsible_id, created_by)
values
    ('30000000-0000-4000-8000-000000000330', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000310', 'HOUR_METER', '2026-07-01 10:00:00+00', 3000, 3000, 'h', 'MANUAL', false, '30000000-0000-4000-8000-000000000304', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000331', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000310', 'HOUR_METER', '2026-08-15 10:00:00+00', 3080, 3080, 'h', 'MANUAL', false, '30000000-0000-4000-8000-000000000304', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000332', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000310', 'HOUR_METER', '2026-09-05 09:00:00+00', 3120, 3120, 'h', 'MANUAL', false, '30000000-0000-4000-8000-000000000304', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.fleet_maintenance_plans (
    id, tenant_id, asset_id, maintenance_type, description, periodicity, control_unit, next_execution_at, next_meter,
    status, responsible_id, estimated_cost, due_policy, hour_interval, date_interval_days, version_no, created_by, updated_by)
values (
    '30000000-0000-4000-8000-000000000340', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000310',
    'REVISAO_250H', 'Revisão preventiva 250 h — vence no primeiro critério (horas ou 90 dias).', 250, 'HOUR_METER',
    timestamptz '2026-09-20 12:00:00+00', 3250, 'ACTIVE', '30000000-0000-4000-8000-000000000304', 1800,
    'FIRST_CRITERION', 250, 90, 1, '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003')
on conflict (id) do update set status = 'ACTIVE', next_execution_at = excluded.next_execution_at, next_meter = excluded.next_meter;

insert into agro360.fleet_maintenance_plan_items (id, tenant_id, plan_id, sequence, description, part_id, planned_quantity, created_by, updated_by)
values ('30000000-0000-4000-8000-000000000341', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000340', 1,
        'Troca de filtro de óleo', '30000000-0000-4000-8000-000000000306', 1, '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.fleet_maintenance_requests (id, tenant_id, asset_id, defect_class, severity, problem_description, status, blocks_asset, created_by, updated_by)
values ('30000000-0000-4000-8000-000000000350', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000311',
        'HYDRAULIC', 'HIGH', 'Vazamento hidráulico demonstrativo na grade. Solicitação corretiva.', 'OPEN', true,
        '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003')
on conflict (id) do update set status = 'OPEN', blocks_asset = true;

insert into agro360.fleet_work_orders (
    id, tenant_id, code, asset_id, maintenance_request_id, type, priority, responsible_id, description, status,
    blocks_asset, estimated_cost, opened_at, due_at, created_by, updated_by)
values
    ('30000000-0000-4000-8000-000000000360', '30000000-0000-0000-0000-000000000001', 'OS-SC-0001',
     '30000000-0000-4000-8000-000000000311', '30000000-0000-4000-8000-000000000350', 'CORRECTIVE', 'HIGH',
     '30000000-0000-4000-8000-000000000304', 'OS corretiva demonstrativa aguardando peça (filtro).', 'WAITING_PART',
     true, 420, now() - interval '2 days', now() + interval '3 days',
     '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000361', '30000000-0000-0000-0000-000000000001', 'OS-SC-0002',
     '30000000-0000-4000-8000-000000000311', null, 'CORRECTIVE', 'MEDIUM',
     '30000000-0000-4000-8000-000000000304', 'OS demonstrativa com consumo apontado e inspeção pendente.', 'INSPECTION',
     true, 280, now() - interval '1 day', now() + interval '1 day',
     '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003')
on conflict (id) do update set status = excluded.status, blocks_asset = true, deleted_at = null;

insert into agro360.fleet_work_order_parts (id, tenant_id, work_order_id, product_id, description, quantity, unit_cost, warehouse_id, reserved_quantity, unit, created_by, updated_by)
values ('30000000-0000-4000-8000-000000000370', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000360',
        '30000000-0000-4000-8000-000000000306', 'Filtro de óleo demonstrativo', 1, 85, '30000000-0000-4000-8000-000000000307', 1, 'unit',
        '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.fleet_work_order_parts (id, tenant_id, work_order_id, product_id, description, quantity, unit_cost, warehouse_id, consumed_quantity, unit, created_by, updated_by)
values ('30000000-0000-4000-8000-000000000371', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000361',
        '30000000-0000-4000-8000-000000000306', 'Filtro consumido na OS de inspeção', 1, 85, '30000000-0000-4000-8000-000000000307', 1, 'unit',
        '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.fleet_work_order_inspections (id, tenant_id, work_order_id, result, blocking_failures, inspector_id, notes, created_by)
values ('30000000-0000-4000-8000-000000000372', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000361',
        'PENDING', 0, '30000000-0000-0000-0000-000000000003', 'Inspeção pendente demonstrativa. Serviço executado não libera o ativo.',
        '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.fleet_operational_blocks (id, tenant_id, asset_id, kind, reason, dispensable, work_order_id, status, created_by)
values ('30000000-0000-4000-8000-000000000380', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000311',
        'MAINTENANCE', 'Bloqueio por OS corretiva demonstrativa. Não dispensável.', false,
        '30000000-0000-4000-8000-000000000360', 'ACTIVE', '30000000-0000-0000-0000-000000000003')
on conflict (id) do update set status = 'ACTIVE';

insert into agro360.fleet_asset_reservations (id, tenant_id, asset_id, purpose, starts_at, ends_at, reference_type, status, notes, created_by)
values ('30000000-0000-4000-8000-000000000381', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000311',
        'AGRICULTURE', now() - interval '1 day', now() + interval '2 days', 'FIELD_OPERATION', 'ACTIVE',
        'Reserva agrícola demonstrativa afetada pela indisponibilidade da grade.', '30000000-0000-0000-0000-000000000003')
on conflict (id) do update set status = 'ACTIVE';

insert into agro360.fleet_refuelings (
    id, tenant_id, asset_id, operator_id, fuel_type_id, quantity, unit_price, total_value, occurred_at,
    hour_meter, source, warehouse_id, product_id, tank_full, status, notes, created_by, updated_by)
values
    ('30000000-0000-4000-8000-000000000390', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000310',
     '30000000-0000-4000-8000-000000000304', '30000000-0000-4000-8000-000000000303', 120, 6.10, 732.00, '2026-09-04 16:00:00+00',
     3110, 'INTERNAL', '30000000-0000-4000-8000-000000000307', '30000000-0000-4000-8000-000000000305', true, 'ACTIVE',
     'Abastecimento interno demonstrativo — baixa estoque uma vez.', '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000391', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000310',
     '30000000-0000-4000-8000-000000000304', '30000000-0000-4000-8000-000000000303', 80, 6.45, 516.00, '2026-09-08 11:00:00+00',
     3120, 'EXTERNAL', null, null, false, 'ACTIVE',
     'Abastecimento externo demonstrativo — não baixa estoque interno.', '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003')
on conflict (id) do nothing;

insert into agro360.inventory_stock_movements (
    id, tenant_id, warehouse_id, product_id, movement_type, quantity, unit, unit_cost, total_cost, lot_number,
    reference_type, reference_id, balance_after, average_cost_after, balance_version, occurred_at, created_by)
select '30000000-0000-4000-8000-000000000392', '30000000-0000-0000-0000-000000000001',
       '30000000-0000-4000-8000-000000000307', '30000000-0000-4000-8000-000000000305',
       'CONSUMPTION', 120, 'l', 6.10, 732.00, 'LOTE-DSL-SC-01',
       'REFUELING', '30000000-0000-4000-8000-000000000390', 680, 6.10, 1,
       timestamptz '2026-09-04 16:00:00+00', '30000000-0000-0000-0000-000000000003'
where not exists (select 1 from agro360.inventory_stock_movements where id = '30000000-0000-4000-8000-000000000392');
update agro360.inventory_stock_balances set available = 680, version = greatest(version, 1), updated_at = now()
where id = '30000000-0000-4000-8000-000000000308' and available >= 680;

insert into agro360.fleet_operational_costs (id, tenant_id, asset_id, cost_type, value, occurred_on, origin_type, origin_id, status, created_by, updated_by)
values
    ('30000000-0000-4000-8000-000000000393', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000310',
     'FUEL', 732.00, '2026-09-04', 'REFUELING', '30000000-0000-4000-8000-000000000390', 'ACTIVE',
     '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003'),
    ('30000000-0000-4000-8000-000000000394', '30000000-0000-0000-0000-000000000001', '30000000-0000-4000-8000-000000000310',
     'FUEL', 516.00, '2026-09-08', 'REFUELING', '30000000-0000-4000-8000-000000000391', 'ACTIVE',
     '30000000-0000-0000-0000-000000000003', '30000000-0000-0000-0000-000000000003')
on conflict (tenant_id, origin_type, origin_id) do nothing;

insert into agro360.operations_operational_alerts (id, tenant_id, dedup_key, title, description, severity, module, origin_type, origin_id, created_by)
values ('30000000-0000-4000-8000-000000000395', '30000000-0000-0000-0000-000000000001',
        'FLEET-PLAN:30000000-0000-4000-8000-000000000340',
        'Preventiva próxima do vencimento',
        'Revisão 250 h do trator SC-TR-01 vence pelo primeiro critério (horas ou data). Não há leitura suficiente? A previsão de horímetro exige leitura válida.',
        'ATTENTION', 'FLEET', 'MAINTENANCE_PLAN', '30000000-0000-4000-8000-000000000340', '30000000-0000-0000-0000-000000000003')
on conflict do nothing;

commit;
