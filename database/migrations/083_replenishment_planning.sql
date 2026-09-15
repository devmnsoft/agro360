begin;

create table agro360.inventory_replenishment_policies (
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), product_id uuid not null,
 warehouse_id uuid not null, minimum_stock numeric(20,6) not null default 0, target_stock numeric(20,6) not null,
 replenishment_days integer, minimum_purchase numeric(20,6), purchase_multiple numeric(20,6), purchase_unit varchar(16) not null,
 stock_per_purchase_unit numeric(20,6) not null, responsible_id uuid not null, status varchar(16) not null default 'ACTIVE', version bigint not null default 1,
 created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz not null default now(), updated_by uuid not null,
 unique(tenant_id,id), foreign key(tenant_id,product_id) references agro360.inventory_products(tenant_id,id),
 foreign key(tenant_id,warehouse_id) references agro360.inventory_warehouses(tenant_id,id),
 check(minimum_stock>=0 and target_stock>=minimum_stock), check(replenishment_days is null or replenishment_days>=0),
 check(minimum_purchase is null or minimum_purchase>=0), check(purchase_multiple is null or purchase_multiple>0),
 check(stock_per_purchase_unit>0), check(status in ('ACTIVE','INACTIVE'))
);
create unique index ux_replenishment_policy_active on agro360.inventory_replenishment_policies(tenant_id,product_id,warehouse_id) where status='ACTIVE';

create table agro360.inventory_material_needs (
 id uuid primary key, tenant_id uuid not null, policy_id uuid not null, product_id uuid not null, warehouse_id uuid not null,
 needed_on date not null, horizon_on date not null, usable_stock numeric(20,6) not null, reserved_stock numeric(20,6) not null,
 uncovered_demand numeric(20,6) not null, confirmed_inbound numeric(20,6) not null, transfer_inbound numeric(20,6) not null,
 existing_coverage numeric(20,6) not null, projected_stock numeric(20,6) not null, operational_need numeric(20,6) not null,
 suggested_stock_quantity numeric(20,6) not null, suggested_purchase_quantity numeric(20,6) not null, purchase_unit varchar(16) not null,
 calculation jsonb not null, status varchar(24) not null default 'IDENTIFIED', version bigint not null default 1,
 decision_reason varchar(1000), requisition_id uuid, transfer_id uuid, idempotency_key varchar(160),
 created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz not null default now(), updated_by uuid not null,
 unique(tenant_id,id), foreign key(tenant_id,policy_id) references agro360.inventory_replenishment_policies(tenant_id,id),
 foreign key(tenant_id,product_id) references agro360.inventory_products(tenant_id,id), foreign key(tenant_id,warehouse_id) references agro360.inventory_warehouses(tenant_id,id),
 check(status in ('IDENTIFIED','ANALYSIS','FORWARDED','PARTIALLY_FULFILLED','FULFILLED','DISMISSED','CANCELLED')),
 check(suggested_stock_quantity>=0 and suggested_purchase_quantity>=0)
);
create unique index ux_material_need_confirmation on agro360.inventory_material_needs(tenant_id,idempotency_key) where idempotency_key is not null;
create index ix_material_needs_queue on agro360.inventory_material_needs(tenant_id,status,needed_on,product_id,id);
do $$ declare t text; begin foreach t in array array['inventory_replenishment_policies','inventory_material_needs'] loop perform agro360.platform_enable_tenant_rls('agro360.'||t); end loop; end $$;
insert into agro360.platform_schema_versions(version,description,installed_at) values('83.0.0','Planejamento de reposição e necessidades de materiais',now()) on conflict(version) do nothing;
commit;
