-- Migration 129: Tabelas de governança de IA, quotas atômicas por competência e auditoria de execuções.
-- Schema 11.19.0.
set local search_path to agro360, public;

-- 1. Tabela de Quotas de IA por Tenant, Caso de Uso e Período
create table if not exists agro360.tenant_ai_quotas (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references agro360.platform_tenants(id),
    use_case varchar(60) not null,
    period_start date not null,
    period_end date not null,
    max_tokens bigint not null check (max_tokens >= 0),
    reserved_tokens bigint not null default 0 check (reserved_tokens >= 0),
    consumed_tokens bigint not null default 0 check (consumed_tokens >= 0),
    active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    constraint ck_tenant_ai_quotas_period check (period_end >= period_start)
);

create unique index if not exists uq_tenant_ai_quotas_period
    on agro360.tenant_ai_quotas(tenant_id, use_case, period_start, period_end);

create index if not exists ix_tenant_ai_quotas_lookup
    on agro360.tenant_ai_quotas(tenant_id, use_case, active, period_start, period_end);

alter table agro360.tenant_ai_quotas enable row level security;
alter table agro360.tenant_ai_quotas force row level security;

drop policy if exists tenant_isolation on agro360.tenant_ai_quotas;
create policy tenant_isolation on agro360.tenant_ai_quotas
    using (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    with check (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

-- 2. Tabela de Execuções e Auditoria de IA
create table if not exists agro360.ai_executions (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references agro360.platform_tenants(id),
    user_id uuid not null references agro360.identity_users(id),
    use_case varchar(60) not null,
    idempotency_key varchar(160),
    attempt_number int not null default 1,
    status varchar(30) not null default 'RESERVED' check (status in ('RESERVED', 'IN_PROGRESS', 'COMPLETED', 'FAILED', 'CANCELLED', 'RELEASED')),
    provider varchar(60),
    model varchar(80),
    reserved_tokens int not null default 0 check (reserved_tokens >= 0),
    prompt_tokens int not null default 0 check (prompt_tokens >= 0),
    completion_tokens int not null default 0 check (completion_tokens >= 0),
    total_tokens int not null default 0 check (total_tokens >= 0),
    token_confidence varchar(20) not null default 'EXACT' check (token_confidence in ('EXACT', 'ESTIMATED', 'UNKNOWN')),
    duration_ms numeric(12, 2) not null default 0,
    error_message text,
    occurred_at timestamptz not null default now(),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now()
);

create index if not exists ix_ai_executions_tenant_user
    on agro360.ai_executions(tenant_id, user_id, occurred_at desc);

create index if not exists ix_ai_executions_tenant_status
    on agro360.ai_executions(tenant_id, use_case, status);

create unique index if not exists ux_ai_executions_idempotency
    on agro360.ai_executions(tenant_id, use_case, idempotency_key)
    where idempotency_key is not null;

alter table agro360.ai_executions enable row level security;
alter table agro360.ai_executions force row level security;

drop policy if exists tenant_isolation on agro360.ai_executions;
create policy tenant_isolation on agro360.ai_executions
    using (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    with check (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

-- 3. Concessão de Privilégios ao papel da aplicação agro360_app
do $$
begin
    if exists (select 1 from pg_roles where rolname = 'agro360_app') then
        execute 'grant select, insert, update, delete on agro360.tenant_ai_quotas to agro360_app';
        execute 'grant select, insert, update, delete on agro360.ai_executions to agro360_app';
    end if;
end $$;

-- 4. Seed de Quotas iniciais vigentes para tenants ativos
insert into agro360.tenant_ai_quotas (id, tenant_id, use_case, period_start, period_end, max_tokens, reserved_tokens, consumed_tokens, active, created_at, updated_at)
select
    gen_random_uuid(),
    t.id,
    'stock_assistant',
    date_trunc('month', current_date)::date,
    (date_trunc('month', current_date) + interval '1 month - 1 day')::date,
    500000,
    0,
    0,
    true,
    now(),
    now()
from agro360.platform_tenants t
where t.deleted_at is null
on conflict (tenant_id, use_case, period_start, period_end) do nothing;

insert into agro360.tenant_ai_quotas (id, tenant_id, use_case, period_start, period_end, max_tokens, reserved_tokens, consumed_tokens, active, created_at, updated_at)
select
    gen_random_uuid(),
    t.id,
    'general_intelligence',
    date_trunc('month', current_date)::date,
    (date_trunc('month', current_date) + interval '1 month - 1 day')::date,
    1000000,
    0,
    0,
    true,
    now(),
    now()
from agro360.platform_tenants t
where t.deleted_at is null
on conflict (tenant_id, use_case, period_start, period_end) do nothing;

-- 5. Atualização da Versão Canônica do Schema
insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.19.0', 'Tabelas de governança de IA, quotas atômicas por competência e auditoria de execuções', now())
on conflict (version) do update set description = excluded.description;
