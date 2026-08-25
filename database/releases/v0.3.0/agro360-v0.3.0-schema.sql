-- Sprint 6: supply, purchasing, generalized stock, fleet, maintenance and fuel.
create table if not exists platform.schema_versions(version varchar(30) primary key, description varchar(240) not null, installed_at timestamptz not null default now());
insert into platform.schema_versions values ('0.3.0','Sprint 6 operational modules',now()) on conflict(version) do nothing;

alter table inventory.products add column if not exists minimum_stock numeric(20,6) not null default 0;
alter table fleet.assets alter column farm_id drop not null;
alter table fleet.assets alter column code drop not null;
alter table fleet.assets alter column name drop not null;
alter table fleet.assets alter column asset_type drop not null;
alter table fleet.assets add column if not exists type varchar(40);
alter table fleet.assets add column if not exists brand varchar(100);
alter table fleet.assets add column if not exists year integer;
alter table fleet.assets add column if not exists identification varchar(80);
alter table fleet.assets add column if not exists hour_meter numeric(14,2);
alter table fleet.assets add column if not exists odometer numeric(14,2);
alter table fleet.assets add column if not exists responsible_id uuid;
alter table fleet.assets add column if not exists updated_by uuid;
alter table fleet.assets add column if not exists deleted_at timestamptz;
create unique index if not exists ux_assets_identification on fleet.assets(tenant_id,identification) where deleted_at is null and identification is not null;

create table if not exists purchasing.suppliers(id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), name varchar(160) not null, type varchar(40) not null, document_number varchar(30) not null, state_registration varchar(30), email varchar(254), phone varchar(40), address text, categories text[] not null default '{}', status varchar(20) not null default 'ACTIVE', notes text, created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz, updated_by uuid, deleted_at timestamptz, deleted_by uuid, version bigint not null default 1, unique(tenant_id,id), check(status in('ACTIVE','INACTIVE','BLOCKED')));
create unique index if not exists ux_suppliers_document on purchasing.suppliers(tenant_id,document_number) where deleted_at is null;
create index if not exists ix_suppliers_search on purchasing.suppliers using gin(name gin_trgm_ops);
create table if not exists purchasing.purchase_orders(id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), supplier_id uuid not null, farm_id uuid, cost_center varchar(100), season_id uuid, status varchar(30) not null default 'DRAFT', discount numeric(18,4) not null default 0, freight numeric(18,4) not null default 0, taxes numeric(18,4) not null default 0, total numeric(18,4) not null, received_at timestamptz, created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz, updated_by uuid, unique(tenant_id,id), foreign key(tenant_id,supplier_id) references purchasing.suppliers(tenant_id,id), check(total>=0));
create index if not exists ix_purchase_status on purchasing.purchase_orders(tenant_id,status,created_at desc);
create table if not exists purchasing.purchase_items(id uuid primary key, tenant_id uuid not null, purchase_id uuid not null, product_id uuid not null, description varchar(240) not null, quantity numeric(20,6) not null, received_quantity numeric(20,6) not null default 0, unit_price numeric(18,4) not null, foreign key(tenant_id,purchase_id) references purchasing.purchase_orders(tenant_id,id), foreign key(tenant_id,product_id) references inventory.products(tenant_id,id), check(quantity>0 and received_quantity between 0 and quantity and unit_price>=0));
create table if not exists purchasing.purchase_status_history(id uuid primary key,tenant_id uuid not null,purchase_id uuid not null,from_status varchar(30) not null,to_status varchar(30) not null,changed_at timestamptz not null default now(),changed_by uuid not null,foreign key(tenant_id,purchase_id) references purchasing.purchase_orders(tenant_id,id));

create table if not exists inventory.stock_lots(id uuid primary key,tenant_id uuid not null,warehouse_id uuid not null,product_id uuid not null,lot_number varchar(100) not null,expires_on date,quantity numeric(20,6) not null default 0,unique(tenant_id,warehouse_id,product_id,lot_number),foreign key(tenant_id,warehouse_id) references inventory.warehouses(tenant_id,id),foreign key(tenant_id,product_id) references inventory.products(tenant_id,id));
create or replace function inventory.apply_stock_movement(p_tenant uuid,p_warehouse uuid,p_product uuid,p_quantity numeric,p_cost numeric,p_type varchar,p_reference uuid,p_lot varchar,p_expires date,p_user uuid,p_reason text default null) returns uuid language plpgsql as $$
declare b inventory.stock_balances%rowtype; movement uuid:=gen_random_uuid(); baseunit varchar(16); newbalance numeric; newcost numeric;
begin if p_quantity=0 or p_cost<0 then raise exception 'invalid stock movement'; end if; select base_unit into baseunit from inventory.products where tenant_id=p_tenant and id=p_product; if baseunit is null then raise exception 'product not found'; end if;
 select * into b from inventory.stock_balances where tenant_id=p_tenant and warehouse_id=p_warehouse and product_id=p_product for update;
 if not found then if p_quantity<0 then raise exception 'insufficient stock'; end if; insert into inventory.stock_balances(id,tenant_id,warehouse_id,product_id,unit,available,minimum,average_cost,version) select gen_random_uuid(),p_tenant,p_warehouse,p_product,baseunit,p_quantity,minimum_stock,p_cost,1 from inventory.products where id=p_product returning * into b; newbalance:=p_quantity;newcost:=p_cost;
 else newbalance:=b.available+p_quantity;if newbalance<0 then raise exception 'insufficient stock';end if;newcost:=case when p_quantity>0 then ((b.available*b.average_cost)+(p_quantity*p_cost))/nullif(newbalance,0) else b.average_cost end;update inventory.stock_balances set available=newbalance,average_cost=newcost,updated_at=now(),version=version+1 where id=b.id;end if;
 insert into inventory.stock_movements(id,tenant_id,warehouse_id,product_id,movement_type,quantity,unit,unit_cost,total_cost,lot_number,expires_on,reference_type,reference_id,notes,balance_after,average_cost_after,balance_version,occurred_at,created_by) values(movement,p_tenant,p_warehouse,p_product,case when p_type in('ENTRY','PURCHASE_RECEIPT') then 'RECEIPT' when p_type in('EXIT','MAINTENANCE','FUEL') then 'CONSUMPTION' when p_type='ADJUST' and p_quantity>0 then 'ADJUSTMENT_IN' when p_type='ADJUST' then 'ADJUSTMENT_OUT' else p_type end,abs(p_quantity),baseunit,p_cost,abs(p_quantity)*p_cost,p_lot,p_expires,p_type,p_reference,p_reason,newbalance,newcost,coalesce(b.version,0)+1,now(),p_user);
 if p_lot is not null then insert into inventory.stock_lots values(gen_random_uuid(),p_tenant,p_warehouse,p_product,p_lot,p_expires,p_quantity) on conflict(tenant_id,warehouse_id,product_id,lot_number) do update set quantity=inventory.stock_lots.quantity+excluded.quantity,expires_on=coalesce(excluded.expires_on,inventory.stock_lots.expires_on);end if;return movement;end $$;

create table if not exists fleet.maintenance_orders(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),asset_id uuid not null,type varchar(30) not null,description varchar(500) not null,supplier_id uuid,responsible_id uuid,status varchar(24) not null,scheduled_for date,next_review_date date,next_hour_meter numeric(14,2),next_odometer numeric(14,2),parts_cost numeric(18,4) not null,labor_cost numeric(18,4) not null,total_cost numeric(18,4) not null,completed_at timestamptz,completion_notes text,created_at timestamptz not null default now(),created_by uuid not null,updated_at timestamptz,updated_by uuid,unique(tenant_id,id),foreign key(tenant_id,asset_id) references fleet.assets(tenant_id,id));
create table if not exists fleet.maintenance_parts(id uuid primary key,tenant_id uuid not null,maintenance_id uuid not null,product_id uuid not null,warehouse_id uuid not null,quantity numeric(20,6) not null,unit_cost numeric(18,4) not null,foreign key(tenant_id,maintenance_id) references fleet.maintenance_orders(tenant_id,id));
create table if not exists fleet.fuel_fillups(id uuid primary key,tenant_id uuid not null references tenancy.tenants(id),asset_id uuid not null,fuel_product_id uuid not null,warehouse_id uuid not null,quantity numeric(20,6) not null,unit_price numeric(18,4) not null,total numeric(18,4) not null,hour_meter numeric(14,2),odometer numeric(14,2),responsible_id uuid,location varchar(200),created_at timestamptz not null default now(),created_by uuid not null,foreign key(tenant_id,asset_id) references fleet.assets(tenant_id,id),check(quantity>0 and unit_price>=0));
create index if not exists ix_maintenance_due on fleet.maintenance_orders(tenant_id,scheduled_for) where status<>'COMPLETED';
create index if not exists ix_fillups_month on fleet.fuel_fillups(tenant_id,created_at desc);
create or replace view analytics.operational_inventory_alerts as select b.tenant_id,b.product_id,b.warehouse_id,b.available,b.minimum,l.expires_on,(b.available<b.minimum) low_stock,(l.expires_on<=current_date+30 and l.quantity>0) expiring from inventory.stock_balances b left join inventory.stock_lots l on l.tenant_id=b.tenant_id and l.product_id=b.product_id and l.warehouse_id=b.warehouse_id;

create or replace function platform.enable_tenant_rls(target_table regclass) returns void language plpgsql as $$ begin execute format('alter table %s enable row level security',target_table); execute format('alter table %s force row level security',target_table); execute format('create policy tenant_isolation on %s using (tenant_id=platform.current_tenant_id()) with check (tenant_id=platform.current_tenant_id())',target_table); end $$;
select platform.enable_tenant_rls('purchasing.suppliers');select platform.enable_tenant_rls('purchasing.purchase_orders');select platform.enable_tenant_rls('purchasing.purchase_items');select platform.enable_tenant_rls('purchasing.purchase_status_history');select platform.enable_tenant_rls('inventory.stock_lots');select platform.enable_tenant_rls('fleet.maintenance_orders');select platform.enable_tenant_rls('fleet.maintenance_parts');select platform.enable_tenant_rls('fleet.fuel_fillups');

drop function platform.enable_tenant_rls(regclass);

create or replace function platform.set_updated_at() returns trigger language plpgsql as $$ begin new.updated_at=now(); return new; end $$;
drop trigger if exists suppliers_set_updated_at on purchasing.suppliers;
create trigger suppliers_set_updated_at before update on purchasing.suppliers for each row execute function platform.set_updated_at();
drop trigger if exists assets_set_updated_at on fleet.assets;
create trigger assets_set_updated_at before update on fleet.assets for each row execute function platform.set_updated_at();
