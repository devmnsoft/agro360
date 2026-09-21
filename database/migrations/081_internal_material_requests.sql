begin;
create table if not exists agro360.inventory_material_requests(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null references agro360.tenancy_tenants(id),
 number bigint generated always as identity, farm_id uuid not null, warehouse_id uuid, cost_center_id uuid,
 requester_id uuid not null, purpose varchar(40) not null, related_type varchar(30), related_id uuid,
 needed_on date not null, priority varchar(12) not null, urgency_reason varchar(1000), notes varchar(2000),
 status varchar(24) not null default 'DRAFT', version bigint not null default 1,
 created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz not null default now(), updated_by uuid not null,
 cancelled_at timestamptz, cancelled_by uuid, cancellation_reason varchar(1000),
 unique(tenant_id,id), foreign key(tenant_id,farm_id) references agro360.geo_farms(tenant_id,id),
 foreign key(tenant_id,warehouse_id) references agro360.inventory_warehouses(tenant_id,id),
 foreign key(tenant_id,cost_center_id) references agro360.finance_cost_centers(tenant_id,id),
 check(priority in('LOW','MEDIUM','HIGH','URGENT')), check(status in('DRAFT','AWAITING_APPROVAL','APPROVED','RESERVED','PARTIALLY_DELIVERED','DELIVERED','CLOSED','REJECTED','ADJUSTMENT_REQUESTED','CANCELLED')),
 check(priority<>'URGENT' or length(trim(coalesce(urgency_reason,'')))>=5), check((related_type is null)=(related_id is null)));
create table if not exists agro360.inventory_material_request_items(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, request_id uuid not null, product_id uuid not null,
 requested numeric(20,6) not null, reserved numeric(20,6) not null default 0, delivered numeric(20,6) not null default 0,
 consumed numeric(20,6) not null default 0, returned numeric(20,6) not null default 0, unit varchar(16) not null,
 unique(tenant_id,id), foreign key(tenant_id,request_id) references agro360.inventory_material_requests(tenant_id,id),
 foreign key(tenant_id,product_id) references agro360.inventory_products(tenant_id,id),
 check(requested>0 and reserved>=0 and delivered>=0 and consumed>=0 and returned>=0), check(delivered<=requested));
create table if not exists agro360.inventory_material_reservations(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, request_id uuid not null, item_id uuid not null, warehouse_id uuid not null,
 quantity numeric(20,6) not null, fulfilled numeric(20,6) not null default 0, released numeric(20,6) not null default 0,
 status varchar(16) not null default 'ACTIVE', idempotency_key varchar(160) not null, created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,idempotency_key), foreign key(tenant_id,request_id) references agro360.inventory_material_requests(tenant_id,id),
 foreign key(tenant_id,item_id) references agro360.inventory_material_request_items(tenant_id,id), foreign key(tenant_id,warehouse_id) references agro360.inventory_warehouses(tenant_id,id),
 check(quantity>0 and fulfilled>=0 and released>=0 and fulfilled+released<=quantity), check(status in('ACTIVE','FULFILLED','RELEASED')));
create table if not exists agro360.inventory_material_deliveries(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, request_id uuid not null, item_id uuid not null, warehouse_id uuid not null,
 quantity numeric(20,6) not null, unit varchar(16) not null, destination varchar(300) not null, receiver varchar(160) not null,
 direct_consumption boolean not null default false, unit_cost numeric(18,4) not null, movement_id uuid not null,
 idempotency_key varchar(160) not null, status varchar(16) not null default 'DELIVERED', delivered_at timestamptz not null default now(), delivered_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,idempotency_key), foreign key(tenant_id,request_id) references agro360.inventory_material_requests(tenant_id,id),
 foreign key(tenant_id,item_id) references agro360.inventory_material_request_items(tenant_id,id), check(quantity>0), check(status in('DELIVERED','REVERSED')));
create table if not exists agro360.inventory_material_delivery_lots(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, delivery_id uuid not null, lot_number varchar(100) not null, quantity numeric(20,6) not null check(quantity>0),
 foreign key(tenant_id,delivery_id) references agro360.inventory_material_deliveries(tenant_id,id));
create table if not exists agro360.inventory_material_consumptions(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, request_id uuid not null, delivery_id uuid not null,
 quantity numeric(20,6) not null, destination_type varchar(30) not null, destination_id uuid not null, cost_center_id uuid,
 total_cost numeric(18,4) not null, idempotency_key varchar(160) not null, consumed_at timestamptz not null default now(), consumed_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,idempotency_key), foreign key(tenant_id,delivery_id) references agro360.inventory_material_deliveries(tenant_id,id), check(quantity>0));
create table if not exists agro360.inventory_material_returns(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, request_id uuid not null, delivery_id uuid not null, warehouse_id uuid not null,
 quantity numeric(20,6) not null, lot_number varchar(100) not null, condition varchar(20) not null, reason varchar(1000) not null, responsible varchar(160) not null,
 movement_id uuid, status varchar(20) not null, idempotency_key varchar(160) not null, returned_at timestamptz not null default now(), returned_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,idempotency_key), check(quantity>0), check(condition in('GOOD','DAMAGED','UNKNOWN')), check(status in('AVAILABLE','AWAITING_INSPECTION')));
create table if not exists agro360.inventory_material_request_events(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, request_id uuid not null, type varchar(30) not null, status varchar(24) not null,
 reason varchar(1000), occurred_at timestamptz not null default now(), actor_id uuid not null,
 foreign key(tenant_id,request_id) references agro360.inventory_material_requests(tenant_id,id));
create table if not exists agro360.inventory_material_approval_rules(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, category varchar(60), farm_id uuid, priority varchar(12), segregation_required boolean not null default false, active boolean not null default true,
 unique(tenant_id,id));
create index if not exists ix_material_requests_queue on agro360.inventory_material_requests(tenant_id,status,needed_on,number);
create index if not exists ix_material_events_request on agro360.inventory_material_request_events(tenant_id,request_id,occurred_at);
do $$ declare t text; begin foreach t in array array['inventory_material_requests','inventory_material_request_items','inventory_material_reservations','inventory_material_deliveries','inventory_material_delivery_lots','inventory_material_consumptions','inventory_material_returns','inventory_material_request_events','inventory_material_approval_rules'] loop execute format('alter table agro360.%I enable row level security',t); execute format('alter table agro360.%I force row level security',t); execute format('create policy %I on agro360.%I using (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id=nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',t||'_tenant',t); end loop; end $$;
insert into agro360.platform_schema_versions(version,description,installed_at) values('0.81.0','Requisições internas, reservas, entrega, consumo e devolução',now()) on conflict(version) do nothing;
commit;
