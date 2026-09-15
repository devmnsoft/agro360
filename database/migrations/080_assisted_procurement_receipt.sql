begin;

create table if not exists agro360.procurement_receipt_quarantine(
 tenant_id uuid not null references agro360.tenancy_tenants(id),
 receipt_item_id uuid not null,
 warehouse_id uuid not null,
 product_id uuid not null,
 quantity numeric(18,4) not null check(quantity>0),
 released_quantity numeric(18,4) not null default 0 check(released_quantity>=0),
 rejected_quantity numeric(18,4) not null default 0 check(rejected_quantity>=0),
 unit varchar(20) not null,
 lot_number varchar(100), expires_on date,
 status varchar(30) not null check(status in('PENDING','RELEASED','DECIDED_WITH_REJECTION')),
 reason varchar(1000), created_at timestamptz not null default now(), created_by uuid not null,
 updated_at timestamptz, updated_by uuid,
 primary key(tenant_id,receipt_item_id),
 foreign key(tenant_id,receipt_item_id) references agro360.procurement_receipt_items(tenant_id,id),
 foreign key(tenant_id,warehouse_id) references agro360.inventory_warehouses(tenant_id,id),
 foreign key(tenant_id,product_id) references agro360.inventory_products(tenant_id,id),
 check(released_quantity+rejected_quantity<=quantity)
);

create table if not exists agro360.procurement_receipt_quality_decisions(
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), receipt_item_id uuid not null,
 result varchar(30) not null check(result in('APPROVED','CONDITIONALLY_APPROVED','REJECTED')),
 accepted_quantity numeric(18,4) not null check(accepted_quantity>=0), rejected_quantity numeric(18,4) not null check(rejected_quantity>=0),
 reason varchar(1000), evidence_reference varchar(500), idempotency_key varchar(100) not null,
 decided_at timestamptz not null, decided_by uuid not null, created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,idempotency_key),
 foreign key(tenant_id,receipt_item_id) references agro360.procurement_receipt_items(tenant_id,id),
 check(accepted_quantity+rejected_quantity>0),
 check(result='APPROVED' or nullif(trim(reason),'') is not null)
);

create table if not exists agro360.procurement_receipt_release_links(
 tenant_id uuid not null references agro360.tenancy_tenants(id), decision_id uuid not null, stock_movement_id uuid not null,
 created_at timestamptz not null default now(), created_by uuid not null,
 primary key(tenant_id,decision_id), unique(tenant_id,stock_movement_id),
 foreign key(tenant_id,decision_id) references agro360.procurement_receipt_quality_decisions(tenant_id,id),
 foreign key(tenant_id,stock_movement_id) references agro360.inventory_stock_movements(tenant_id,id)
);

create index if not exists ix_procurement_quarantine_pending on agro360.procurement_receipt_quarantine(tenant_id,status,created_at) where status='PENDING';
create index if not exists ix_procurement_quality_item on agro360.procurement_receipt_quality_decisions(tenant_id,receipt_item_id,decided_at desc);
select agro360.platform_enable_tenant_rls('agro360.procurement_receipt_quarantine');
select agro360.platform_enable_tenant_rls('agro360.procurement_receipt_quality_decisions');
select agro360.platform_enable_tenant_rls('agro360.procurement_receipt_release_links');
insert into agro360.platform_schema_versions(version,description,installed_at) values('8.0.0','Recebimento assistido com quarentena e liberação parcial idempotente',now()) on conflict(version) do nothing;
commit;
