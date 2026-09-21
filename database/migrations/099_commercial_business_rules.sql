begin;

-- Amplia os estados sem recriar os agregados comerciais existentes.
alter table agro360.crm_customers drop constraint if exists crm_customers_status_check;
alter table agro360.crm_customers drop constraint if exists customers_status_check;
alter table agro360.crm_customers add constraint crm_customers_status_check
    check(status in('ACTIVE','UNDER_REVIEW','BLOCKED','DELINQUENT','INACTIVE'));

alter table agro360.sales_contracts drop constraint if exists sales_contracts_status_check;
alter table agro360.sales_contracts add constraint sales_contracts_status_check
    check(status in('DRAFT','UNDER_REVIEW','APPROVED','ACTIVE','SUSPENDED','PARTIALLY_FULFILLED','FULFILLED','CANCELLED','CLOSED','EXPIRED'));

alter table agro360.sales_orders drop constraint if exists sales_orders_status_check;
alter table agro360.sales_orders drop constraint if exists orders_status_check;
alter table agro360.sales_orders add constraint sales_orders_status_check
    check(status in('DRAFT','UNDER_REVIEW','APPROVED','RESERVED','FULFILLMENT','INVOICED','DELIVERED','CANCELLED','RETURNED'));

alter table agro360.sales_contracts
    add column if not exists requires_environmental_documents boolean not null default false,
    add column if not exists requires_tracked_origin boolean not null default false,
    add column if not exists requires_photo_gps_evidence boolean not null default false,
    add column if not exists requires_quality_report boolean not null default false,
    add column if not exists requires_export_compliance boolean not null default false;

alter table agro360.sales_orders
    add column if not exists requires_environmental_documents boolean not null default false,
    add column if not exists requires_tracked_origin boolean not null default false,
    add column if not exists requires_photo_gps_evidence boolean not null default false,
    add column if not exists requires_quality_report boolean not null default false,
    add column if not exists requires_export_compliance boolean not null default false;

create index if not exists ix_sales_orders_compliance_pending
    on agro360.sales_orders(tenant_id,updated_at desc)
    where deleted_at is null and (requires_environmental_documents or requires_tracked_origin or requires_photo_gps_evidence or requires_quality_report or requires_export_compliance);

insert into agro360.platform_schema_versions(version,description,installed_at)
values('9.9.0','Estados e requisitos de compliance do fluxo comercial',now())
on conflict(version) do nothing;

commit;
