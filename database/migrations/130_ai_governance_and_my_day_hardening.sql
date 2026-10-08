-- Migration 130: Governança de IA com hash de payload de idempotência, índices de recuperação de reservas e hardening de MyDay.
-- Schema 11.20.0.
set local search_path to agro360, public;

-- 1. Coluna de Hash de Payload para detecção de conflitos de idempotência
alter table agro360.ai_executions
    add column if not exists payload_hash varchar(64);

-- 2. Índice para recuperação rápida de reservas abandonadas
create index if not exists ix_ai_executions_abandoned
    on agro360.ai_executions(tenant_id, status, created_at)
    where status in ('RESERVED', 'IN_PROGRESS');

-- 3. Concessão de Privilégios ao papel da aplicação agro360_app
do $$
begin
    if exists (select 1 from pg_roles where rolname = 'agro360_app') then
        execute 'grant select, insert, update, delete on agro360.tenant_ai_quotas to agro360_app';
        execute 'grant select, insert, update, delete on agro360.ai_executions to agro360_app';
    end if;
end $$;

-- 4. Atualização da Versão Canônica do Schema
insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.20.0', 'Governança de IA com validação de payload de idempotência e recuperação de reservas', now())
on conflict (version) do update set description = excluded.description;
