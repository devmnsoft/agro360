begin;

-- Extends the existing agriculture_records work-order aggregate.  Reservations,
-- execution and material custody are separate facts so a reservation is never
-- mistaken for consumption.
create sequence if not exists agro360.field_work_order_number_seq;
alter table agro360.agriculture_records drop constraint if exists agriculture_records_status_check;
alter table agro360.agriculture_records add constraint agriculture_records_status_check check(status in('OPEN','PLANNED','AWAITING_RESOURCES','RELEASED','IN_PROGRESS','PAUSED','AWAITING_REVIEW','COMPLETED','CANCELLED','APPROVED','REVISION','CLOSED'));

create table agro360.field_work_order_resources (
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id),
 work_order_id uuid not null references agro360.agriculture_records(id), resource_type varchar(16) not null,
 resource_id uuid not null, starts_at timestamptz not null, ends_at timestamptz not null,
 status varchar(20) not null default 'RESERVED', version bigint not null default 1,
 created_at timestamptz not null default now(), created_by uuid not null,
 updated_at timestamptz not null default now(), updated_by uuid not null,
 deleted_at timestamptz, deleted_by uuid, unique(tenant_id,id),
 foreign key(tenant_id,work_order_id) references agro360.agriculture_records(tenant_id,id),
 check(resource_type in ('PERSON','EQUIPMENT')), check(ends_at>starts_at),
 check(status in ('RESERVED','RELEASED','CANCELLED'))
);
create index ix_field_resource_overlap on agro360.field_work_order_resources(tenant_id,resource_type,resource_id,starts_at,ends_at) where deleted_at is null and status='RESERVED';

create table agro360.field_work_logs (
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id),
 work_order_id uuid not null, operator_id uuid not null, equipment_id uuid, stage varchar(100) not null,
 starts_at timestamptz not null, ends_at timestamptz not null, performed_quantity numeric(20,6) not null,
 unit varchar(20) not null, physical_area_ha numeric(14,4), initial_meter numeric(20,3), final_meter numeric(20,3),
 interruption_minutes integer not null default 0, interruption_reason varchar(500), notes varchar(2000),
 evidence_reference varchar(500), idempotency_key varchar(100) not null, confirmed_at timestamptz not null default now(),
 version bigint not null default 1, created_at timestamptz not null default now(), created_by uuid not null,
 updated_at timestamptz not null default now(), updated_by uuid not null, deleted_at timestamptz, deleted_by uuid,
 unique(tenant_id,id), unique(tenant_id,idempotency_key),
 foreign key(tenant_id,work_order_id) references agro360.agriculture_records(tenant_id,id),
 check(ends_at>starts_at), check(performed_quantity>=0), check(physical_area_ha is null or physical_area_ha>=0),
 check(initial_meter is null or initial_meter>=0), check(final_meter is null or final_meter>=initial_meter),
 check(interruption_minutes>=0), check(length(trim(stage))>=2)
);
create index ix_field_log_order on agro360.field_work_logs(tenant_id,work_order_id,starts_at) where deleted_at is null;
create index ix_field_log_resource on agro360.field_work_logs(tenant_id,equipment_id,starts_at,ends_at) where equipment_id is not null and deleted_at is null;

create table agro360.field_work_order_materials (
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), work_order_id uuid not null,
 product_id uuid not null, warehouse_id uuid, unit varchar(20) not null, planned_quantity numeric(20,6) not null,
 reserved_quantity numeric(20,6) not null default 0, delivered_quantity numeric(20,6) not null default 0,
 consumed_quantity numeric(20,6) not null default 0, returned_quantity numeric(20,6) not null default 0,
 lost_quantity numeric(20,6) not null default 0, unit_cost numeric(18,6), status varchar(20) not null default 'PLANNED',
 version bigint not null default 1, created_at timestamptz not null default now(), created_by uuid not null,
 updated_at timestamptz not null default now(), updated_by uuid not null, deleted_at timestamptz, deleted_by uuid,
 unique(tenant_id,id), unique(tenant_id,work_order_id,product_id,warehouse_id),
 foreign key(tenant_id,work_order_id) references agro360.agriculture_records(tenant_id,id),
 foreign key(tenant_id,product_id) references agro360.inventory_products(tenant_id,id),
 check(planned_quantity>0 and reserved_quantity>=0 and delivered_quantity>=0 and consumed_quantity>=0 and returned_quantity>=0 and lost_quantity>=0),
 check(consumed_quantity+returned_quantity+lost_quantity<=delivered_quantity),
 check(status in('PLANNED','RESERVED','DELIVERED','SETTLED','SHORTAGE'))
);
create index ix_field_material_order on agro360.field_work_order_materials(tenant_id,work_order_id) where deleted_at is null;

create table agro360.field_material_events (
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), work_order_material_id uuid not null,
 event_type varchar(16) not null, quantity numeric(20,6) not null, reason varchar(1000),
 inventory_movement_id uuid, source_event_id uuid, idempotency_key varchar(100) not null,
 created_at timestamptz not null default now(), created_by uuid not null, unique(tenant_id,id), unique(tenant_id,idempotency_key),
 foreign key(tenant_id,work_order_material_id) references agro360.field_work_order_materials(tenant_id,id),
 foreign key(tenant_id,source_event_id) references agro360.field_material_events(tenant_id,id),
 check(event_type in('RESERVE','DELIVER','CONSUME','RETURN','LOSS','RELEASE')), check(quantity>0),
 check(event_type<>'LOSS' or nullif(trim(reason),'') is not null),
 check(event_type<>'RETURN' or source_event_id is not null)
);

create table agro360.field_work_order_reviews (
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), work_order_id uuid not null,
 order_version bigint not null, outcome varchar(20) not null, summary jsonb not null, notes varchar(2000),
 created_at timestamptz not null default now(), created_by uuid not null, unique(tenant_id,id), unique(tenant_id,work_order_id,order_version),
 foreign key(tenant_id,work_order_id) references agro360.agriculture_records(tenant_id,id),
 check(outcome in('READY','BLOCKED','COMPLETED'))
);

do $$ declare t text; begin foreach t in array array['field_work_order_resources','field_work_logs','field_work_order_materials','field_material_events','field_work_order_reviews'] loop perform agro360.platform_enable_tenant_rls('agro360.'||t); end loop; end $$;
insert into agro360.platform_schema_versions(version,description,installed_at) values('85.0.0','Ordens de campo: recursos, apontamentos, custódia de materiais e conferência',now()) on conflict(version) do nothing;
commit;
