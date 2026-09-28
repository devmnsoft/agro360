-- AG-SaaS-ADM-003 - sessao persistida e revogavel de suporte global assistido
begin;
set local search_path to agro360, public;

create table if not exists agro360.saas_support_sessions(
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    actor_id uuid not null references agro360.identity_users(id),
    reason varchar(1000) not null check(length(trim(reason)) >= 5),
    scope varchar(100) not null default 'SUPPORT_READ_OPERATIONAL',
    started_at timestamptz not null default now(),
    expires_at timestamptz not null,
    ended_at timestamptz,
    ended_by uuid references agro360.identity_users(id),
    end_reason varchar(1000),
    created_at timestamptz not null default now(),
    check(expires_at > started_at),
    check((ended_at is null and ended_by is null and end_reason is null) or
          (ended_at is not null and ended_by is not null and length(trim(end_reason)) >= 5))
);

create index if not exists ix_saas_support_sessions_active
    on agro360.saas_support_sessions(tenant_id, actor_id)
    where ended_at is null;

create index if not exists ix_saas_support_sessions_history
    on agro360.saas_support_sessions(tenant_id, started_at desc);

insert into agro360.platform_schema_versions(version,description,installed_at)
values('11.3.0','Sessão persistida e revogável de suporte global assistido',now())
on conflict(version) do nothing;

commit;
