begin;

-- Jornada comercial -> estoque -> transporte -> entrega. Os eventos efetivados nunca são apagados.
create table if not exists agro360.fulfillment_reservations(
 id uuid primary key, tenant_id uuid not null, order_item_id uuid not null, stock_lot_id uuid not null,
 quantity numeric(20,6) not null check(quantity>0), unit varchar(20) not null,
 status varchar(20) not null check(status in('ACTIVE','CONSUMED','RELEASED','CANCELLED')),
 idempotency_key varchar(120) not null, request_hash char(64) not null, version bigint not null default 1,
 created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz not null default now(), updated_by uuid not null,
 deleted_at timestamptz, deleted_by uuid, deletion_reason text,
 unique(tenant_id,id), unique(tenant_id,idempotency_key),
 foreign key(tenant_id,order_item_id) references agro360.sales_order_items(tenant_id,id),
 foreign key(tenant_id,stock_lot_id) references agro360.inventory_stock_lots(tenant_id,id));

create table if not exists agro360.fulfillment_shipments(
 id uuid primary key, tenant_id uuid not null, number varchar(40) not null, origin_warehouse_id uuid not null,
 destination text not null, customer_id uuid not null, status varchar(24) not null check(status in('PREPARING','CHECKED','DISPATCHED','IN_DELIVERY','PARTIAL','RETURN_PENDING','RECONCILED','CANCELLED')),
 idempotency_key varchar(120) not null, request_hash char(64) not null, dispatched_at timestamptz,
 version bigint not null default 1, created_at timestamptz not null default now(), created_by uuid not null,
 updated_at timestamptz not null default now(), updated_by uuid not null, deleted_at timestamptz, deleted_by uuid, deletion_reason text,
 unique(tenant_id,id), unique(tenant_id,number), unique(tenant_id,idempotency_key),
 foreign key(tenant_id,origin_warehouse_id) references agro360.inventory_warehouses(tenant_id,id),
 foreign key(tenant_id,customer_id) references agro360.crm_customers(tenant_id,id));

create table if not exists agro360.fulfillment_shipment_items(
 id uuid primary key, tenant_id uuid not null, shipment_id uuid not null, reservation_id uuid not null,
 order_item_id uuid not null, stock_lot_id uuid not null, requested_quantity numeric(20,6) not null,
 reserved_quantity numeric(20,6) not null, picked_quantity numeric(20,6) not null,
 checked_quantity numeric(20,6) not null, accepted_quantity numeric(20,6) not null default 0,
 refused_quantity numeric(20,6) not null default 0, returned_quantity numeric(20,6) not null default 0,
 lost_quantity numeric(20,6) not null default 0, unit varchar(20) not null, divergence_reason text,
 created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz not null default now(), updated_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,reservation_id), foreign key(tenant_id,shipment_id) references agro360.fulfillment_shipments(tenant_id,id),
 foreign key(tenant_id,reservation_id) references agro360.fulfillment_reservations(tenant_id,id),
 check(checked_quantity<=picked_quantity and picked_quantity<=reserved_quantity),
 check(accepted_quantity+refused_quantity+returned_quantity+lost_quantity<=checked_quantity));

create table if not exists agro360.fulfillment_delivery_attempts(
 id uuid primary key, tenant_id uuid not null, shipment_id uuid not null, occurred_at timestamptz not null,
 destination text not null, responsible_id uuid not null, status varchar(24) not null check(status in('PARTIAL','ACCEPTED','REFUSED','FAILED')),
 reason text, evidence_document_id uuid, evidence_pending boolean not null default false, pending_notes text,
 idempotency_key varchar(120) not null, request_hash char(64) not null, recorded_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,idempotency_key), foreign key(tenant_id,shipment_id) references agro360.fulfillment_shipments(tenant_id,id),
 check(not evidence_pending or evidence_document_id is null));

create table if not exists agro360.fulfillment_delivery_attempt_items(
 id uuid primary key, tenant_id uuid not null, attempt_id uuid not null, shipment_item_id uuid not null,
 accepted_quantity numeric(20,6) not null default 0, refused_quantity numeric(20,6) not null default 0,
 reason text, created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,attempt_id,shipment_item_id),
 foreign key(tenant_id,attempt_id) references agro360.fulfillment_delivery_attempts(tenant_id,id),
 foreign key(tenant_id,shipment_item_id) references agro360.fulfillment_shipment_items(tenant_id,id),
 check(accepted_quantity>=0 and refused_quantity>=0 and accepted_quantity+refused_quantity>0));

create table if not exists agro360.fulfillment_returns(
 id uuid primary key, tenant_id uuid not null, shipment_item_id uuid not null, quantity numeric(20,6) not null check(quantity>0),
 status varchar(24) not null check(status in('AWAITING_RECEIPT','AWAITING_QUALITY','RELEASED','BLOCKED','DISPOSED')),
 received_at timestamptz, quality_decision_at timestamptz, reason text not null, idempotency_key varchar(120) not null,
 request_hash char(64) not null, created_at timestamptz not null default now(), created_by uuid not null,
 updated_at timestamptz not null default now(), updated_by uuid not null, unique(tenant_id,id), unique(tenant_id,idempotency_key),
 foreign key(tenant_id,shipment_item_id) references agro360.fulfillment_shipment_items(tenant_id,id));

alter table agro360.logistics_trips add column if not exists asset_id uuid;
alter table agro360.logistics_trips add column if not exists responsible_id uuid;
alter table agro360.logistics_trips add column if not exists planned_start timestamptz;
alter table agro360.logistics_trips add column if not exists planned_end timestamptz;
alter table agro360.logistics_trips add column if not exists transport_mode varchar(20) not null default 'ROAD';
alter table agro360.logistics_trips add column if not exists capacity_dimension varchar(20);
alter table agro360.logistics_trips add column if not exists capacity_used numeric(20,6);
alter table agro360.logistics_trips add column if not exists capacity_total numeric(20,6);
alter table agro360.logistics_trips add column if not exists route_source varchar(20) not null default 'MANUAL';

create table if not exists agro360.fulfillment_trip_shipments(
 tenant_id uuid not null, trip_id uuid not null, shipment_id uuid not null, stop_sequence integer not null check(stop_sequence>0),
 destination text not null, created_at timestamptz not null default now(), created_by uuid not null,
 primary key(tenant_id,trip_id,shipment_id), foreign key(tenant_id,shipment_id) references agro360.fulfillment_shipments(tenant_id,id));

create index if not exists ix_fulfillment_queue on agro360.fulfillment_shipments(tenant_id,status,created_at) where deleted_at is null;
create index if not exists ix_fulfillment_returns_quality on agro360.fulfillment_returns(tenant_id,status) where status='AWAITING_QUALITY';

do $$ declare t text; begin
 foreach t in array array['fulfillment_reservations','fulfillment_shipments','fulfillment_shipment_items','fulfillment_delivery_attempts','fulfillment_delivery_attempt_items','fulfillment_returns','fulfillment_trip_shipments'] loop
  perform agro360.platform_enable_tenant_rls('agro360.'||t);
 end loop;
end $$;

insert into agro360.identity_permissions(code,module,description) values
 ('logistics.fulfillment.read','Logistics','Consultar separação, expedição, entrega e retorno.'),
 ('logistics.fulfillment.write','Logistics','Executar separação, expedição, entrega e retorno.')
on conflict(code) do update set description=excluded.description;
insert into agro360.platform_schema_versions(version,description,installed_at)
values('7.1.0','Jornada transacional de expedição e entrega',now()) on conflict(version) do nothing;
commit;
