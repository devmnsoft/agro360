begin;

-- Sprint 49 references SLA policies and automation rules by (tenant_id,id)
-- without publishing matching parent keys. Pre-create only those parents with
-- their published shape and tenant key.
create schema if not exists automation;

create table if not exists operations.sla_policies(
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null references tenancy.tenants(id),
    name varchar(160) not null, module varchar(50) not null, occurrence_type varchar(60) not null,
    priority varchar(16) not null check(priority in('LOW','MEDIUM','HIGH','CRITICAL')),
    due_minutes int not null check(due_minutes>0), initial_responsible_id uuid, manager_id uuid,
    timezone varchar(80) not null default 'America/Sao_Paulo', overdue_action varchar(40) not null,
    status varchar(20) not null check(status in('ACTIVE','INACTIVE')),
    created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
    created_by uuid not null references identity.users(id), updated_by uuid references identity.users(id),
    deleted_at timestamptz, unique(tenant_id,id), unique(tenant_id,module,occurrence_type,priority)
);

create table if not exists automation.rules(
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null references tenancy.tenants(id),
    name varchar(160) not null, trigger_type varchar(40) not null, condition_json jsonb not null,
    action_type varchar(40) not null, action_config jsonb not null default '{}', source_module varchar(50) not null,
    target_module varchar(50), status varchar(20) not null check(status in('DRAFT','ACTIVE','INACTIVE')),
    critical boolean not null default false, created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(), created_by uuid not null references identity.users(id),
    updated_by uuid references identity.users(id), deleted_at timestamptz,
    unique(tenant_id,id), unique(tenant_id,name), check(status<>'ACTIVE' or condition_json<>'{}'::jsonb)
);

insert into platform.schema_versions(version,description,installed_at)
values('4.8.1','Bridge das chaves compostas de workflow para a Sprint 49',now())
on conflict(version) do update set description=excluded.description;

commit;
