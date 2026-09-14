begin;
create table agro360.cost_management_entries(
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), farm_id uuid not null,
 category varchar(60) not null, competence_date date not null, planned_amount numeric(18,4) not null default 0,
 committed_amount numeric(18,4) not null default 0, recognized_amount numeric(18,4) not null, paid_amount numeric(18,4) not null default 0,
 allocated_amount numeric(18,4) not null default 0, reversed_amount numeric(18,4) not null default 0, currency char(3) not null default 'BRL',
 source_type varchar(60) not null, source_key varchar(200) not null, source_document varchar(1000), description varchar(240) not null,
 status varchar(16) not null default 'OPEN', row_version bigint not null default 1,
 created_at timestamptz not null default now(),created_by uuid not null,updated_at timestamptz,updated_by uuid,deleted_at timestamptz,deleted_by uuid,
 unique(tenant_id,id),unique(tenant_id,source_type,source_key),foreign key(tenant_id,farm_id) references agro360.geo_farms(tenant_id,id),
 check(planned_amount>=0 and committed_amount>=0 and recognized_amount>=0 and paid_amount>=0 and allocated_amount>=0 and reversed_amount>=0),
 check(allocated_amount<=recognized_amount),check(currency='BRL'),check(status in('OPEN','VOID'))
);
create index ix_cost_management_filter on agro360.cost_management_entries(tenant_id,farm_id,competence_date desc,status) where deleted_at is null;
create index ix_cost_management_pending on agro360.cost_management_entries(tenant_id,competence_date desc) where deleted_at is null and status='OPEN';
create table agro360.cost_allocation_batches(
 id uuid primary key,tenant_id uuid not null references agro360.tenancy_tenants(id),entry_id uuid not null,method varchar(16) not null,
 amount numeric(18,4) not null,currency char(3) not null,base_snapshot jsonb not null,idempotency_key varchar(160) not null,status varchar(16) not null,
 justification varchar(1000),confirmed_at timestamptz not null,confirmed_by uuid not null,reversed_at timestamptz,reversed_by uuid,reversal_reason varchar(1000),reversal_key varchar(160),created_at timestamptz not null default now(),created_by uuid not null,
 unique(tenant_id,id),unique(tenant_id,idempotency_key),unique(tenant_id,reversal_key),foreign key(tenant_id,entry_id) references agro360.cost_management_entries(tenant_id,id),
 check(amount>0),check(method in('DIRECT','PERCENTAGE','AREA','PRODUCTION','HOURS','EQUAL')),check(status in('CONFIRMED','REVERSED')),
 check(status<>'REVERSED' or (reversed_at is not null and reversed_by is not null and nullif(trim(reversal_reason),'') is not null))
);
create table agro360.cost_allocations(
 id uuid primary key,tenant_id uuid not null,batch_id uuid not null,entry_id uuid not null,season_id uuid not null,farm_id uuid not null,field_id uuid,cost_center_id uuid,
 base_value numeric(20,6) not null,base_unit varchar(20),percentage numeric(12,6) not null,amount numeric(18,4) not null,rounding_adjustment numeric(18,4) not null default 0,
 status varchar(16) not null,confirmed_at timestamptz not null,confirmed_by uuid not null,reversed_at timestamptz,reversed_by uuid,reversal_reason varchar(1000),created_at timestamptz not null default now(),created_by uuid not null,
 unique(tenant_id,id),foreign key(tenant_id,batch_id) references agro360.cost_allocation_batches(tenant_id,id),foreign key(tenant_id,entry_id) references agro360.cost_management_entries(tenant_id,id),
 foreign key(tenant_id,season_id) references agro360.agriculture_seasons(tenant_id,id),foreign key(tenant_id,farm_id) references agro360.geo_farms(tenant_id,id),foreign key(tenant_id,field_id) references agro360.geo_fields(tenant_id,id),foreign key(tenant_id,cost_center_id) references agro360.finance_cost_centers(tenant_id,id),
 check(base_value>=0 and percentage>=0 and percentage<=100 and amount>0),check(status in('CONFIRMED','REVERSED'))
);
create index ix_cost_allocations_season on agro360.cost_allocations(tenant_id,season_id,confirmed_at desc,status);
create index ix_cost_allocations_entry on agro360.cost_allocations(tenant_id,entry_id,status);
select agro360.platform_enable_tenant_rls('agro360.cost_management_entries');
select agro360.platform_enable_tenant_rls('agro360.cost_allocation_batches');
select agro360.platform_enable_tenant_rls('agro360.cost_allocations');
insert into agro360.cost_management_entries(id,tenant_id,farm_id,category,competence_date,recognized_amount,allocated_amount,currency,source_type,source_key,source_document,description,status,created_at,created_by)
select gen_random_uuid(),e.tenant_id,e.farm_id,e.category,e.occurred_on,e.amount,e.amount,e.currency,e.source_type,'legacy:'||e.id,e.source_id::text,'Custo operacional legado já apropriado','OPEN',e.created_at,e.created_by from agro360.cost_entries e
on conflict(tenant_id,source_type,source_key) do nothing;
insert into agro360.cost_allocation_batches(id,tenant_id,entry_id,method,amount,currency,base_snapshot,idempotency_key,status,justification,confirmed_at,confirmed_by,created_at,created_by)
select gen_random_uuid(),m.tenant_id,m.id,'DIRECT',e.amount,e.currency,jsonb_build_object('legacyCostEntryId',e.id),'legacy-allocation:'||e.id,'CONFIRMED','Migração do vínculo direto existente',e.created_at,e.created_by,e.created_at,e.created_by
from agro360.cost_entries e join agro360.cost_management_entries m on m.tenant_id=e.tenant_id and m.source_key='legacy:'||e.id where e.season_id is not null
on conflict(tenant_id,idempotency_key) do nothing;
insert into agro360.cost_allocations(id,tenant_id,batch_id,entry_id,season_id,farm_id,field_id,cost_center_id,base_value,base_unit,percentage,amount,status,confirmed_at,confirmed_by,created_at,created_by)
select gen_random_uuid(),e.tenant_id,b.id,m.id,e.season_id,e.farm_id,e.field_id,null,1,null,100,e.amount,'CONFIRMED',e.created_at,e.created_by,e.created_at,e.created_by
from agro360.cost_entries e join agro360.cost_management_entries m on m.tenant_id=e.tenant_id and m.source_key='legacy:'||e.id join agro360.cost_allocation_batches b on b.tenant_id=e.tenant_id and b.idempotency_key='legacy-allocation:'||e.id
where e.season_id is not null and not exists(select 1 from agro360.cost_allocations a where a.tenant_id=e.tenant_id and a.batch_id=b.id);
insert into agro360.platform_schema_versions(version,description,installed_at) values('7.8.0','Custos por safra, apropriação direta, rateio reproduzível e conferência gerencial',now()) on conflict(version) do nothing;
commit;
