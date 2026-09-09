begin;

-- Sprint 47 references the earlier SaaS/support names while Sprint 45 stores
-- plans in platform_saas_plans. Preserve the IDs needed by historical FKs and
-- materialize the support article contract before the published ALTER/INDEX.
create schema if not exists saas;
create schema if not exists support;

create table if not exists saas.plans(
    id uuid primary key,
    code varchar(50) not null unique,
    name varchar(120) not null,
    active boolean not null default true,
    created_at timestamptz not null default now()
);

insert into saas.plans(id,code,name,active,created_at)
select id,code,name,active,created_at from platform_saas_plans
on conflict(id) do update set code=excluded.code,name=excluded.name,active=excluded.active;

create table if not exists support.knowledge_articles(
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid references tenancy.tenants(id),
    title varchar(200) not null,
    content text not null,
    status varchar(20) not null default 'DRAFT',
    module varchar(60) not null default 'PLATFORM',
    audience varchar(30) not null default 'TENANT',
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid not null,
    updated_by uuid not null,
    deleted_at timestamptz,
    unique(tenant_id,id)
);

insert into platform.schema_versions(version,description,installed_at)
values('4.6.1','Bridge dos contratos SaaS e suporte esperados pela Sprint 47',now())
on conflict(version) do update set description=excluded.description;

commit;
