begin;

alter table agro360.procurement_requisitions
    add column if not exists version bigint not null default 1 check (version > 0);

alter table agro360.procurement_purchase_orders
    add column if not exists version bigint not null default 1 check (version > 0);

alter table agro360.procurement_purchase_order_items
    add column if not exists requisition_item_id uuid;

do $$ begin
 if not exists(select 1 from pg_constraint where conname='uq_procurement_requisition_item_tenant_id') then
  alter table agro360.procurement_requisition_items add constraint uq_procurement_requisition_item_tenant_id unique(tenant_id,id);
 end if;
 if not exists(select 1 from pg_constraint where conname='fk_procurement_order_item_requisition_item') then
  alter table agro360.procurement_purchase_order_items add constraint fk_procurement_order_item_requisition_item
   foreign key(tenant_id,requisition_item_id) references agro360.procurement_requisition_items(tenant_id,id);
 end if;
end $$;

create index if not exists ix_procurement_order_item_requisition
 on agro360.procurement_purchase_order_items(tenant_id,requisition_item_id)
 where requisition_item_id is not null;

create table if not exists agro360.procurement_requisition_events(
 id uuid primary key,
 tenant_id uuid not null references agro360.tenancy_tenants(id),
 requisition_id uuid not null,
 event_type varchar(40) not null check(event_type in('DRAFT_CREATED','SUBMITTED','APPROVED','REJECTED','CANCELLED','APPROVAL_INVALIDATED')),
 reason text,
 version bigint not null check(version>0),
 created_at timestamptz not null default now(),
 created_by uuid not null,
 unique(tenant_id,id),
 foreign key(tenant_id,requisition_id) references agro360.procurement_requisitions(tenant_id,id),
 check(event_type not in('REJECTED','CANCELLED') or length(trim(reason))>=3)
);

create index if not exists ix_procurement_requisition_events_history
 on agro360.procurement_requisition_events(tenant_id,requisition_id,created_at,id);
select agro360.platform_enable_tenant_rls('agro360.procurement_requisition_events');

insert into agro360.platform_schema_versions(version,description,installed_at)
values('10.0.0','Ciclo versionado de requisição e saldo autorizado de compra',now())
on conflict(version) do nothing;

commit;
