-- AG-SaaS-ADM-002 - restricoes independentes e conciliacao de cobrancas
begin;
set local search_path to agro360, public;

create table if not exists agro360.saas_tenant_restrictions(
 id uuid primary key default gen_random_uuid(),
 tenant_id uuid not null references agro360.tenancy_tenants(id),
 cause varchar(24) not null check(cause in ('ADMINISTRATIVE','FINANCIAL')),
 reason varchar(1000) not null check(length(trim(reason))>=5),
 starts_at timestamptz not null default now(), ends_at timestamptz,
 created_at timestamptz not null default now(), created_by uuid not null,
 ended_at timestamptz, ended_by uuid, end_reason varchar(1000),
 check(ends_at is null or ends_at>starts_at),
 check((ended_at is null and ended_by is null and end_reason is null) or
       (ended_at is not null and ended_by is not null and length(trim(end_reason))>=5))
);
create unique index if not exists ux_saas_tenant_active_restriction
 on agro360.saas_tenant_restrictions(tenant_id,cause) where ended_at is null;
create index if not exists ix_saas_tenant_restrictions_history
 on agro360.saas_tenant_restrictions(tenant_id,created_at desc);

create table if not exists agro360.saas_billing_payments(
 id uuid primary key default gen_random_uuid(),
 tenant_id uuid not null references agro360.tenancy_tenants(id),
 charge_id uuid not null references agro360.saas_billing_charges(id),
 idempotency_key varchar(120) not null,
 payment_reference varchar(160) not null check(length(trim(payment_reference))>=3),
 amount numeric(14,2) not null check(amount>0), applied_amount numeric(14,2) not null check(applied_amount>0),
 credit_amount numeric(14,2) not null default 0 check(credit_amount>=0),
 paid_on date not null, method varchar(30) not null default 'MANUAL',
 notes varchar(1000) not null check(length(trim(notes))>=5),
 created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,idempotency_key), check(amount=applied_amount+credit_amount)
);
create index if not exists ix_saas_billing_payments_charge
 on agro360.saas_billing_payments(tenant_id,charge_id,created_at);
create table if not exists agro360.saas_billing_credits(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null references agro360.tenancy_tenants(id),
 payment_id uuid not null unique references agro360.saas_billing_payments(id),
 amount numeric(14,2) not null check(amount>0), status varchar(20) not null default 'AVAILABLE'
  check(status in ('AVAILABLE','APPLIED','REFUNDED','CANCELLED')),
 created_at timestamptz not null default now(), created_by uuid not null
);

do $$ declare v_constraint text; begin
 select c.conname into v_constraint from pg_constraint c
 where c.conrelid='agro360.saas_billing_charges'::regclass and c.conname='ck_saas_billing_charge_status';
 if v_constraint is not null then execute format('alter table agro360.saas_billing_charges drop constraint %I',v_constraint); end if;
end $$;
alter table agro360.saas_billing_charges add constraint ck_saas_billing_charge_status
 check(status in('DRAFT','OPEN','ISSUED','PARTIALLY_PAID','PAID','OVERDUE','CANCELLED','NEGOTIATING'));
alter table agro360.saas_billing_charges drop constraint if exists ck_saas_billing_payment_evidence;
alter table agro360.saas_billing_charges add constraint ck_saas_billing_payment_evidence
 check(status not in ('PAID','PARTIALLY_PAID') or (paid_on is not null and paid_amount>0 and length(trim(coalesce(notes,'')))>0));

alter table agro360.saas_tenant_restrictions enable row level security;
alter table agro360.saas_tenant_restrictions force row level security;
drop policy if exists saas_tenant_restrictions_isolation on agro360.saas_tenant_restrictions;
create policy saas_tenant_restrictions_isolation on agro360.saas_tenant_restrictions
 using (tenant_id=nullif(current_setting('app.tenant_id',true),'')::uuid)
 with check (tenant_id=nullif(current_setting('app.tenant_id',true),'')::uuid);
alter table agro360.saas_billing_payments enable row level security;
alter table agro360.saas_billing_payments force row level security;
drop policy if exists saas_billing_payments_isolation on agro360.saas_billing_payments;
create policy saas_billing_payments_isolation on agro360.saas_billing_payments
 using (tenant_id=nullif(current_setting('app.tenant_id',true),'')::uuid)
 with check (tenant_id=nullif(current_setting('app.tenant_id',true),'')::uuid);
alter table agro360.saas_billing_credits enable row level security;
alter table agro360.saas_billing_credits force row level security;
drop policy if exists saas_billing_credits_isolation on agro360.saas_billing_credits;
create policy saas_billing_credits_isolation on agro360.saas_billing_credits
 using (tenant_id=nullif(current_setting('app.tenant_id',true),'')::uuid)
 with check (tenant_id=nullif(current_setting('app.tenant_id',true),'')::uuid);

grant select,insert,update,delete on agro360.saas_tenant_restrictions,
 agro360.saas_billing_payments,agro360.saas_billing_credits to agro360_app;

insert into agro360.platform_schema_versions(version,description,installed_at)
values('10.4.0','AG-SaaS-ADM-002 - ciclo administrativo e conciliacao',now()) on conflict(version) do nothing;
commit;
