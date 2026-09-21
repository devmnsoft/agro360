-- AG-SaaS-COM-001 - catálogo comercial, contratação, cobrança gerencial e onboarding assistido
begin;
set local search_path to agro360, public;

create table if not exists agro360.saas_module_catalog(
 id uuid primary key default gen_random_uuid(), code varchar(80) not null unique, name varchar(160) not null,
 description varchar(1000) not null, active boolean not null default true, essential boolean not null default false,
 additional boolean not null default true, base_price numeric(14,2), menu_order int not null check(menu_order>0),
 permission_codes varchar[] not null default '{}', created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
 created_by uuid, updated_by uuid, deleted_at timestamptz, check(base_price is null or base_price>=0), check(not (essential and additional)));
create table if not exists agro360.saas_module_dependencies(
 module_id uuid not null references agro360.saas_module_catalog(id), depends_on_id uuid not null references agro360.saas_module_catalog(id),
 primary key(module_id,depends_on_id), check(module_id<>depends_on_id));
create table if not exists agro360.saas_plan_modules(
 plan_id uuid not null references agro360.saas_plans(id), module_id uuid not null references agro360.saas_module_catalog(id),
 included boolean not null default true, limit_value bigint, created_at timestamptz not null default now(), created_by uuid,
 primary key(plan_id,module_id), check(limit_value is null or limit_value>0));
create table if not exists agro360.saas_tenant_modules(
 tenant_id uuid not null references agro360.tenancy_tenants(id), module_id uuid not null references agro360.saas_module_catalog(id),
 status varchar(20) not null check(status in('REQUESTED','CONTRACTED','ACTIVE','BLOCKED','SUSPENDED','CANCELLED')),
 reason varchar(1000) not null check(length(trim(reason))>=5), contracted_at timestamptz, blocked_at timestamptz,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid,
 primary key(tenant_id,module_id));
create table if not exists agro360.saas_tenant_module_events(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null references agro360.tenancy_tenants(id),
 module_id uuid not null references agro360.saas_module_catalog(id), previous_status varchar(20), new_status varchar(20) not null,
 reason varchar(1000) not null check(length(trim(reason))>=5), payload jsonb not null default '{}',
 created_at timestamptz not null default now(), created_by uuid not null);
create index if not exists ix_saas_tenant_modules_status on agro360.saas_tenant_modules(tenant_id,status);
create index if not exists ix_saas_tenant_module_events on agro360.saas_tenant_module_events(tenant_id,created_at desc);

alter table agro360.saas_billing_charges add column if not exists base_amount numeric(14,2);
alter table agro360.saas_billing_charges add column if not exists additional_amount numeric(14,2) not null default 0;
alter table agro360.saas_billing_charges add column if not exists discount numeric(14,2) not null default 0;
alter table agro360.saas_billing_charges add column if not exists module_codes varchar[] not null default '{}';
alter table agro360.saas_billing_charges add column if not exists paid_amount numeric(14,2);
update agro360.saas_billing_charges set base_amount=amount where base_amount is null;
alter table agro360.saas_billing_charges alter column base_amount set not null;
do $$ declare constraint_name text; begin
 select c.conname into constraint_name from pg_constraint c where c.conrelid='agro360.saas_billing_charges'::regclass and c.contype='c' and pg_get_constraintdef(c.oid) like '%status = ANY%';
 if constraint_name is not null then execute format('alter table agro360.saas_billing_charges drop constraint %I',constraint_name); end if;
end $$;
alter table agro360.saas_billing_charges add constraint ck_saas_billing_charge_status check(status in('DRAFT','OPEN','ISSUED','PAID','OVERDUE','CANCELLED','NEGOTIATING'));
alter table agro360.saas_billing_charges add constraint ck_saas_billing_amounts check(base_amount>=0 and additional_amount>=0 and discount>=0 and discount<=base_amount+additional_amount and amount=round(base_amount+additional_amount-discount,2));
alter table agro360.saas_billing_charges add constraint ck_saas_billing_payment_evidence check(status<>'PAID' or (paid_on is not null and paid_amount>0 and length(trim(coalesce(notes,'')))>0));

alter table agro360.saas_onboarding_steps add column if not exists recommended_action varchar(500);
alter table agro360.saas_onboarding_steps add column if not exists prerequisite_codes varchar[] not null default '{}';
alter table agro360.saas_tenant_onboarding_progress add column if not exists blocked_reason varchar(1000);
alter table agro360.saas_tenant_onboarding_progress add column if not exists history jsonb not null default '[]';

insert into agro360.saas_module_catalog(code,name,description,essential,additional,menu_order) values
 ('platform-base','Plataforma Base','Identidade, segurança, auditoria e administração da conta.',true,false,1),
 ('properties','Propriedades e Fazendas','Estrutura de propriedades, unidades e talhões.',true,false,2),
 ('agriculture','Agricultura e Safras','Planejamento e execução agrícola.',false,true,3),('livestock','Pecuária','Gestão zootécnica.',false,true,4),
 ('inventory','Estoque e Insumos','Saldos, lotes e movimentações.',false,true,5),('procurement','Compras e Suprimentos','Solicitação, compra e recebimento.',false,true,6),
 ('costs','Custos e Controladoria','Apropriação e análise gerencial.',false,true,7),('finance','Financeiro','Financeiro operacional rural, separado da cobrança MNSOFT.',false,true,8),
 ('logistics','Logística e Expedição','Reservas, viagens, entregas e retornos.',false,true,9),('quality','Qualidade e Compliance','Inspeções, não conformidades e CAPA.',false,true,10),
 ('traceability','Rastreabilidade e QR Code','Genealogia e publicação controlada.',false,true,11),('industrial','Produção Agroindustrial','Ordens, consumos e lotes produzidos.',false,true,12),
 ('mobile','Campo Mobile/PWA','Operação controlada em campo.',false,true,13),('fleet','Frota e Máquinas','Ativos, manutenção e abastecimento.',false,true,14),
 ('esg','ESG/Sustentabilidade','Indicadores e compromissos socioambientais.',false,true,15),('marketplace','Marketplace/Ecossistema','Ofertas e parceiros do ecossistema.',false,true,16),
 ('bi','BI/Inteligência','Indicadores e inteligência executiva.',false,true,17),('support','Suporte/Customer Success','Atendimento e sucesso do cliente.',false,true,18)
on conflict(code) do update set name=excluded.name,description=excluded.description,essential=excluded.essential,additional=excluded.additional,menu_order=excluded.menu_order,updated_at=now();
insert into agro360.saas_module_dependencies(module_id,depends_on_id)
select m.id,b.id from agro360.saas_module_catalog m cross join agro360.saas_module_catalog b where b.code='platform-base' and m.code<>'platform-base' on conflict do nothing;

alter table agro360.saas_tenant_modules enable row level security;
alter table agro360.saas_tenant_modules force row level security;
drop policy if exists saas_tenant_modules_isolation on agro360.saas_tenant_modules;
create policy saas_tenant_modules_isolation on agro360.saas_tenant_modules using (tenant_id=nullif(current_setting('app.tenant_id',true),'')::uuid) with check (tenant_id=nullif(current_setting('app.tenant_id',true),'')::uuid);
alter table agro360.saas_tenant_module_events enable row level security;
alter table agro360.saas_tenant_module_events force row level security;
drop policy if exists saas_tenant_module_events_isolation on agro360.saas_tenant_module_events;
create policy saas_tenant_module_events_isolation on agro360.saas_tenant_module_events using (tenant_id=nullif(current_setting('app.tenant_id',true),'')::uuid) with check (tenant_id=nullif(current_setting('app.tenant_id',true),'')::uuid);

insert into agro360.platform_schema_versions(version,description,installed_at) values('9.7.0','AG-SaaS-COM-001 - administração comercial SaaS',now()) on conflict(version) do nothing;
commit;
