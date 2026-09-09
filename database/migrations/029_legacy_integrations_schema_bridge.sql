begin;

-- Sprint 30 was published against the plural schema and two compatibility
-- tables that were never created by the earlier incremental chain. Keep the
-- published file immutable and materialize only the contract it expects.
create schema if not exists integrations;

create table if not exists integrations.integration_connectors(
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenancy.tenants(id),
    name varchar(120) not null,
    type varchar(32) not null check(type in('ERP','FISCAL','LOGISTICS','FINANCIAL','MARKETPLACE','BI','COOPERATIVE','EXTERNAL_PORTAL','GOVERNMENT','WEBHOOK','EXTERNAL_API','OTHER')),
    provider varchar(80),
    endpoint_url varchar(1000),
    authentication_type varchar(30) not null default 'NONE',
    status varchar(30) not null default 'NOT_CONFIGURED' check(status in('NOT_CONFIGURED','ACTIVE','INACTIVE','ERROR')),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid not null,
    updated_by uuid not null,
    deleted_at timestamptz,
    unique(tenant_id,id),
    unique(tenant_id,name),
    check(endpoint_url is null or endpoint_url ~ '^https?://')
);

create table if not exists integrations.integration_outbox(
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null,
    connector_id uuid not null,
    idempotency_key varchar(160) not null,
    event_type varchar(100) not null,
    payload jsonb not null,
    status varchar(20) not null default 'PENDING' check(status in('PENDING','PROCESSING','SENT','FAILED','CANCELLED')),
    attempts int not null default 0 check(attempts between 0 and 20),
    available_at timestamptz not null default now(),
    last_error varchar(1000),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid not null,
    updated_by uuid not null,
    unique(tenant_id,id),
    unique(tenant_id,connector_id,idempotency_key),
    foreign key(tenant_id,connector_id) references integrations.integration_connectors(tenant_id,id)
);

create table if not exists integrations.api_keys(
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenancy.tenants(id),
    name varchar(120) not null,
    key_hash char(64) not null unique,
    status varchar(20) not null default 'ACTIVE',
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique(tenant_id,id)
);

create table if not exists integrations.webhook_events(
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references tenancy.tenants(id),
    event_type varchar(100) not null,
    payload jsonb not null default '{}',
    status varchar(20) not null default 'PENDING',
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique(tenant_id,id)
);

insert into platform.schema_versions(version,description,installed_at)
values('2.9.1','Bridge de compatibilidade do schema integrations para a Sprint 30',now())
on conflict(version) do update set description=excluded.description;

commit;
