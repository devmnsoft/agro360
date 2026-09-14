begin;

alter table agro360.production_batches add column if not exists warehouse_id uuid;
do $$ begin alter table agro360.production_batches add constraint fk_production_batches_warehouse foreign key(tenant_id,warehouse_id) references agro360.inventory_warehouses(tenant_id,id); exception when duplicate_object then null; end $$;
alter table agro360.production_batches add column if not exists output_type varchar(16) not null default 'MAIN';
alter table agro360.production_batches drop constraint if exists ck_production_batches_output_type;
alter table agro360.production_batches add constraint ck_production_batches_output_type check(output_type in('MAIN','COPRODUCT'));

create table if not exists agro360.production_recipe_outputs(
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), recipe_version_id uuid not null, product_id uuid not null,
 output_type varchar(16) not null check(output_type in('MAIN','COPRODUCT')), quantity numeric(20,6) not null check(quantity>0), unit varchar(20) not null,
 created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz not null default now(), updated_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,recipe_version_id,product_id,output_type),
 foreign key(tenant_id,recipe_version_id) references agro360.production_recipe_versions(tenant_id,id), foreign key(tenant_id,product_id) references agro360.production_products(tenant_id,id)
);
create table if not exists agro360.production_material_reservations(
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), order_id uuid not null, receipt_id uuid not null,
 material_id uuid not null, warehouse_id uuid not null, quantity numeric(20,6) not null check(quantity>0), consumed_quantity numeric(20,6) not null default 0,
 unit varchar(20) not null, status varchar(24) not null check(status in('RESERVED','PARTIALLY_CONSUMED','CONSUMED','RELEASED')),
 idempotency_key varchar(160) not null, request_hash char(64) not null, created_at timestamptz not null default now(), created_by uuid not null,
 updated_at timestamptz not null default now(), updated_by uuid not null, unique(tenant_id,id), unique(tenant_id,idempotency_key),
 foreign key(tenant_id,order_id) references agro360.production_orders(tenant_id,id), foreign key(tenant_id,receipt_id) references agro360.production_receipts(tenant_id,id),
 foreign key(tenant_id,material_id) references agro360.production_products(tenant_id,id), foreign key(tenant_id,warehouse_id) references agro360.inventory_warehouses(tenant_id,id),
 check(consumed_quantity>=0 and consumed_quantity<=quantity)
);
create table if not exists agro360.production_reservation_consumptions(
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), reservation_id uuid not null, order_id uuid not null,
 quantity numeric(20,6) not null check(quantity>0), unit varchar(20) not null, justification text, idempotency_key varchar(160) not null,
 request_hash char(64) not null, created_at timestamptz not null default now(), created_by uuid not null, unique(tenant_id,id), unique(tenant_id,idempotency_key),
 foreign key(tenant_id,reservation_id) references agro360.production_material_reservations(tenant_id,id), foreign key(tenant_id,order_id) references agro360.production_orders(tenant_id,id)
);
create table if not exists agro360.production_output_requests(
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), order_id uuid not null, batch_id uuid,
 output_type varchar(16) not null check(output_type in('MAIN','COPRODUCT','LOSS','SCRAP')), quantity numeric(20,6) not null check(quantity>0), unit varchar(20) not null,
 idempotency_key varchar(160) not null, request_hash char(64) not null, created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,idempotency_key), foreign key(tenant_id,order_id) references agro360.production_orders(tenant_id,id),
 foreign key(tenant_id,batch_id) references agro360.production_batches(tenant_id,id)
);
create index if not exists ix_prod_reservations_receipt on agro360.production_material_reservations(tenant_id,receipt_id,status);
create index if not exists ix_prod_reservations_order on agro360.production_material_reservations(tenant_id,order_id,status);
select agro360.platform_enable_tenant_rls('agro360.production_recipe_outputs');
select agro360.platform_enable_tenant_rls('agro360.production_material_reservations');
select agro360.platform_enable_tenant_rls('agro360.production_reservation_consumptions');
select agro360.platform_enable_tenant_rls('agro360.production_output_requests');
insert into agro360.platform_schema_versions(version,description,installed_at) values('7.6.0','Beneficiamento integrado: reserva de recebimento, consumo idempotente e resultados',now()) on conflict(version) do nothing;
commit;
