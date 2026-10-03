-- Migration 122: Unificacao de jornadas, alocacao de equipes e integracao de custos de RH Rural
-- Schema version 11.12.0
set local search_path to agro360, public;

-- 1. Ampliar tipos de registro em rural_hr_records para suportar PERSON e TIME_ENTRY unificados
alter table agro360.rural_hr_records drop constraint if exists rural_hr_records_kind_check;
alter table agro360.rural_hr_records add constraint rural_hr_records_kind_check
  check(kind in('PERSON','TIME_ENTRY','TEAM','ALLOCATION','LABOR_COST','TRAINING','PPE','RISK','INSPECTION','INCIDENT','CORRECTIVE_ACTION','ACCOMMODATION','TRANSPORT'));

-- 2. Adicionar colunas de apoio a jornada, operacao agricola e tarifas
alter table agro360.rural_hr_records add column if not exists started_at timestamptz;
alter table agro360.rural_hr_records add column if not exists ended_at timestamptz;
alter table agro360.rural_hr_records add column if not exists break_minutes int not null default 0;
alter table agro360.rural_hr_records add column if not exists role varchar(120);
alter table agro360.rural_hr_records add column if not exists activity_type varchar(60);
alter table agro360.rural_hr_records add column if not exists order_id uuid;
alter table agro360.rural_hr_records add column if not exists season_id uuid;
alter table agro360.rural_hr_records add column if not exists plot_id uuid;
alter table agro360.rural_hr_records add column if not exists rate_type varchar(30) default 'HOURLY';
alter table agro360.rural_hr_records add column if not exists rate_value numeric(18,4) not null default 0;
alter table agro360.rural_hr_records add column if not exists hours_worked numeric(18,2) not null default 0;

-- 3. Sincronizar dados existentes
update agro360.rural_hr_records
set started_at = coalesce(started_at, starts_at),
    ended_at = coalesce(ended_at, ends_at)
where started_at is null or ended_at is null;

-- 4. Tabela de tarifas de mao de obra com vigencia
create table if not exists agro360.rural_hr_tariffs(
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    role_id uuid references agro360.rural_hr_roles(id),
    activity_type varchar(60),
    rate_type varchar(30) not null check(rate_type in('HOURLY','DAILY','PIECEWORK','FIXED')),
    rate_value numeric(18,4) not null check(rate_value >= 0),
    valid_from date not null,
    valid_to date,
    active boolean not null default true,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    unique(tenant_id, id),
    check(valid_to is null or valid_to >= valid_from)
);
create index if not exists ix_rural_hr_tariffs_lookup on agro360.rural_hr_tariffs(tenant_id, active, valid_from);
select agro360.platform_enable_tenant_rls('agro360.rural_hr_tariffs');

-- 5. Atualizar modulos base dos planos para incluir rural-hr e verticals
update agro360.saas_plans
set modules = array_cat(modules, array['rural-hr','verticals']::varchar[])
where not ('rural-hr' = any(modules));

-- 6. Atualizar versao da plataforma
insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.12.0', 'Unificacao de jornadas, alocacao de equipes e integracao de custos de RH Rural (AG-HR-OP-001)', now())
on conflict (version) do update set description = excluded.description;
