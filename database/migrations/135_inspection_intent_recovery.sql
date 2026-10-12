-- Migration 135: recuperação automática de intents de inspeção por evento operacional.
-- Adiciona contagem de tentativas com backoff, lease/claim para processamento concorrente,
-- dead-letter com motivo (error_code/last_error), e índice de varredura de itens vencidos.
-- Também adiciona os status RESOLVED (origem saiu do estado pendente sem nova ação) e
-- DEAD_LETTERED (falha permanente com motivo e ação operacional: re-disparo manual auditado).
-- A coluna error_code corrige a consulta de genealogia da safra que já referenciava essa coluna.
-- Alterações aditivas: intents históricos mantêm attempts=0 e next_attempt_at=now().
begin;

alter table agro360.quality_inspection_event_intents
  add column if not exists attempts int not null default 0,
  add column if not exists last_error varchar(2000),
  add column if not exists error_code varchar(80),
  add column if not exists next_attempt_at timestamptz not null default now(),
  add column if not exists claimed_at timestamptz,
  add column if not exists claimed_by varchar(100),
  add column if not exists lease_expires_at timestamptz,
  add column if not exists dead_lettered_at timestamptz;

-- Solta o CHECK antigo somente quando ele restringe os 5 status originais (idempotente e
-- independente do nome automático gerado pelo PostgreSQL na criação original — migration 093).
do $$
declare c record;
begin
  for c in select conname from pg_constraint
           where conrelid = 'agro360.quality_inspection_event_intents'::regclass and contype = 'c'
             and pg_get_constraintdef(oid) like '%status in%''PENDING'''
             and pg_get_constraintdef(oid) not like '%DEAD_LETTERED%'
  loop execute format('alter table agro360.quality_inspection_event_intents drop constraint %I', c.conname);
  end loop;
  if not exists (select 1 from pg_constraint
                 where conrelid = 'agro360.quality_inspection_event_intents'::regclass
                   and conname = 'ck_quality_inspection_event_intents_status') then
    execute 'alter table agro360.quality_inspection_event_intents add constraint ck_quality_inspection_event_intents_status check (status in (''PENDING'', ''STARTED'', ''AMBIGUOUS'', ''PENDING_MODEL'', ''SKIPPED_NO_ACTOR'', ''RESOLVED'', ''DEAD_LETTERED''))';
  end if;
end $$;

do $$
begin
  if not exists (select 1 from pg_constraint
                 where conrelid = 'agro360.quality_inspection_event_intents'::regclass
                   and conname = 'ck_quality_inspection_event_intents_attempts') then
    execute 'alter table agro360.quality_inspection_event_intents add constraint ck_quality_inspection_event_intents_attempts check (attempts >= 0)';
  end if;
end $$;

create index if not exists ix_quality_inspection_event_intents_due
  on agro360.quality_inspection_event_intents(tenant_id, next_attempt_at)
  where status in ('PENDING','PENDING_MODEL') and deleted_at is null;

create index if not exists ix_quality_inspection_event_intents_dead_letter
  on agro360.quality_inspection_event_intents(tenant_id, created_at desc)
  where status = 'DEAD_LETTERED' and deleted_at is null;

insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.25.0', 'Recuperação automática de intents de inspeção: tentativas/backoff, claim/lease, dead-letter com motivo, varredura de origens pendentes e re-disparo manual auditado', now())
on conflict(version) do update set description = excluded.description;

commit;
