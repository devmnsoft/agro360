-- Migration 125: governança SaaS — estados completos de status, origem/vigência dos direitos e catálogo completo.
-- Schema 11.15.0. Não altera checksum de migration aplicada.
-- Bloqueio explícito prevalece sobre o plano: desativar um plano não remove direitos por módulo.
-- Reaplicável: drop/add idempotentes de constraints com nome estável.
set local search_path to agro360, public;

alter table agro360.saas_organizations drop constraint if exists saas_organizations_status_check;
alter table agro360.saas_organizations add constraint saas_organizations_status_check check(status in('REGISTERING','IMPLEMENTING','TRIAL','ACTIVE','SUSPENDED','BLOCKED','DELINQUENT','INACTIVE','CANCELLED','CLOSED'));

alter table agro360.saas_tenant_status_events drop constraint if exists saas_tenant_status_events_new_status_check;
alter table agro360.saas_tenant_status_events add constraint saas_tenant_status_events_new_status_check check(new_status in('REGISTERING','IMPLEMENTING','TRIAL','ACTIVE','SUSPENDED','BLOCKED','DELINQUENT','INACTIVE','CANCELLED','CLOSED'));

alter table agro360.platform_tenants drop constraint if exists platform_tenants_status_check;
alter table agro360.platform_tenants add constraint platform_tenants_status_check check(status in ('REGISTERING','IMPLEMENTING','TRIAL','ACTIVE','SUSPENDED','BLOCKED','DELINQUENT','INACTIVE','CANCELLED','CLOSED'));

alter table agro360.platform_tenant_module_entitlements
  add column if not exists origin varchar(20),
  add column if not exists valid_until timestamptz;
comment on column agro360.platform_tenant_module_entitlements.origin is
  'Origem do direito: PLAN (plano contratado), MARKETPLACE (módulo avulso ou trial) ou MANUAL (ajuste administrativo).';
comment on column agro360.platform_tenant_module_entitlements.valid_until is
  'Vigência de direitos temporários (trial); nulo indica direito permanente.';

alter table agro360.platform_tenant_module_entitlements drop constraint if exists platform_tenant_module_entitlements_status_check;
alter table agro360.platform_tenant_module_entitlements add constraint platform_tenant_module_entitlements_status_check check(status in ('CONTRACTED','ACTIVE','BLOCKED','TRIAL','DELINQUENT','SUSPENDED','INACTIVE'));
alter table agro360.platform_tenant_module_entitlements drop constraint if exists ck_platform_tenant_module_entitlement_origin;
alter table agro360.platform_tenant_module_entitlements add constraint ck_platform_tenant_module_entitlement_origin check(origin is null or origin in('PLAN','MARKETPLACE','MANUAL'));

create index if not exists ix_platform_tenant_module_entitlements_valid_until on agro360.platform_tenant_module_entitlements(tenant_id,valid_until) where valid_until is not null;

insert into agro360.platform_module_catalog(code,name,description,active) values
 ('livestock','Pecuária','Animais, rebanho e sanidade.',true),
 ('reports','Relatórios','Relatórios operacionais consolidados.',true),
 ('intelligence','Inteligência','Análise preditiva e recomendações.',true)
 on conflict(code) do update set active=true,updated_at=now();

update agro360.platform_tenant_module_entitlements set origin='PLAN' where origin is null;

insert into agro360.platform_tenant_module_entitlements(tenant_id,module_id,status,reason,activated_at,origin)
 select distinct o.tenant_id,m.id,'ACTIVE','Snapshot retroativo do plano vigente',now(),'PLAN'
 from agro360.saas_organizations o
 join agro360.saas_plans p on p.id=o.plan_id
 cross join lateral unnest(p.modules) as planned(code)
 join agro360.platform_module_catalog m on lower(m.code)=lower(planned.code)
 where o.status='ACTIVE'
 on conflict(tenant_id,module_id) do nothing;

insert into agro360.platform_schema_versions(version, description, installed_at)
 values('11.15.0', 'Governança SaaS: estados completos, origem e vigência dos direitos, catálogo completo', now())
 on conflict (version) do update set description = excluded.description;
