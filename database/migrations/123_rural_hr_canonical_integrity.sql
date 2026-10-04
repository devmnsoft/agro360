-- Migration 123: fonte canônica de pessoa/jornada, integridade de vínculos e correção comercial da 122.
-- Schema 11.13.0. Não altera checksum de migration aplicada.
-- Pessoa canônica: agro360.rural_hr_people.
-- Jornada canônica: agro360.rural_hr_time_entries.
-- rural_hr_records PERSON/TIME_ENTRY é projeção sincronizada na mesma transação
-- (canonical_table + canonical_id). Não é uma segunda fonte operacional.
set local search_path to agro360, public;

alter table agro360.rural_hr_records add column if not exists canonical_table varchar(64);
alter table agro360.rural_hr_records add column if not exists canonical_id uuid;
comment on column agro360.rural_hr_records.canonical_table is
  'Projeção persistida. PERSON espelha rural_hr_people; TIME_ENTRY espelha rural_hr_time_entries. O estado operacional fica só na tabela canônica e é gravado na mesma transação.';

alter table agro360.rural_hr_records drop constraint if exists ck_rural_hr_projection;
alter table agro360.rural_hr_records add constraint ck_rural_hr_projection check (
  (kind not in ('PERSON', 'TIME_ENTRY'))
  or (canonical_table is null and canonical_id is null)
  or (kind = 'PERSON' and canonical_table = 'rural_hr_people' and canonical_id is not null)
  or (kind = 'TIME_ENTRY' and canonical_table = 'rural_hr_time_entries' and canonical_id is not null)
);

alter table agro360.rural_hr_time_entries add column if not exists allocation_id uuid;
alter table agro360.rural_hr_time_entries add column if not exists order_id uuid;
alter table agro360.rural_hr_time_entries add column if not exists season_id uuid;
alter table agro360.rural_hr_time_entries add column if not exists plot_id uuid;
alter table agro360.rural_hr_time_entries add column if not exists hours_worked numeric(18,2);
alter table agro360.rural_hr_time_entries add column if not exists piece_quantity numeric(18,4);
alter table agro360.rural_hr_time_entries add column if not exists review_status varchar(16) not null default 'PENDING';
alter table agro360.rural_hr_time_entries add column if not exists reviewed_by uuid;
alter table agro360.rural_hr_time_entries add column if not exists reviewed_at timestamptz;
alter table agro360.rural_hr_time_entries add column if not exists tariff_id uuid;
alter table agro360.rural_hr_time_entries add column if not exists rate_type varchar(30);
alter table agro360.rural_hr_time_entries add column if not exists rate_value numeric(18,4);
alter table agro360.rural_hr_time_entries add column if not exists cost_amount numeric(18,4);
alter table agro360.rural_hr_time_entries add column if not exists cost_status varchar(16);
alter table agro360.rural_hr_time_entries add column if not exists cost_block varchar(200);
alter table agro360.rural_hr_time_entries add column if not exists cost_currency char(3) not null default 'BRL';
alter table agro360.rural_hr_time_entries add column if not exists rounding_rule varchar(80) not null default '4 casas; AwayFromZero';
alter table agro360.rural_hr_time_entries add column if not exists cost_entry_id uuid;
alter table agro360.rural_hr_time_entries add column if not exists allocation_batch_id uuid;
alter table agro360.rural_hr_time_entries add column if not exists idempotency_key varchar(160);
alter table agro360.rural_hr_time_entries add column if not exists version bigint not null default 1;

alter table agro360.rural_hr_time_entries drop constraint if exists ck_rural_hr_time_review;
alter table agro360.rural_hr_time_entries add constraint ck_rural_hr_time_review check (
  review_status in ('PENDING', 'CONFIRMED')
  and (reviewed_at is null) = (reviewed_by is null)
  and (piece_quantity is null or piece_quantity >= 0)
  and (hours_worked is null or hours_worked > 0)
  and (cost_status is null or cost_status in ('CALCULATED', 'UNAVAILABLE'))
  and (cost_amount is null or cost_amount >= 0)
  and cost_currency = 'BRL'
  and (cost_status is distinct from 'CALCULATED' or cost_amount is not null)
  and (cost_status is distinct from 'UNAVAILABLE' or cost_amount is null)
);
create unique index if not exists ux_rural_hr_time_idempotency
  on agro360.rural_hr_time_entries (tenant_id, idempotency_key)
  where idempotency_key is not null;

alter table agro360.rural_hr_tariffs add column if not exists currency char(3) not null default 'BRL';
alter table agro360.rural_hr_tariffs add column if not exists unit_code varchar(16) not null default 'HOUR';
alter table agro360.rural_hr_tariffs add column if not exists rounding_scale smallint not null default 4;
alter table agro360.rural_hr_tariffs add column if not exists updated_at timestamptz;
alter table agro360.rural_hr_tariffs add column if not exists updated_by uuid;
update agro360.rural_hr_tariffs set unit_code = case rate_type
  when 'HOURLY' then 'HOUR' when 'DAILY' then 'DAY' when 'PIECEWORK' then 'UNIT' when 'FIXED' then 'CONTRACT' else 'HOUR' end
where unit_code is distinct from case rate_type
  when 'HOURLY' then 'HOUR' when 'DAILY' then 'DAY' when 'PIECEWORK' then 'UNIT' when 'FIXED' then 'CONTRACT' else 'HOUR' end;
alter table agro360.rural_hr_tariffs drop constraint if exists ck_rural_hr_tariff_limits;
alter table agro360.rural_hr_tariffs add constraint ck_rural_hr_tariff_limits check (
  currency = 'BRL'
  and rate_value >= 0 and rate_value <= 999999.9999
  and rounding_scale = 4
  and (
    (rate_type = 'HOURLY' and unit_code = 'HOUR')
    or (rate_type = 'DAILY' and unit_code = 'DAY')
    or (rate_type = 'PIECEWORK' and unit_code = 'UNIT')
    or (rate_type = 'FIXED' and unit_code = 'CONTRACT')
  )
);

do $$
declare constraint_name text;
begin
  select c.conname into constraint_name
  from pg_constraint c
  join pg_attribute a on a.attrelid = c.conrelid and a.attnum = any (c.conkey)
  where c.conrelid = 'agro360.rural_hr_tariffs'::regclass and c.contype = 'f' and a.attname = 'role_id'
  limit 1;
  if constraint_name is not null then
    execute format('alter table agro360.rural_hr_tariffs drop constraint %I', constraint_name);
  end if;
end $$;
alter table agro360.rural_hr_tariffs drop constraint if exists fk_rural_hr_tariffs_tenant_role;
alter table agro360.rural_hr_tariffs
  add constraint fk_rural_hr_tariffs_tenant_role
  foreign key (tenant_id, role_id) references agro360.rural_hr_roles (tenant_id, id) not valid;

do $$
begin
  if not exists (
    select 1 from agro360.rural_hr_records r
    where r.season_id is not null and not exists (
      select 1 from agro360.agriculture_seasons s where s.tenant_id = r.tenant_id and s.id = r.season_id)
  ) then
    alter table agro360.rural_hr_records drop constraint if exists fk_rural_hr_records_season;
    alter table agro360.rural_hr_records
      add constraint fk_rural_hr_records_season
      foreign key (tenant_id, season_id) references agro360.agriculture_seasons (tenant_id, id);
  end if;
  if not exists (
    select 1 from agro360.rural_hr_records r
    where r.plot_id is not null and not exists (
      select 1 from agro360.geo_fields f where f.tenant_id = r.tenant_id and f.id = r.plot_id)
  ) then
    alter table agro360.rural_hr_records drop constraint if exists fk_rural_hr_records_plot;
    alter table agro360.rural_hr_records
      add constraint fk_rural_hr_records_plot
      foreign key (tenant_id, plot_id) references agro360.geo_fields (tenant_id, id);
  end if;
  if not exists (
    select 1 from agro360.rural_hr_records r
    where r.order_id is not null and not exists (
      select 1 from agro360.agriculture_records o where o.tenant_id = r.tenant_id and o.id = r.order_id)
  ) then
    alter table agro360.rural_hr_records drop constraint if exists fk_rural_hr_records_order;
    alter table agro360.rural_hr_records
      add constraint fk_rural_hr_records_order
      foreign key (tenant_id, order_id) references agro360.agriculture_records (tenant_id, id);
  end if;
end $$;

alter table agro360.rural_hr_time_entries drop constraint if exists fk_rural_hr_time_allocation;
alter table agro360.rural_hr_time_entries
  add constraint fk_rural_hr_time_allocation
  foreign key (tenant_id, allocation_id) references agro360.rural_hr_records (tenant_id, id) not valid;
alter table agro360.rural_hr_time_entries drop constraint if exists fk_rural_hr_time_season;
alter table agro360.rural_hr_time_entries
  add constraint fk_rural_hr_time_season
  foreign key (tenant_id, season_id) references agro360.agriculture_seasons (tenant_id, id) not valid;
alter table agro360.rural_hr_time_entries drop constraint if exists fk_rural_hr_time_plot;
alter table agro360.rural_hr_time_entries
  add constraint fk_rural_hr_time_plot
  foreign key (tenant_id, plot_id) references agro360.geo_fields (tenant_id, id) not valid;
alter table agro360.rural_hr_time_entries drop constraint if exists fk_rural_hr_time_order;
alter table agro360.rural_hr_time_entries
  add constraint fk_rural_hr_time_order
  foreign key (tenant_id, order_id) references agro360.agriculture_records (tenant_id, id) not valid;
alter table agro360.rural_hr_time_entries drop constraint if exists fk_rural_hr_time_cost_entry;
alter table agro360.rural_hr_time_entries
  add constraint fk_rural_hr_time_cost_entry
  foreign key (tenant_id, cost_entry_id) references agro360.cost_management_entries (tenant_id, id) not valid;
alter table agro360.rural_hr_time_entries drop constraint if exists fk_rural_hr_time_cost_batch;
alter table agro360.rural_hr_time_entries
  add constraint fk_rural_hr_time_cost_batch
  foreign key (tenant_id, allocation_batch_id) references agro360.cost_allocation_batches (tenant_id, id) not valid;

alter table agro360.field_work_order_resources add column if not exists hr_allocation_id uuid;
alter table agro360.field_work_order_resources drop constraint if exists fk_field_resource_hr_allocation;
alter table agro360.field_work_order_resources
  add constraint fk_field_resource_hr_allocation
  foreign key (tenant_id, hr_allocation_id) references agro360.rural_hr_records (tenant_id, id) not valid;

create table if not exists agro360.rural_hr_time_corrections (
  id uuid primary key,
  tenant_id uuid not null references agro360.tenancy_tenants(id),
  time_entry_id uuid not null,
  previous_started_at timestamptz not null,
  previous_ended_at timestamptz,
  previous_break_minutes int not null,
  previous_hours numeric(18,2),
  previous_cost_amount numeric(18,4),
  new_ended_at timestamptz,
  new_break_minutes int not null,
  new_hours numeric(18,2),
  new_cost_amount numeric(18,4),
  justification varchar(1000) not null,
  created_at timestamptz not null default now(),
  created_by uuid not null,
  unique (tenant_id, id),
  foreign key (tenant_id, time_entry_id) references agro360.rural_hr_time_entries (tenant_id, id),
  check (length(trim(justification)) >= 5),
  check (new_break_minutes between 0 and 1440)
);
create index if not exists ix_rural_hr_time_corrections_entry
  on agro360.rural_hr_time_corrections (tenant_id, time_entry_id, created_at);

create table if not exists agro360.rural_hr_record_events (
  id uuid primary key,
  tenant_id uuid not null references agro360.tenancy_tenants(id),
  record_id uuid not null,
  event_type varchar(40) not null,
  reason varchar(1000),
  snapshot jsonb not null,
  created_at timestamptz not null default now(),
  created_by uuid not null,
  unique (tenant_id, id),
  foreign key (tenant_id, record_id) references agro360.rural_hr_records (tenant_id, id)
);

create table if not exists agro360.rural_hr_command_replays (
  tenant_id uuid not null references agro360.tenancy_tenants(id),
  command_name varchar(60) not null,
  idempotency_key varchar(160) not null,
  entity_id uuid not null,
  request_hash char(64) not null,
  created_at timestamptz not null default now(),
  primary key (tenant_id, command_name, idempotency_key)
);

create table if not exists agro360.rural_hr_data_reviews (
  id uuid primary key default gen_random_uuid(),
  tenant_id uuid not null references agro360.tenancy_tenants(id),
  entity_table varchar(64) not null,
  entity_id uuid not null,
  reason_code varchar(80) not null,
  detail varchar(500) not null,
  resolution varchar(20) not null default 'OPEN' check (resolution in ('OPEN', 'CONFIRMED_REAL', 'CORRECTED')),
  detected_at timestamptz not null default now(),
  unique (tenant_id, entity_table, entity_id, reason_code)
);
comment on table agro360.rural_hr_data_reviews is
  'Fila rastreável dos registros possivelmente produzidos pelos fallbacks da entrega anterior. A migration não apaga nem reescreve o dado de origem.';

insert into agro360.rural_hr_data_reviews (tenant_id, entity_table, entity_id, reason_code, detail)
select p.tenant_id, 'rural_hr_people', p.id, 'SUSPECTED_SYNTHETIC_DOCUMENT',
  'Documento no formato 1########00 gerado por SaveGenericAsync, com projeção PERSON de mesmo id. Não corrigido automaticamente.'
from agro360.rural_hr_people p
where p.document ~ '^1[0-9]{8}00$'
  and exists (
    select 1 from agro360.rural_hr_records r
    where r.tenant_id = p.tenant_id and r.id = p.id and r.kind = 'PERSON')
on conflict do nothing;

insert into agro360.rural_hr_data_reviews (tenant_id, entity_table, entity_id, reason_code, detail)
select r.tenant_id, 'rural_hr_records', r.id, 'ORPHAN_PERSON_PROJECTION',
  'Registro PERSON sem pessoa canônica. A propriedade ou o cargo não foi persistido pelo fallback.'
from agro360.rural_hr_records r
where r.kind = 'PERSON'
  and not exists (select 1 from agro360.rural_hr_people p where p.tenant_id = r.tenant_id and p.id = r.id)
on conflict do nothing;

insert into agro360.rural_hr_data_reviews (tenant_id, entity_table, entity_id, reason_code, detail)
select r.tenant_id, 'rural_hr_records', r.id, 'ORPHAN_JOURNEY_PROJECTION',
  'Registro TIME_ENTRY sem jornada canônica em rural_hr_time_entries.'
from agro360.rural_hr_records r
where r.kind = 'TIME_ENTRY'
  and not exists (select 1 from agro360.rural_hr_time_entries e where e.tenant_id = r.tenant_id and e.id = r.id)
on conflict do nothing;

insert into agro360.rural_hr_data_reviews (tenant_id, entity_table, entity_id, reason_code, detail)
select r.tenant_id, 'rural_hr_roles', r.id, 'DUPLICATE_ROLE_NAME',
  'Mais de um cargo com o mesmo nome, ignorando maiúsculas. A unicidade canônica não foi imposta.'
from agro360.rural_hr_roles r
join (
  select tenant_id, lower(name) as name_key
  from agro360.rural_hr_roles
  group by tenant_id, lower(name)
  having count(*) > 1
) d on d.tenant_id = r.tenant_id and d.name_key = lower(r.name)
on conflict do nothing;

update agro360.rural_hr_records r
set canonical_table = 'rural_hr_people', canonical_id = r.id
where r.kind = 'PERSON' and r.canonical_id is null
  and exists (select 1 from agro360.rural_hr_people p where p.tenant_id = r.tenant_id and p.id = r.id);

update agro360.rural_hr_records r
set canonical_table = 'rural_hr_time_entries', canonical_id = r.id
where r.kind = 'TIME_ENTRY' and r.canonical_id is null
  and exists (select 1 from agro360.rural_hr_time_entries e where e.tenant_id = r.tenant_id and e.id = r.id);

insert into agro360.rural_hr_records (
  id, tenant_id, kind, name, person_id, property_id, status, role, canonical_table, canonical_id, created_by, updated_by)
select p.id, p.tenant_id, 'PERSON', p.name, p.id, p.property_id, p.status, roles.name, 'rural_hr_people', p.id, p.created_by, p.updated_by
from agro360.rural_hr_people p
left join agro360.rural_hr_roles roles on roles.tenant_id = p.tenant_id and roles.id = p.role_id
where not exists (select 1 from agro360.rural_hr_records r where r.id = p.id)
on conflict (id) do nothing;

insert into agro360.rural_hr_records (
  id, tenant_id, kind, name, person_id, team_id, property_id, resource_id,
  starts_at, started_at, ends_at, ended_at, break_minutes, activity_type, status,
  canonical_table, canonical_id, created_by, updated_by)
select e.id, e.tenant_id, 'TIME_ENTRY', coalesce(p.name, 'Jornada'), e.person_id, e.team_id, e.property_id, e.resource_id,
  e.started_at, e.started_at, e.ended_at, e.ended_at, e.break_minutes, e.activity_type, e.status,
  'rural_hr_time_entries', e.id, e.created_by, e.created_by
from agro360.rural_hr_time_entries e
left join agro360.rural_hr_people p on p.tenant_id = e.tenant_id and p.id = e.person_id
where not exists (select 1 from agro360.rural_hr_records r where r.id = e.id)
on conflict (id) do nothing;

create or replace function agro360.rural_hr_assert_projection() returns trigger
language plpgsql as $$
begin
  if new.kind = 'PERSON' then
    if new.canonical_id is null then
      raise exception 'Nova projeção de pessoa exige vínculo com rural_hr_people';
    end if;
    if not exists (
      select 1 from agro360.rural_hr_people p
      where p.tenant_id = new.tenant_id and p.id = new.canonical_id
        and p.name = new.name and p.status = new.status
    ) then
      raise exception 'Projeção de pessoa divergente da fonte canônica rural_hr_people';
    end if;
  elsif new.kind = 'TIME_ENTRY' then
    if new.canonical_id is null then
      raise exception 'Nova projeção de jornada exige vínculo com rural_hr_time_entries';
    end if;
    if not exists (
      select 1 from agro360.rural_hr_time_entries e
      where e.tenant_id = new.tenant_id and e.id = new.canonical_id and e.status = new.status
    ) then
      raise exception 'Projeção de jornada divergente da fonte canônica rural_hr_time_entries';
    end if;
  end if;
  return new;
end $$;

drop trigger if exists rural_hr_records_projection_chk on agro360.rural_hr_records;
create constraint trigger rural_hr_records_projection_chk
  after insert or update on agro360.rural_hr_records
  deferrable initially deferred
  for each row
  when (new.kind in ('PERSON', 'TIME_ENTRY') and new.canonical_id is not null)
  execute function agro360.rural_hr_assert_projection();

create or replace function agro360.rural_hr_projection_issues()
returns table(tenant_id uuid, kind text, record_id uuid, issue text)
language sql stable as $$
  select r.tenant_id, r.kind::text, r.id, 'ORPHAN_PERSON_PROJECTION'::text
  from agro360.rural_hr_records r
  where r.kind = 'PERSON'
    and not exists (
      select 1 from agro360.rural_hr_people p
      where p.tenant_id = r.tenant_id and p.id = coalesce(r.canonical_id, r.id))
  union all
  select r.tenant_id, 'TIME_ENTRY', r.id, 'ORPHAN_JOURNEY_PROJECTION'
  from agro360.rural_hr_records r
  where r.kind = 'TIME_ENTRY'
    and not exists (
      select 1 from agro360.rural_hr_time_entries e
      where e.tenant_id = r.tenant_id and e.id = coalesce(r.canonical_id, r.id))
  union all
  select p.tenant_id, 'PERSON', p.id, 'SUSPECTED_SYNTHETIC_DOCUMENT'
  from agro360.rural_hr_people p
  where p.document ~ '^1[0-9]{8}00$'
    and exists (
      select 1 from agro360.rural_hr_records r
      where r.tenant_id = p.tenant_id and r.id = p.id and r.kind = 'PERSON')
  union all
  select e.tenant_id, 'TIME_ENTRY', e.id, 'JOURNEY_STATUS_DIVERGENCE'
  from agro360.rural_hr_time_entries e
  join agro360.rural_hr_records r on r.tenant_id = e.tenant_id and r.canonical_id = e.id and r.kind = 'TIME_ENTRY'
  where r.status is distinct from e.status
$$;

do $$
begin
  if not exists (
    select 1 from agro360.rural_hr_roles
    group by tenant_id, lower(name) having count(*) > 1
  ) then
    execute 'create unique index if not exists ux_rural_hr_roles_name_ci on agro360.rural_hr_roles (tenant_id, lower(name))';
  end if;
end $$;

-- A migration 122 acrescentou rural-hr e verticals a todo plano que ainda não tinha rural-hr.
-- verticals permanece um módulo de plataforma planejado, não um direito contratado.
-- O catálogo semeado é restaurado somente quando o plano não tem outros módulos além desse acréscimo.
-- Planos com composição diferente ficam na fila de revisão e não perdem módulos.
create table if not exists agro360.saas_plan_module_reviews (
  id uuid primary key default gen_random_uuid(),
  plan_id uuid not null references agro360.saas_plans(id),
  module_code varchar(80) not null,
  reason_code varchar(80) not null,
  detail varchar(500) not null,
  resolution varchar(20) not null default 'OPEN' check (resolution in ('OPEN', 'KEEP', 'REMOVE')),
  detected_at timestamptz not null default now(),
  unique (plan_id, module_code, reason_code)
);
comment on table agro360.saas_plan_module_reviews is
  'Revisão comercial do acréscimo indiscriminado feito pela migration 122. Não revoga plano cuja composição não é exatamente o catálogo semeado mais rural-hr/verticals.';

update agro360.saas_plans p
set modules = v.modules, updated_at = now()
from (values
  ('Essencial', array['properties','agriculture','inventory']::varchar[]),
  ('Profissional', array['properties','agriculture','livestock','inventory','finance','reports']::varchar[]),
  ('Cooperativa', array['properties','agriculture','inventory','finance','logistics','traceability','reports']::varchar[]),
  ('Agroindústria', array['properties','inventory','finance','logistics','traceability','reports']::varchar[]),
  ('Enterprise', array['properties','agriculture','livestock','inventory','finance','logistics','traceability','reports','intelligence']::varchar[])
) as v(name, modules)
where p.name = v.name
  and p.modules @> v.modules
  and p.modules <@ (v.modules || array['rural-hr','verticals']::varchar[])
  and (p.modules @> array['rural-hr']::varchar[] or p.modules @> array['verticals']::varchar[]);

insert into agro360.saas_plan_module_reviews (plan_id, module_code, reason_code, detail)
select p.id, 'verticals', 'ADDED_BY_MIGRATION_122',
  'O plano ainda contém verticals. A correção automática só restaura o catálogo semeado quando não há outros módulos.'
from agro360.saas_plans p
where 'verticals' = any (p.modules)
on conflict do nothing;

insert into agro360.saas_plan_module_reviews (plan_id, module_code, reason_code, detail)
select p.id, 'rural-hr', 'ADDED_OUTSIDE_SEED_CATALOG',
  'rural-hr permanece no plano. O catálogo semeado não inclui o módulo; a retirada automática foi limitada aos planos cujo conjunto era só o catálogo mais o acréscimo da 122.'
from agro360.saas_plans p
where 'rural-hr' = any (p.modules)
on conflict do nothing;

select agro360.platform_enable_tenant_rls('agro360.rural_hr_time_corrections');
select agro360.platform_enable_tenant_rls('agro360.rural_hr_record_events');
select agro360.platform_enable_tenant_rls('agro360.rural_hr_command_replays');
select agro360.platform_enable_tenant_rls('agro360.rural_hr_data_reviews');

do $$
begin
  if exists (select 1 from pg_roles where rolname = 'agro360_app') then
    grant select, insert, update, delete on table
      agro360.rural_hr_tariffs,
      agro360.rural_hr_time_corrections,
      agro360.rural_hr_record_events,
      agro360.rural_hr_command_replays,
      agro360.rural_hr_data_reviews
    to agro360_app;
    grant select on table agro360.saas_plan_module_reviews to agro360_app;
    grant execute on function agro360.rural_hr_projection_issues() to agro360_app;
    grant execute on function agro360.rural_hr_assert_projection() to agro360_app;
  end if;
end $$;

insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.13.0', 'Fonte canônica de RH, integridade de vínculos e correção comercial da 122 (AG-HR-OP-002)', now())
on conflict (version) do update set description = excluded.description;
