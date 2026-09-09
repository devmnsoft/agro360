begin;

-- The published Sprint 37 file creates receipt_items with a tenant-composite
-- FK to purchase_order_items, but its parent table originally had no matching
-- unique key. Pre-create the four parent tables with the published shape and
-- the missing tenant key so the historical checksum remains untouched.
create schema if not exists procurement;

create table if not exists procurement.suppliers(
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null references tenancy.tenants(id),
    legal_name varchar(200) not null, trade_name varchar(200), tax_document varchar(20), state_registration varchar(30),
    supplier_type varchar(40) not null, main_category varchar(50) not null, email varchar(254), phone varchar(30),
    address text, city varchar(100), state varchar(60), country varchar(80) not null default 'Brasil', main_contact varchar(160),
    payment_terms varchar(160), average_delivery_days int not null default 0 check(average_delivery_days>=0),
    status varchar(20) not null check(status in('ACTIVE','INACTIVE','BLOCKED','UNDER_REVIEW','APPROVED','REJECTED')),
    rejection_reason text, homologated_at timestamptz, homologated_by uuid, notes text, tags text[] not null default '{}',
    created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
    created_by uuid not null, updated_by uuid not null, deleted_at timestamptz,
    unique(tenant_id,id), check(status<>'REJECTED' or length(trim(rejection_reason))>=3),
    check(status<>'APPROVED' or (homologated_at is not null and homologated_by is not null))
);

create table if not exists procurement.item_catalog(
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null references tenancy.tenants(id),
    name varchar(200) not null, internal_code varchar(50) not null, category varchar(50) not null, unit varchar(20) not null,
    item_type varchar(16) not null check(item_type in('MATERIAL','SERVICE','ASSET')), description text,
    active boolean not null default true, minimum_stock numeric(18,4) check(minimum_stock>=0), cost_center_id uuid,
    managerial_account_id uuid, related_product_id uuid, requires_lot boolean not null default false,
    requires_expiry boolean not null default false, requires_document boolean not null default false,
    requires_inspection boolean not null default false, requires_approved_supplier boolean not null default false,
    notes text, created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
    created_by uuid not null, updated_by uuid not null, deleted_at timestamptz,
    unique(tenant_id,id), unique(tenant_id,internal_code)
);

create table if not exists procurement.purchase_orders(
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null references tenancy.tenants(id),
    number varchar(30) not null, supplier_id uuid not null, requisition_id uuid, quotation_id uuid, requester_id uuid,
    cost_center_id uuid, property_id uuid, payment_terms varchar(160) not null, delivery_on date not null,
    delivery_address text not null, freight numeric(18,2) not null default 0 check(freight>=0),
    taxes numeric(18,2) not null default 0 check(taxes>=0), total numeric(18,2) not null check(total>0),
    status varchar(30) not null check(status in('DRAFT','AWAITING_APPROVAL','APPROVED','SENT','PARTIALLY_RECEIVED','RECEIVED','DIVERGENT','CANCELLED','CLOSED')),
    approved_at timestamptz, approved_by uuid, cancel_reason text, created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, deleted_at timestamptz,
    unique(tenant_id,id), unique(tenant_id,number),
    foreign key(tenant_id,supplier_id) references procurement.suppliers(tenant_id,id)
);

create table if not exists procurement.purchase_order_items(
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, purchase_order_id uuid not null,
    catalog_item_id uuid not null, quantity numeric(18,4) not null check(quantity>0),
    received_quantity numeric(18,4) not null default 0 check(received_quantity>=0), unit varchar(20) not null,
    unit_price numeric(18,4) not null check(unit_price>0), discount numeric(18,2) not null default 0 check(discount>=0),
    total numeric(18,2) not null check(total>=0), created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, deleted_at timestamptz,
    unique(tenant_id,id), foreign key(tenant_id,purchase_order_id) references procurement.purchase_orders(tenant_id,id),
    foreign key(tenant_id,catalog_item_id) references procurement.item_catalog(tenant_id,id)
);

create table if not exists procurement.approval_policies(
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null references tenancy.tenants(id),
    name varchar(160) not null, min_value numeric(18,2) not null default 0, max_value numeric(18,2),
    cost_center_id uuid, category varchar(50), critical boolean not null default false,
    separation_of_duties boolean not null default true, active boolean not null default true,
    created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
    created_by uuid not null, updated_by uuid not null, deleted_at timestamptz,
    unique(tenant_id,id), check(max_value is null or max_value>=min_value)
);

create table if not exists procurement.approval_requests(
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null, policy_id uuid,
    entity_type varchar(30) not null, entity_id uuid not null, requester_id uuid not null,
    status varchar(20) not null check(status in('NOT_REQUIRED','PENDING','ANALYSIS','APPROVED','REJECTED','CANCELLED')),
    amount numeric(18,2) not null check(amount>=0), created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null,
    deleted_at timestamptz, unique(tenant_id,id),
    foreign key(tenant_id,policy_id) references procurement.approval_policies(tenant_id,id)
);

insert into platform.schema_versions(version,description,installed_at)
values('3.6.1','Bridge da chave composta de itens de pedido para a Sprint 37',now())
on conflict(version) do update set description=excluded.description;

commit;
