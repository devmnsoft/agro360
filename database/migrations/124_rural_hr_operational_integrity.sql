-- Migration 124: concorrência operacional de RH, decisão das revisões e fuso civil.
-- Schema 11.14.0. Não altera checksum de migration aplicada.
-- A suspeita de documento não prova falsidade e não substitui o cadastro.
-- A composição atual de um plano não prova, sozinha, concessão indevida.
set local search_path to agro360, public;

alter table agro360.rural_hr_time_entries
  add column if not exists plan_overrun boolean not null default false;
comment on column agro360.rural_hr_time_entries.plan_overrun is
  'Verdadeiro quando a saída passa do fim planejado ou quando a correção encontra início legado fora do plano. A alocação e os fatos executados não são reescritos.';

alter table agro360.rural_hr_tariffs
  add column if not exists close_reason varchar(1000),
  add column if not exists closed_by uuid,
  add column if not exists closed_at timestamptz;
comment on column agro360.rural_hr_tariffs.close_reason is
  'Encerramento de vigência. O valor já gravado na jornada permanece; a tarifa deixa de valer após valid_to.';

alter table agro360.rural_hr_data_reviews
  add column if not exists resolution_reason varchar(1000),
  add column if not exists resolved_by uuid,
  add column if not exists resolved_at timestamptz,
  add column if not exists before_state jsonb,
  add column if not exists after_state jsonb;

alter table agro360.saas_plan_module_reviews
  add column if not exists resolution_reason varchar(1000),
  add column if not exists resolved_by uuid,
  add column if not exists resolved_at timestamptz,
  add column if not exists before_modules varchar[],
  add column if not exists after_modules varchar[];

do $$
declare constraint_name text;
begin
  for constraint_name in
    select c.conname
    from pg_constraint c
    where c.conrelid = 'agro360.rural_hr_data_reviews'::regclass
      and c.contype = 'c'
      and pg_get_constraintdef(c.oid) ilike '%resolution%'
  loop
    execute format('alter table agro360.rural_hr_data_reviews drop constraint %I', constraint_name);
  end loop;
  for constraint_name in
    select c.conname
    from pg_constraint c
    where c.conrelid = 'agro360.saas_plan_module_reviews'::regclass
      and c.contype = 'c'
      and pg_get_constraintdef(c.oid) ilike '%resolution%'
  loop
    execute format('alter table agro360.saas_plan_module_reviews drop constraint %I', constraint_name);
  end loop;
end $$;

alter table agro360.rural_hr_data_reviews
  add constraint ck_rural_hr_data_review_decision check (
    resolution in ('OPEN', 'CONFIRMED_REAL', 'CORRECTED')
    and (
      resolution = 'OPEN'
      or (length(trim(resolution_reason)) >= 5 and resolved_by is not null and resolved_at is not null and before_state is not null and after_state is not null)
    )
  );
alter table agro360.saas_plan_module_reviews
  add constraint ck_saas_plan_module_review_decision check (
    resolution in ('OPEN', 'KEEP', 'REMOVE')
    and (
      resolution = 'OPEN'
      or (length(trim(resolution_reason)) >= 5 and resolved_by is not null and resolved_at is not null and before_modules is not null and after_modules is not null)
    )
  );

comment on table agro360.rural_hr_data_reviews is
  'Fila de revisão de dados legados. SUSPECTED_SYNTHETIC_DOCUMENT é uma suspeita, não prova de documento falso. A decisão autorizada registra motivo, ator e o estado antes/depois, sem substituir nem apagar a origem.';
comment on table agro360.saas_plan_module_reviews is
  'Revisão comercial do acréscimo da migration 122. A composição atual não prova concessão indevida. KEEP preserva os módulos. REMOVE retira somente rural-hr ou verticals, por decisão explícita.';

create or replace function agro360.validate_pending_constraints()
returns table(constraint_name text, outcome text)
language plpgsql
security definer
set search_path = agro360, pg_temp
as $$
declare
  item record;
begin
  for item in
    select c.conname, c.conrelid::regclass as relation_name
    from pg_constraint c
    join pg_namespace n on n.oid = c.connamespace
    where n.nspname = 'agro360' and not c.convalidated
    order by c.conname
  loop
    begin
      execute format('alter table %s validate constraint %I', item.relation_name, item.conname);
      constraint_name := item.conname;
      outcome := 'VALIDATED';
      return next;
    exception when others then
      constraint_name := item.conname;
      outcome := sqlstate || ' ' || sqlerrm;
      return next;
    end;
  end loop;
end $$;

comment on function agro360.validate_pending_constraints() is
  'Valida constraints NOT VALID sem alterar linhas. Falha de uma constraint permanece registrada no retorno e não apaga o dado.';

revoke all on function agro360.validate_pending_constraints() from public;

do $$
begin
  if exists (select 1 from pg_roles where rolname = 'agro360_app') then
    grant update on table agro360.saas_plan_module_reviews to agro360_app;
    grant execute on function agro360.validate_pending_constraints() to agro360_app;
  end if;
end $$;

-- Tabelas com tenant_id criadas nas sprints comercial/fiscal sem a política canônica.
-- A aplicação já filtra tenant_id; sem RLS o papel agro360_app ainda lia a linha de outro cliente.
select agro360.platform_enable_tenant_rls('agro360.fiscal_profiles');
select agro360.platform_enable_tenant_rls('agro360.fiscal_provider_configs');
select agro360.platform_enable_tenant_rls('agro360.fiscal_provider_attempts');
select agro360.platform_enable_tenant_rls('agro360.fiscal_correction_letters');
select agro360.platform_enable_tenant_rls('agro360.commercial_deliveries');
select agro360.platform_enable_tenant_rls('agro360.commercial_billing_forecasts');
select agro360.platform_enable_tenant_rls('agro360.commercial_events');

insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.14.0', 'Integridade operacional de RH: sobreposição, revisão autorizada e fuso civil (AG-HR-OP-003)', now())
on conflict (version) do update set description = excluded.description;
