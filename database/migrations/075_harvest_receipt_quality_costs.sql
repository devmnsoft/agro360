-- Jornada agrícola: planejamento -> colheita -> recebimento -> qualidade -> destinação/estoque.
begin;
create table if not exists agro360.harvest_plans (
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), farm_id uuid not null,
 season_id uuid not null, field_id uuid not null, product_id uuid not null, destination_warehouse_id uuid not null,
 cost_center_id uuid, responsible_id uuid, planned_start date not null, planned_end date not null,
 planned_area_ha numeric(18,4) not null, estimated_quantity numeric(20,6) not null, unit varchar(16) not null references agro360.platform_units(code),
 notes varchar(2000), status varchar(24) not null default 'PLANNED', idempotency_key varchar(160) not null, request_hash char(64) not null,
 created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz, updated_by uuid, version bigint not null default 1,
 unique(tenant_id,id), unique(tenant_id,idempotency_key),
 foreign key(tenant_id,farm_id) references agro360.geo_farms(tenant_id,id), foreign key(tenant_id,season_id) references agro360.agriculture_seasons(tenant_id,id),
 foreign key(tenant_id,field_id) references agro360.geo_fields(tenant_id,id), foreign key(tenant_id,product_id) references agro360.inventory_products(tenant_id,id),
 foreign key(tenant_id,destination_warehouse_id) references agro360.inventory_warehouses(tenant_id,id),
 check(planned_end>=planned_start), check(planned_area_ha>0), check(estimated_quantity>0), check(status in('PLANNED','IN_PROGRESS','COMPLETED','CANCELLED'))
);
create unique index if not exists ux_harvest_plan_accidental_duplicate on agro360.harvest_plans(tenant_id,season_id,field_id,product_id,planned_start,planned_end) where status<>'CANCELLED';

create table if not exists agro360.harvest_records (
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), plan_id uuid not null,
 operational_at timestamptz not null, harvested_quantity numeric(20,6) not null, unit varchar(16) not null references agro360.platform_units(code),
 harvested_area_ha numeric(18,4), commercial_reference varchar(100) not null, notes varchar(2000), status varchar(24) not null default 'AWAITING_RECEIPT',
 idempotency_key varchar(160) not null, request_hash char(64) not null, supersedes_id uuid, correction_reason varchar(1000), created_at timestamptz not null default now(), created_by uuid not null,
 updated_at timestamptz, updated_by uuid, version bigint not null default 1, unique(tenant_id,id), unique(tenant_id,idempotency_key), unique(tenant_id,commercial_reference),
 foreign key(tenant_id,plan_id) references agro360.harvest_plans(tenant_id,id), foreign key(tenant_id,supersedes_id) references agro360.harvest_records(tenant_id,id),
 check(harvested_quantity>0), check(harvested_area_ha is null or harvested_area_ha>0), check(status in('AWAITING_RECEIPT','PARTIALLY_RECEIVED','RECEIVED','CANCELLED')),
 check(supersedes_id is null or correction_reason is not null)
);

create table if not exists agro360.production_receipts (
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), harvest_record_id uuid not null, warehouse_id uuid not null,
 product_id uuid not null, received_at timestamptz not null, received_quantity numeric(20,6) not null, accepted_quantity numeric(20,6) not null default 0,
 commercial_quantity numeric(20,6), unit varchar(16) not null references agro360.platform_units(code), gross_weight numeric(20,6), tare_weight numeric(20,6), net_weight numeric(20,6),
 lot_number varchar(100) not null, entry_mode varchar(16) not null, divergence_reason varchar(1000), notes varchar(2000), quality_status varchar(30) not null default 'AWAITING_INSPECTION',
 idempotency_key varchar(160) not null, request_hash char(64) not null, created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz, updated_by uuid, version bigint not null default 1,
 unique(tenant_id,id), unique(tenant_id,idempotency_key), unique(tenant_id,warehouse_id,product_id,lot_number),
 foreign key(tenant_id,harvest_record_id) references agro360.harvest_records(tenant_id,id), foreign key(tenant_id,warehouse_id) references agro360.inventory_warehouses(tenant_id,id),
 foreign key(tenant_id,product_id) references agro360.inventory_products(tenant_id,id), check(received_quantity>0), check(accepted_quantity>=0 and accepted_quantity<=received_quantity),
 check(gross_weight is null or tare_weight is null or gross_weight>=tare_weight), check(net_weight is null or net_weight>=0), check(entry_mode in('MANUAL','SCALE')),
 check(quality_status in('AWAITING_INSPECTION','APPROVED','BLOCKED','QUARANTINE','REJECTED','PARTIALLY_ALLOCATED','ALLOCATED'))
);
alter table agro360.quality_inspections add column if not exists production_receipt_id uuid;
alter table agro360.quality_inspections add column if not exists specification_version int;
create unique index if not exists ux_quality_inspection_receipt on agro360.quality_inspections(tenant_id,production_receipt_id) where production_receipt_id is not null and deleted_at is null;
create table if not exists agro360.harvest_inspection_requests(
 tenant_id uuid not null references agro360.tenancy_tenants(id), idempotency_key varchar(160) not null, request_hash char(64) not null,
 inspection_id uuid not null, created_at timestamptz not null default now(), created_by uuid not null,
 primary key(tenant_id,idempotency_key), foreign key(tenant_id,inspection_id) references agro360.quality_inspections(tenant_id,id)
);

create table if not exists agro360.harvest_material_allocations (
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), receipt_id uuid not null,
 quantity numeric(20,6) not null, destination varchar(24) not null, reason varchar(1000) not null, stock_movement_id uuid,
 idempotency_key varchar(160) not null, request_hash char(64) not null, created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,idempotency_key), foreign key(tenant_id,receipt_id) references agro360.production_receipts(tenant_id,id),
 foreign key(tenant_id,stock_movement_id) references agro360.inventory_stock_movements(tenant_id,id), check(quantity>0),
 check(destination in('AVAILABLE','QUARANTINE','RECLASSIFICATION','REPROCESSING','RETURN_TO_ORIGIN','LOSS','DISPOSAL'))
);
create index if not exists ix_harvest_records_balance on agro360.harvest_records(tenant_id,plan_id,status);
create index if not exists ix_production_receipts_quality on agro360.production_receipts(tenant_id,quality_status,received_at);
create index if not exists ix_harvest_allocations_receipt on agro360.harvest_material_allocations(tenant_id,receipt_id);

select agro360.platform_enable_tenant_rls('agro360.harvest_plans'); select agro360.platform_enable_tenant_rls('agro360.harvest_records');
select agro360.platform_enable_tenant_rls('agro360.production_receipts'); select agro360.platform_enable_tenant_rls('agro360.harvest_material_allocations');
select agro360.platform_enable_tenant_rls('agro360.harvest_inspection_requests');
insert into agro360.platform_schema_versions(version,description,installed_at)
values('7.5.0','Colheita, recebimento, qualidade, destinação e custos da safra',now()) on conflict(version) do nothing;
commit;
