begin;

-- Reading belongs to a person; assignment belongs to the shared occurrence episode.
create table if not exists agro360.operation_occurrence_reads(
 tenant_id uuid not null references agro360.tenancy_tenants(id), occurrence_key varchar(180) not null,
 user_id uuid not null, first_viewed_at timestamptz not null default now(), last_viewed_at timestamptz not null default now(),
 view_count integer not null default 1 check(view_count>0),
 primary key(tenant_id,occurrence_key,user_id),
 foreign key(tenant_id,occurrence_key) references agro360.operation_occurrence_states(tenant_id,occurrence_key),
 foreign key(tenant_id,user_id) references agro360.identity_users(tenant_id,id)
);
-- Only migrate a view when 073 recorded which user actually performed it.
insert into agro360.operation_occurrence_reads(tenant_id,occurrence_key,user_id,first_viewed_at,last_viewed_at)
select tenant_id,occurrence_key,viewed_by,viewed_at,viewed_at from agro360.operation_occurrence_states
where viewed_by is not null and viewed_at is not null on conflict do nothing;
alter table agro360.operation_occurrence_states add column if not exists assignment_version bigint not null default 0;
alter table agro360.operation_occurrence_states add column if not exists assignment_reason varchar(1000);
alter table agro360.operation_occurrence_events drop constraint if exists operation_occurrence_events_event_type_check;
alter table agro360.operation_occurrence_events add constraint operation_occurrence_events_event_type_check
 check(event_type in('VIEWED','ASSIGNED','TRANSFERRED','UNASSIGNED'));
alter table agro360.operation_occurrence_events add column if not exists reason varchar(1000);
create index if not exists ix_operation_occurrence_reads_user on agro360.operation_occurrence_reads(tenant_id,user_id,last_viewed_at desc);
select agro360.platform_enable_tenant_rls('agro360.operation_occurrence_reads');
alter table agro360.fulfillment_returns add column if not exists received_quantity numeric(20,6) not null default 0 check(received_quantity>=0);
alter table agro360.fulfillment_returns add column if not exists version bigint not null default 1;
create table if not exists agro360.fulfillment_return_receipts(
 id uuid primary key,tenant_id uuid not null,return_id uuid not null,quantity numeric(20,6) not null check(quantity>0),unit varchar(20) not null,condition varchar(20) not null check(condition in('INTACT','DAMAGED','INSPECTION_REQUIRED')),warehouse_id uuid not null,lot_number varchar(100),evidence_document_id uuid,notes varchar(1000),idempotency_key varchar(120) not null,request_hash char(64) not null,received_at timestamptz not null default now(),created_by uuid not null,unique(tenant_id,id),unique(tenant_id,idempotency_key),foreign key(tenant_id,return_id) references agro360.fulfillment_returns(tenant_id,id),foreign key(tenant_id,warehouse_id) references agro360.inventory_warehouses(tenant_id,id));
create table if not exists agro360.fulfillment_return_decisions(
 id uuid primary key,tenant_id uuid not null,return_id uuid not null,decision varchar(20) not null check(decision in('RELEASE','BLOCK','DISPOSE')),quantity numeric(20,6) not null check(quantity>0),reason varchar(1000) not null,idempotency_key varchar(120) not null,request_hash char(64) not null,decided_at timestamptz not null default now(),created_by uuid not null,unique(tenant_id,id),unique(tenant_id,idempotency_key),foreign key(tenant_id,return_id) references agro360.fulfillment_returns(tenant_id,id));
select agro360.platform_enable_tenant_rls('agro360.fulfillment_return_receipts');
select agro360.platform_enable_tenant_rls('agro360.fulfillment_return_decisions');
insert into agro360.platform_schema_versions(version,description,installed_at)
values('7.4.0','Leitura individual, atribuição concorrente e retornos físicos',now()) on conflict(version) do nothing;
commit;
