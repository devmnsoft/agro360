begin;

create sequence if not exists agro360.sales_proposal_number_seq;
create table if not exists agro360.sales_proposals(
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), customer_id uuid not null,
 opportunity_id uuid, representative_id uuid, proposal_number varchar(40) not null, status varchar(20) not null,
 current_version bigint not null default 1, accepted_version bigint, accepted_at timestamptz, accepted_by uuid,
 acceptance_evidence_type varchar(40), acceptance_evidence_reference varchar(1000),
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid,
 deleted_at timestamptz, unique(tenant_id,id), unique(tenant_id,proposal_number),
 foreign key(tenant_id,customer_id) references agro360.crm_customers(tenant_id,id),
 foreign key(tenant_id,opportunity_id) references agro360.sales_opportunities(tenant_id,id),
 foreign key(tenant_id,representative_id) references agro360.sales_representatives(tenant_id,id),
 check(status in('DRAFT','SUBMITTED','APPROVED','REJECTED','ACCEPTED','EXPIRED','CANCELLED')),
 check((status='ACCEPTED')=(accepted_version is not null))
);
create table if not exists agro360.sales_proposal_versions(
 id uuid primary key, tenant_id uuid not null, proposal_id uuid not null, version_number bigint not null,
 currency char(3) not null, valid_until date not null, freight numeric(18,2) not null default 0,
 payment_terms text not null, items_total numeric(18,2) not null, total_amount numeric(18,2) not null,
 change_reason text, supersedes_version bigint, created_at timestamptz not null default now(), created_by uuid,
 unique(tenant_id,id), unique(tenant_id,proposal_id,version_number),
 foreign key(tenant_id,proposal_id) references agro360.sales_proposals(tenant_id,id),
 check(freight>=0 and items_total>=0 and total_amount=round(items_total+freight,2)),
 check(version_number=1 or (supersedes_version=version_number-1 and change_reason is not null))
);
create table if not exists agro360.sales_proposal_items(
 id uuid primary key, tenant_id uuid not null, proposal_id uuid not null, version_number bigint not null,
 product_id uuid not null, unit varchar(20) not null, quantity numeric(20,6) not null,
 unit_price numeric(18,4) not null, discount_percentage numeric(7,4) not null default 0,
 total_amount numeric(18,2) not null, price_table_id uuid, pricing_snapshot jsonb not null,
 created_at timestamptz not null default now(), unique(tenant_id,id),
 foreign key(tenant_id,proposal_id,version_number) references agro360.sales_proposal_versions(tenant_id,proposal_id,version_number),
 foreign key(tenant_id,product_id) references agro360.inventory_products(tenant_id,id),
 foreign key(tenant_id,price_table_id) references agro360.sales_price_tables(tenant_id,id),
 check(quantity>0 and unit_price>0 and discount_percentage between 0 and 100 and total_amount>=0)
);
create table if not exists agro360.sales_proposal_decisions(
 id uuid primary key, tenant_id uuid not null, proposal_id uuid not null, version_number bigint not null,
 decision varchar(20) not null, reason text, decided_at timestamptz not null default now(), decided_by uuid not null,
 unique(tenant_id,proposal_id,version_number,decision),
 foreign key(tenant_id,proposal_id,version_number) references agro360.sales_proposal_versions(tenant_id,proposal_id,version_number),
 check(decision in('SUBMITTED','APPROVED','REJECTED','CANCELLED','ACCEPTED'))
);
create table if not exists agro360.sales_proposal_conversions(
 id uuid primary key, tenant_id uuid not null, proposal_id uuid not null, version_number bigint not null,
 order_id uuid not null, idempotency_key varchar(120) not null, request_hash char(64) not null,
 created_at timestamptz not null default now(), created_by uuid, unique(tenant_id,id), unique(tenant_id,idempotency_key),
 foreign key(tenant_id,proposal_id,version_number) references agro360.sales_proposal_versions(tenant_id,proposal_id,version_number),
 foreign key(tenant_id,order_id) references agro360.sales_orders(tenant_id,id)
);
create table if not exists agro360.sales_proposal_conversion_items(
 tenant_id uuid not null, conversion_id uuid not null, proposal_item_id uuid not null, order_item_id uuid not null,
 quantity numeric(20,6) not null check(quantity>0), primary key(tenant_id,conversion_id,proposal_item_id),
 foreign key(tenant_id,conversion_id) references agro360.sales_proposal_conversions(tenant_id,id),
 foreign key(tenant_id,proposal_item_id) references agro360.sales_proposal_items(tenant_id,id),
 foreign key(tenant_id,order_item_id) references agro360.sales_order_items(tenant_id,id)
);
create index if not exists ix_sales_proposals_customer on agro360.sales_proposals(tenant_id,customer_id,status,updated_at desc) where deleted_at is null;
create index if not exists ix_sales_proposal_items_version on agro360.sales_proposal_items(tenant_id,proposal_id,version_number);
create index if not exists ix_sales_proposal_conversion_balance on agro360.sales_proposal_conversion_items(tenant_id,proposal_item_id);

do $$ declare t text; begin foreach t in array array['sales_proposals','sales_proposal_versions','sales_proposal_items','sales_proposal_decisions','sales_proposal_conversions','sales_proposal_conversion_items'] loop
 execute format('alter table agro360.%I enable row level security',t); execute format('alter table agro360.%I force row level security',t);
 execute format('drop policy if exists tenant_isolation on agro360.%I',t);
 execute format('create policy tenant_isolation on agro360.%I using (tenant_id=agro360.current_tenant_id()) with check (tenant_id=agro360.current_tenant_id())',t);
end loop; end $$;

alter table agro360.sales_commissions add column if not exists rule_snapshot jsonb not null default '{}';
alter table agro360.sales_commissions add column if not exists source_event_id uuid;
alter table agro360.sales_commissions add column if not exists source_event_type varchar(40);
create unique index if not exists uq_sales_commission_source_event on agro360.sales_commissions(tenant_id,rule_id,representative_id,source_event_id) where source_event_id is not null and deleted_at is null;
create table if not exists agro360.sales_commission_adjustments(
 id uuid primary key, tenant_id uuid not null, commission_id uuid not null, source_event_id uuid not null,
 amount numeric(18,2) not null check(amount<>0), reason text not null, status varchar(20) not null default 'CALCULATED',
 created_at timestamptz not null default now(), created_by uuid, unique(tenant_id,commission_id,source_event_id),
 foreign key(tenant_id,commission_id) references agro360.sales_commissions(tenant_id,id),
 check(status in('CALCULATED','APPROVED','COMPENSATED'))
);
alter table agro360.sales_commission_adjustments enable row level security;
alter table agro360.sales_commission_adjustments force row level security;
drop policy if exists tenant_isolation on agro360.sales_commission_adjustments;
create policy tenant_isolation on agro360.sales_commission_adjustments using (tenant_id=agro360.current_tenant_id()) with check (tenant_id=agro360.current_tenant_id());
insert into agro360.platform_schema_versions(version,description,installed_at) values('10.7.0','Propostas comerciais versionadas e comissões rastreáveis',now()) on conflict(version) do nothing;
commit;
