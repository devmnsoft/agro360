-- Migration 127: Privilégios do papel da aplicação agro360_app, auditoria de origem de entitlements e escopos de unidade de usuário.
-- Schema 11.17.0.
set local search_path to agro360, public;

-- 1. Tabela de escopos de unidade por usuário (Organização / Fazenda / Todas)
create table if not exists agro360.identity_user_unit_scopes (
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references agro360.platform_tenants(id),
    user_id uuid not null references agro360.identity_users(id),
    scope_type varchar(20) not null check(scope_type in ('ALL', 'ORGANIZATION', 'FARM')),
    organization_id uuid references agro360.organization_organizations(id),
    farm_id uuid references agro360.geo_farms(id),
    created_at timestamptz not null default now(),
    created_by uuid,
    constraint ck_user_unit_scope_target check (
        (scope_type = 'ALL' and organization_id is null and farm_id is null) or
        (scope_type = 'ORGANIZATION' and organization_id is not null and farm_id is null) or
        (scope_type = 'FARM' and farm_id is not null)
    )
);

create unique index if not exists uq_identity_user_unit_scopes
on agro360.identity_user_unit_scopes(
    tenant_id,
    user_id,
    scope_type,
    coalesce(organization_id, '00000000-0000-0000-0000-000000000000'::uuid),
    coalesce(farm_id, '00000000-0000-0000-0000-000000000000'::uuid)
);

create index if not exists ix_identity_user_unit_scopes_user
on agro360.identity_user_unit_scopes(tenant_id, user_id);

alter table agro360.identity_user_unit_scopes enable row level security;
alter table agro360.identity_user_unit_scopes force row level security;

drop policy if exists tenant_isolation on agro360.identity_user_unit_scopes;
create policy tenant_isolation on agro360.identity_user_unit_scopes
    using (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
    with check (tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid);

-- Seed de escopo 'ALL' para usuários ativos existentes
insert into agro360.identity_user_unit_scopes (id, tenant_id, user_id, scope_type, created_at)
select gen_random_uuid(), u.tenant_id, u.id, 'ALL', now()
from agro360.identity_users u
where u.deleted_at is null
on conflict do nothing;

-- 2. Concessão de privilégios mínimos necessários para o papel restrito agro360_app
do $$
begin
    if exists (select 1 from pg_roles where rolname = 'agro360_app') then
        execute 'grant usage on schema agro360 to agro360_app';

        -- Escopos de usuário
        execute 'grant select, insert, update, delete on agro360.identity_user_unit_scopes to agro360_app';

        -- Fluxos de programação de entrega, liquidação e histórico
        execute 'grant select, insert, update, delete on agro360.sales_delivery_schedules to agro360_app';
        execute 'grant select, insert, update, delete on agro360.sales_delivery_schedule_items to agro360_app';
        execute 'grant select, insert, update, delete on agro360.sales_delivery_schedule_revisions to agro360_app';

        -- Operações de entrega (append-only: select e insert)
        execute 'grant select, insert on agro360.sales_delivery_schedule_operations to agro360_app';

        -- Fluxos de devoluções, expedições e reconciliação
        execute 'grant select, insert, update, delete on agro360.fulfillment_shipment_items to agro360_app';
        execute 'grant select, insert, update, delete on agro360.fulfillment_delivery_attempts to agro360_app';
        execute 'grant select, insert, update, delete on agro360.fulfillment_return_receipts to agro360_app';
        execute 'grant select, insert, update, delete on agro360.fulfillment_operation_requests to agro360_app';
        execute 'grant select, insert, update, delete on agro360.fulfillment_return_dispositions to agro360_app';

        -- Entregas comerciais e eventos
        execute 'grant select, insert, update, delete on agro360.commercial_deliveries to agro360_app';
        execute 'grant select, insert, update, delete on agro360.commercial_billing_forecasts to agro360_app';
        execute 'grant select, insert, update, delete on agro360.commercial_events to agro360_app';

        -- Garante USAGE e SELECT em sequências
        execute 'grant usage, select on all sequences in schema agro360 to agro360_app';
    end if;
end $$;

-- 3. Auditoria e reclassificação precisa de origens de entitlements (correção de ambiguidade da migration 125)
update agro360.platform_tenant_module_entitlements e
set origin = 'MARKETPLACE'
where e.origin = 'PLAN'
  and exists (
      select 1
      from agro360.platform_tenant_modules tm
      join agro360.platform_marketplace_modules mm on mm.id = tm.module_id
      join agro360.platform_module_catalog c on c.id = e.module_id
      where tm.tenant_id = e.tenant_id and lower(mm.code) = lower(c.code)
  );

update agro360.platform_tenant_module_entitlements e
set origin = 'MANUAL'
where e.origin = 'PLAN'
  and not exists (
      select 1
      from agro360.saas_organizations o
      join agro360.saas_plans p on p.id = o.plan_id
      cross join lateral unnest(p.modules) as planned(code)
      join agro360.platform_module_catalog c on c.id = e.module_id
      where o.tenant_id = e.tenant_id and lower(planned.code) = lower(c.code)
  );

-- 4. Registro da versão do schema
insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.17.0', 'Privilégios mínimos agro360_app, escopos de unidades e auditoria de origem de direitos', now())
on conflict (version) do update set description = excluded.description;
