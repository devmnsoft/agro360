begin;

create table agro360.inventory_transfers (
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), number bigint generated always as identity,
 farm_id uuid not null, source_warehouse_id uuid not null, destination_warehouse_id uuid not null,
 requested_by uuid not null, responsible_id uuid not null, expected_on date not null, justification varchar(1000) not null,
 status varchar(24) not null default 'DRAFT', version bigint not null default 1,
 shipped_at timestamptz, shipped_by uuid, closed_at timestamptz, closed_by uuid,
 created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz not null default now(), updated_by uuid not null,
 unique(tenant_id,id),
 foreign key(tenant_id,farm_id) references agro360.geo_farms(tenant_id,id),
 foreign key(tenant_id,source_warehouse_id) references agro360.inventory_warehouses(tenant_id,id),
 foreign key(tenant_id,destination_warehouse_id) references agro360.inventory_warehouses(tenant_id,id),
 check(source_warehouse_id<>destination_warehouse_id),
 check(status in ('DRAFT','AWAITING_SHIPMENT','IN_TRANSIT','PARTIALLY_RECEIVED','RECEIVED','CANCELLED'))
);
create table agro360.inventory_transfer_items (
 id uuid primary key, tenant_id uuid not null, transfer_id uuid not null, product_id uuid not null,
 lot_number varchar(100), expires_on date, quantity numeric(20,6) not null, received numeric(20,6) not null default 0,
 unit varchar(16) not null, blocked boolean not null default false, unit_cost numeric(18,4),
 unique(tenant_id,id), foreign key(tenant_id,transfer_id) references agro360.inventory_transfers(tenant_id,id),
 foreign key(tenant_id,product_id) references agro360.inventory_products(tenant_id,id), check(quantity>0), check(received>=0 and received<=quantity)
);
create table agro360.inventory_transfer_receipts (
 id uuid primary key, tenant_id uuid not null, transfer_id uuid not null, item_id uuid not null,
 quantity numeric(20,6) not null, condition varchar(20) not null, occurrence varchar(1000), idempotency_key varchar(160) not null,
 received_at timestamptz not null default now(), received_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,idempotency_key), foreign key(tenant_id,transfer_id) references agro360.inventory_transfers(tenant_id,id),
 foreign key(tenant_id,item_id) references agro360.inventory_transfer_items(tenant_id,id), check(quantity>0), check(condition in ('GOOD','DAMAGED','BLOCKED'))
);
create table agro360.inventory_transfer_events (
 id uuid primary key, tenant_id uuid not null, transfer_id uuid not null, type varchar(30) not null, reason varchar(1000),
 occurred_at timestamptz not null default now(), actor_id uuid not null,
 foreign key(tenant_id,transfer_id) references agro360.inventory_transfers(tenant_id,id)
);

create table agro360.inventory_counts (
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), number bigint generated always as identity,
 warehouse_id uuid not null, category varchar(60), product_id uuid, lot_number varchar(100), material_status varchar(20),
 reference_at timestamptz, blind boolean not null default false, movement_policy varchar(16) not null default 'BLOCK',
 responsible_id uuid not null, status varchar(24) not null default 'PLANNED', version bigint not null default 1,
 opened_at timestamptz, opened_by uuid, completed_at timestamptz, completed_by uuid,
 created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,id), foreign key(tenant_id,warehouse_id) references agro360.inventory_warehouses(tenant_id,id),
 foreign key(tenant_id,product_id) references agro360.inventory_products(tenant_id,id),
 check(status in ('PLANNED','COUNTING','RECONCILING','AWAITING_APPROVAL','COMPLETED','CANCELLED')), check(movement_policy='BLOCK')
);
create table agro360.inventory_count_items (
 id uuid primary key, tenant_id uuid not null, count_id uuid not null, product_id uuid not null, lot_number varchar(100), location varchar(160),
 unit varchar(16) not null, reference_quantity numeric(20,6) not null, reference_reserved numeric(20,6) not null,
 accepted_quantity numeric(20,6), accepted_entry_id uuid, decision varchar(24), justification varchar(1000),
 adjustment_movement_id uuid, adjustment_value numeric(18,4), valuation_pending boolean not null default false,
 unique(tenant_id,id), foreign key(tenant_id,count_id) references agro360.inventory_counts(tenant_id,id),
 foreign key(tenant_id,product_id) references agro360.inventory_products(tenant_id,id),
 check(accepted_quantity is null or accepted_quantity>=0), check(decision is null or decision in ('ACCEPT','RECOUNT','ADJUST','NO_ACTION'))
);
create table agro360.inventory_count_entries (
 id uuid primary key, tenant_id uuid not null, count_id uuid not null, item_id uuid not null, round integer not null,
 quantity numeric(20,6) not null, unit varchar(16) not null, note varchar(1000), evidence_url varchar(1000),
 counted_at timestamptz not null default now(), counted_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,item_id,round), foreign key(tenant_id,count_id) references agro360.inventory_counts(tenant_id,id),
 foreign key(tenant_id,item_id) references agro360.inventory_count_items(tenant_id,id), check(round>0), check(quantity>=0)
);
alter table agro360.inventory_count_items add constraint fk_count_accepted_entry foreign key(tenant_id,accepted_entry_id) references agro360.inventory_count_entries(tenant_id,id);

create unique index ux_transfer_ship_movement on agro360.inventory_stock_movements(tenant_id,reference_id,product_id,coalesce(lot_number,'')) where reference_type='STOCK_TRANSFER_SHIPMENT';
create unique index ux_count_adjustment_movement on agro360.inventory_stock_movements(tenant_id,reference_id,product_id,coalesce(lot_number,'')) where reference_type='PHYSICAL_COUNT_ADJUSTMENT';
create index ix_transfers_queue on agro360.inventory_transfers(tenant_id,status,expected_on,number);
create index ix_counts_queue on agro360.inventory_counts(tenant_id,status,number);
create unique index ux_active_count_scope on agro360.inventory_counts(tenant_id,warehouse_id,coalesce(product_id,'00000000-0000-0000-0000-000000000000'::uuid),coalesce(lot_number,'')) where status in ('COUNTING','RECONCILING','AWAITING_APPROVAL');

do $$ declare t text; begin foreach t in array array['inventory_transfers','inventory_transfer_items','inventory_transfer_receipts','inventory_transfer_events','inventory_counts','inventory_count_items','inventory_count_entries'] loop perform agro360.platform_enable_tenant_rls('agro360.'||t); end loop; end $$;
insert into agro360.platform_schema_versions(version,description,installed_at) values('82.0.0','Transferências e inventário físico transacionais',now()) on conflict(version) do nothing;
commit;
