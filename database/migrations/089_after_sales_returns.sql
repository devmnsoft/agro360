begin;

-- Pós-venda mantém o caso separado dos fatos físicos, da qualidade e do financeiro.
create sequence if not exists agro360.after_sales_occurrence_number_seq;
create table if not exists agro360.after_sales_occurrences(
 id uuid primary key, tenant_id uuid not null, number bigint not null default nextval('agro360.after_sales_occurrence_number_seq'),
 customer_id uuid not null, order_id uuid not null, shipment_id uuid not null, shipment_item_id uuid, stock_lot_id uuid,
 type varchar(32) not null check(type in('SHORTAGE','WRONG_PRODUCT','DAMAGE','REFUSAL','QUALITY','DELAY','NOT_DELIVERED','OTHER')),
 description text not null, affected_quantity numeric(20,6), unit varchar(20), occurred_at timestamptz not null,
 recorded_at timestamptz not null default now(), assignee_id uuid, due_at timestamptz,
 evidence_document_id uuid, evidence_pending boolean not null default false,
 status varchar(32) not null default 'OPEN' check(status in('OPEN','ANALYSIS','AWAITING_INFORMATION','SOLUTION_PROPOSED','AWAITING_EXECUTION','RESOLVED','CANCELLED')),
 idempotency_key varchar(120) not null, request_hash char(64) not null, version bigint not null default 1,
 created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz not null default now(), updated_by uuid not null,
 deleted_at timestamptz, deleted_by uuid, deletion_reason text,
 unique(tenant_id,id), unique(tenant_id,number), unique(tenant_id,idempotency_key),
 foreign key(tenant_id,customer_id) references agro360.crm_customers(tenant_id,id),
 foreign key(tenant_id,order_id) references agro360.sales_orders(tenant_id,id),
 foreign key(tenant_id,shipment_id) references agro360.fulfillment_shipments(tenant_id,id),
 foreign key(tenant_id,shipment_item_id) references agro360.fulfillment_shipment_items(tenant_id,id),
 foreign key(tenant_id,stock_lot_id) references agro360.inventory_stock_lots(tenant_id,id),
 foreign key(tenant_id,assignee_id) references agro360.identity_users(tenant_id,id),
 check((shipment_item_id is null and affected_quantity is null and unit is null and stock_lot_id is null) or
       (shipment_item_id is not null and affected_quantity is not null and affected_quantity>0 and nullif(trim(unit),'') is not null)),
 check(not evidence_pending or evidence_document_id is null));
create index if not exists ix_after_sales_occurrences_list on agro360.after_sales_occurrences(tenant_id,status,recorded_at desc,id) where deleted_at is null;
create index if not exists ix_after_sales_occurrences_similar on agro360.after_sales_occurrences(tenant_id,shipment_id,type,occurred_at) where deleted_at is null and status not in('CANCELLED','RESOLVED');

create table if not exists agro360.after_sales_events(
 id uuid primary key, tenant_id uuid not null, occurrence_id uuid not null, event_type varchar(40) not null,
 from_status varchar(32), to_status varchar(32), reason text, payload jsonb not null default '{}'::jsonb,
 occurred_at timestamptz not null default now(), actor_id uuid not null,
 unique(tenant_id,id), foreign key(tenant_id,occurrence_id) references agro360.after_sales_occurrences(tenant_id,id));
create index if not exists ix_after_sales_events_timeline on agro360.after_sales_events(tenant_id,occurrence_id,occurred_at,id);

create table if not exists agro360.after_sales_solutions(
 id uuid primary key, tenant_id uuid not null, occurrence_id uuid not null,
 type varchar(32) not null check(type in('COMPLEMENT','COLLECTION','RETURN','REPLACEMENT','INFORMATION_CORRECTION','COMMERCIAL_ADJUSTMENT','CLOSE_WITHOUT_ADJUSTMENT')),
 description text not null, required boolean not null default true, status varchar(20) not null default 'PROPOSED' check(status in('PROPOSED','APPROVED','IN_PROGRESS','COMPLETED','CANCELLED')),
 due_at timestamptz, idempotency_key varchar(120) not null, request_hash char(64) not null,
 created_at timestamptz not null default now(), created_by uuid not null, completed_at timestamptz,
 unique(tenant_id,id), unique(tenant_id,idempotency_key), foreign key(tenant_id,occurrence_id) references agro360.after_sales_occurrences(tenant_id,id),
 check(type<>'CLOSE_WITHOUT_ADJUSTMENT' or nullif(trim(description),'') is not null));

alter table agro360.fulfillment_returns add column if not exists occurrence_id uuid;
alter table agro360.fulfillment_returns add column if not exists receiving_warehouse_id uuid;
alter table agro360.fulfillment_returns add column if not exists responsible_id uuid;
alter table agro360.fulfillment_returns add column if not exists return_conditions text;
alter table agro360.fulfillment_returns add column if not exists due_at timestamptz;
alter table agro360.fulfillment_returns add column if not exists cancelled_at timestamptz;
alter table agro360.fulfillment_returns add column if not exists cancelled_by uuid;
alter table agro360.fulfillment_returns add column if not exists cancellation_reason text;
alter table agro360.fulfillment_returns drop constraint if exists fulfillment_returns_status_check;
alter table agro360.fulfillment_returns add constraint fulfillment_returns_status_check check(status in('AWAITING_RECEIPT','PARTIALLY_RECEIVED','AWAITING_QUALITY','RELEASED','BLOCKED','REPROCESSING','DISPOSED','CANCELLED'));
alter table agro360.fulfillment_returns drop constraint if exists fk_fulfillment_return_occurrence;
alter table agro360.fulfillment_returns add constraint fk_fulfillment_return_occurrence foreign key(tenant_id,occurrence_id) references agro360.after_sales_occurrences(tenant_id,id);

alter table agro360.fulfillment_return_receipts add column if not exists received_at timestamptz not null default now();
alter table agro360.fulfillment_return_receipts add column if not exists responsible_id uuid;
alter table agro360.fulfillment_return_receipts add column if not exists origin_pending boolean not null default false;
alter table agro360.fulfillment_return_receipts add column if not exists divergence text;

alter table agro360.fulfillment_return_decisions drop constraint if exists fulfillment_return_decisions_decision_check;
alter table agro360.fulfillment_return_decisions add constraint fulfillment_return_decisions_decision_check check(decision in('RELEASE','BLOCK','REPROCESS','DISPOSE','OTHER'));
alter table agro360.fulfillment_return_decisions add column if not exists criteria_version varchar(80);
alter table agro360.fulfillment_return_decisions add column if not exists stock_movement_id uuid;

create table if not exists agro360.after_sales_adjustments(
 id uuid primary key, tenant_id uuid not null, occurrence_id uuid not null,
 type varchar(24) not null check(type in('DISCOUNT','CREDIT','PARTIAL_CANCELLATION','REFUND','NONE')),
 currency char(3) not null, proposed_amount numeric(20,4) not null default 0, approved_amount numeric(20,4), executed_amount numeric(20,4),
 status varchar(24) not null default 'PROPOSED' check(status in('PROPOSED','APPROVED','EXECUTION_PENDING','EXECUTED','CANCELLED')),
 financial_reference_id uuid, reason text not null, idempotency_key varchar(120) not null, request_hash char(64) not null,
 created_at timestamptz not null default now(), created_by uuid not null, approved_at timestamptz, approved_by uuid, executed_at timestamptz,
 unique(tenant_id,id), unique(tenant_id,idempotency_key), foreign key(tenant_id,occurrence_id) references agro360.after_sales_occurrences(tenant_id,id),
 check(proposed_amount>=0 and approved_amount>=0 and executed_amount>=0),
 check(type<>'NONE' or (proposed_amount=0 and coalesce(approved_amount,0)=0 and coalesce(executed_amount,0)=0)),
 check(status<>'EXECUTED' or (executed_at is not null and financial_reference_id is not null)));

create table if not exists agro360.after_sales_replacements(
 id uuid primary key, tenant_id uuid not null, occurrence_id uuid not null, shipment_id uuid,
 quantity numeric(20,6) not null check(quantity>0), status varchar(24) not null default 'REQUESTED' check(status in('REQUESTED','LINKED','DISPATCHED','COMPLETED','CANCELLED')),
 idempotency_key varchar(120) not null, request_hash char(64) not null, created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,idempotency_key), foreign key(tenant_id,occurrence_id) references agro360.after_sales_occurrences(tenant_id,id),
 foreign key(tenant_id,shipment_id) references agro360.fulfillment_shipments(tenant_id,id));

do $$ declare t text; begin foreach t in array array['after_sales_occurrences','after_sales_events','after_sales_solutions','after_sales_adjustments','after_sales_replacements'] loop perform agro360.platform_enable_tenant_rls('agro360.'||t); end loop; end $$;
insert into agro360.identity_permissions(code,module,description) values
 ('after-sales.read','AfterSales','Consultar casos, devoluções e execução.'),
 ('after-sales.write','AfterSales','Registrar e tratar ocorrências.'),
 ('after-sales.approve','AfterSales','Aprovar soluções, destinações e ajustes.')
on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into agro360.identity_role_permissions(tenant_id,role_id,permission_id)
select r.tenant_id,r.id,p.id from agro360.identity_roles r cross join agro360.identity_permissions p
where lower(r.code)='tenant-administrator' and p.code in('after-sales.read','after-sales.write','after-sales.approve')
on conflict do nothing;
insert into agro360.platform_schema_versions(version,description,installed_at) values('8.9.0','Pós-venda operacional e devoluções rastreáveis',now()) on conflict(version) do nothing;
commit;
