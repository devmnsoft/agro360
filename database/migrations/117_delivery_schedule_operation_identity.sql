begin;

-- Identidade das operações de reprogramação e cancelamento.
-- A criação já persiste request_hash em sales_delivery_schedules (11.6.0).
-- Sem backfill de ator: linhas antigas permanecem com hash nulo e não são reescritas.

create table if not exists agro360.sales_delivery_schedule_operations(
    id uuid primary key,
    tenant_id uuid not null,
    schedule_id uuid not null,
    operation varchar(32) not null,
    idempotency_key varchar(120) not null,
    request_hash char(64) not null,
    result_version bigint,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique(tenant_id, id),
    unique(tenant_id, operation, schedule_id, idempotency_key),
    foreign key(tenant_id, schedule_id) references agro360.sales_delivery_schedules(tenant_id, id),
    check(operation in ('RESCHEDULE', 'CANCEL')),
    check(nullif(trim(idempotency_key), '') is not null),
    check(request_hash ~ '^[0-9a-f]{64}$')
);

create unique index if not exists ux_sales_delivery_schedules_idempotency
    on agro360.sales_delivery_schedules(tenant_id, idempotency_key)
    where idempotency_key is not null and deleted_at is null;

do $$ begin
    perform agro360.platform_enable_tenant_rls('agro360.sales_delivery_schedule_operations');
end $$;

insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.7.0', 'Idempotência de reprogramação e cancelamento de compromissos', now())
on conflict(version) do nothing;

commit;
