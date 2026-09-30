begin;

create sequence if not exists agro360.sales_delivery_schedule_number_seq;

create table if not exists agro360.sales_delivery_schedules(
    id uuid primary key,
    tenant_id uuid not null,
    order_id uuid not null,
    schedule_number varchar(40) not null,
    destination text not null,
    responsible_id uuid,
    planned_date date not null,
    original_planned_date date not null,
    status varchar(24) not null check(status in('PLANNED','PREPARING','DISPATCHED','PARTIALLY_DELIVERED','DELIVERED','CANCELLED')),
    notes text,
    cancellation_reason text,
    idempotency_key varchar(120),
    request_hash char(64),
    version bigint not null default 1,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz not null default now(),
    updated_by uuid not null,
    deleted_at timestamptz,
    unique(tenant_id, id),
    unique(tenant_id, schedule_number),
    foreign key(tenant_id, order_id) references agro360.sales_orders(tenant_id, id),
    foreign key(tenant_id, responsible_id) references agro360.identity_users(tenant_id, id)
);

create table if not exists agro360.sales_delivery_schedule_items(
    id uuid primary key,
    tenant_id uuid not null,
    schedule_id uuid not null,
    order_item_id uuid not null,
    quantity numeric(20,6) not null check(quantity > 0),
    original_quantity numeric(20,6) not null check(original_quantity > 0),
    delivered_quantity numeric(20,6) not null default 0 check(delivered_quantity >= 0),
    dispatched_quantity numeric(20,6) not null default 0 check(dispatched_quantity >= 0),
    unit varchar(20) not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    unique(tenant_id, id),
    foreign key(tenant_id, schedule_id) references agro360.sales_delivery_schedules(tenant_id, id),
    foreign key(tenant_id, order_item_id) references agro360.sales_order_items(tenant_id, id),
    check(delivered_quantity <= dispatched_quantity and dispatched_quantity <= quantity)
);

create table if not exists agro360.sales_delivery_schedule_revisions(
    id uuid primary key,
    tenant_id uuid not null,
    schedule_id uuid not null,
    version bigint not null,
    reason text not null,
    actor_id uuid not null,
    previous_date date not null,
    new_date date not null,
    previous_items jsonb not null,
    new_items jsonb not null,
    created_at timestamptz not null default now(),
    unique(tenant_id, id),
    unique(tenant_id, schedule_id, version),
    foreign key(tenant_id, schedule_id) references agro360.sales_delivery_schedules(tenant_id, id)
);

alter table agro360.fulfillment_shipments add column if not exists schedule_id uuid;
alter table agro360.fulfillment_shipment_items add column if not exists schedule_item_id uuid;

create index if not exists ix_sales_delivery_schedules_order on agro360.sales_delivery_schedules(tenant_id, order_id);
create index if not exists ix_sales_delivery_schedules_status on agro360.sales_delivery_schedules(tenant_id, status);
create index if not exists ix_sales_delivery_schedules_date on agro360.sales_delivery_schedules(tenant_id, planned_date);
create index if not exists ix_sales_delivery_schedule_items_order_item on agro360.sales_delivery_schedule_items(tenant_id, order_item_id);

alter table agro360.sales_delivery_schedules enable row level security;
alter table agro360.sales_delivery_schedules force row level security;
drop policy if exists tenant_isolation on agro360.sales_delivery_schedules;
create policy tenant_isolation on agro360.sales_delivery_schedules
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

alter table agro360.sales_delivery_schedule_items enable row level security;
alter table agro360.sales_delivery_schedule_items force row level security;
drop policy if exists tenant_isolation on agro360.sales_delivery_schedule_items;
create policy tenant_isolation on agro360.sales_delivery_schedule_items
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

alter table agro360.sales_delivery_schedule_revisions enable row level security;
alter table agro360.sales_delivery_schedule_revisions force row level security;
drop policy if exists tenant_isolation on agro360.sales_delivery_schedule_revisions;
create policy tenant_isolation on agro360.sales_delivery_schedule_revisions
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.6.0', 'Programação de entregas e compromissos operacionais do pedido', now())
on conflict(version) do nothing;

commit;
