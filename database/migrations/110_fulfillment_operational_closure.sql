begin;

-- Quantidade cancelada pertence ao compromisso comercial. Reserva, separação e
-- conferência continuam sendo subconjuntos do pendente e não o reduzem.
alter table agro360.sales_order_items
    add column if not exists cancelled_quantity numeric(20,6) not null default 0,
    add column if not exists fulfillment_version bigint not null default 1;

alter table agro360.sales_order_items drop constraint if exists ck_sales_order_item_cancelled_quantity;
alter table agro360.sales_order_items add constraint ck_sales_order_item_cancelled_quantity
    check(cancelled_quantity >= 0 and cancelled_quantity <= quantity);

-- Itens começam em preparação; zero é válido até que cada etapa seja registrada.
alter table agro360.fulfillment_shipment_items
    alter column picked_quantity set default 0,
    alter column checked_quantity set default 0;
alter table agro360.fulfillment_shipment_items
    add column if not exists version bigint not null default 1;

create table if not exists agro360.fulfillment_operation_requests(
 id uuid primary key, tenant_id uuid not null, operation varchar(32) not null,
 aggregate_id uuid not null, idempotency_key varchar(120) not null,
 request_hash char(64) not null, result_version bigint,
 created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,operation,idempotency_key),
 check(nullif(trim(idempotency_key),'') is not null),
 check(request_hash ~ '^[0-9a-f]{64}$'));

create table if not exists agro360.fulfillment_order_item_cancellations(
 id uuid primary key, tenant_id uuid not null, order_item_id uuid not null,
 quantity numeric(20,6) not null check(quantity > 0), reason text not null,
 request_id uuid not null, created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,request_id),
 foreign key(tenant_id,order_item_id) references agro360.sales_order_items(tenant_id,id),
 foreign key(tenant_id,request_id) references agro360.fulfillment_operation_requests(tenant_id,id));

create table if not exists agro360.fulfillment_reservation_releases(
 id uuid primary key, tenant_id uuid not null, reservation_id uuid not null,
 quantity numeric(20,6) not null check(quantity > 0), reason text not null,
 request_id uuid not null, created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,request_id),
 foreign key(tenant_id,reservation_id) references agro360.fulfillment_reservations(tenant_id,id),
 foreign key(tenant_id,request_id) references agro360.fulfillment_operation_requests(tenant_id,id));

do $$ begin
 perform agro360.platform_enable_tenant_rls('agro360.fulfillment_operation_requests');
 perform agro360.platform_enable_tenant_rls('agro360.fulfillment_order_item_cancellations');
 perform agro360.platform_enable_tenant_rls('agro360.fulfillment_reservation_releases');
end $$;

insert into agro360.platform_schema_versions(version,description,installed_at)
values('11.0.0','Fechamento operacional: etapas, liberação e cancelamento quantitativo',now())
on conflict(version) do nothing;
commit;
