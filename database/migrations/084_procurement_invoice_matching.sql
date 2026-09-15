begin;

create table agro360.procurement_match_tolerances (
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id),
 quantity_percent numeric(9,4) not null default 0, quantity_absolute numeric(20,6) not null default 0,
 price_percent numeric(9,4) not null default 0, price_absolute numeric(18,4) not null default 0,
 total_percent numeric(9,4) not null default 0, total_absolute numeric(18,2) not null default 0,
 excess_percent numeric(9,4) not null default 0, excess_absolute numeric(20,6) not null default 0,
 delivery_days integer not null default 0, separation_of_duties boolean not null default true,
 active boolean not null default true, version bigint not null default 1,
 created_at timestamptz not null default now(), created_by uuid not null,
 updated_at timestamptz not null default now(), updated_by uuid not null,
 deleted_at timestamptz, deleted_by uuid,
 unique(tenant_id,id),
 check(quantity_percent between 0 and 100 and price_percent between 0 and 100 and total_percent between 0 and 100 and excess_percent between 0 and 100),
 check(quantity_absolute>=0 and price_absolute>=0 and total_absolute>=0 and excess_absolute>=0 and delivery_days>=0)
);
create unique index ux_proc_match_tolerance_active on agro360.procurement_match_tolerances(tenant_id) where active and deleted_at is null;

create table agro360.procurement_billing_documents (
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), purchase_order_id uuid not null,
 supplier_id uuid not null, document_number varchar(100) not null, document_series varchar(30) not null default '',
 issued_on date not null, currency char(3) not null default 'BRL', goods_total numeric(18,2) not null,
 discount numeric(18,2) not null default 0, freight numeric(18,2) not null default 0, additional_amount numeric(18,2) not null default 0,
 total numeric(18,2) not null, fiscal_validation_status varchar(24) not null default 'NOT_VALIDATED',
 status varchar(24) not null default 'PENDING_MATCH', replacement_document_id uuid, cancel_reason varchar(1000),
 created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz not null default now(), updated_by uuid not null,
 deleted_at timestamptz, deleted_by uuid,
 unique(tenant_id,id), unique(tenant_id,supplier_id,document_number,document_series),
 foreign key(tenant_id,purchase_order_id) references agro360.procurement_purchase_orders(tenant_id,id),
 foreign key(tenant_id,supplier_id) references agro360.procurement_suppliers(tenant_id,id),
 foreign key(tenant_id,replacement_document_id) references agro360.procurement_billing_documents(tenant_id,id),
 check(goods_total>=0 and discount>=0 and freight>=0 and additional_amount>=0 and total>=0),
 check(total=round(goods_total-discount+freight+additional_amount,2)),
 check(fiscal_validation_status in('NOT_VALIDATED','VALIDATED_EXTERNALLY','REJECTED_EXTERNALLY')),
 check(status in('PENDING_MATCH','PENDING_EXCEPTION','MATCHED','REJECTED','CANCELLED','REPLACED'))
);

create table agro360.procurement_billing_lines (
 id uuid primary key, tenant_id uuid not null, billing_document_id uuid not null, purchase_order_item_id uuid not null,
 receipt_item_id uuid not null, description varchar(300) not null, quantity numeric(20,6) not null, unit varchar(20) not null,
 unit_price numeric(18,4) not null, discount numeric(18,2) not null default 0, total numeric(18,2) not null,
 created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz not null default now(), updated_by uuid not null,
 deleted_at timestamptz, deleted_by uuid,
 unique(tenant_id,id), unique(tenant_id,billing_document_id,receipt_item_id,purchase_order_item_id),
 foreign key(tenant_id,billing_document_id) references agro360.procurement_billing_documents(tenant_id,id),
 foreign key(tenant_id,purchase_order_item_id) references agro360.procurement_purchase_order_items(tenant_id,id),
 foreign key(tenant_id,receipt_item_id) references agro360.procurement_receipt_items(tenant_id,id),
 check(quantity>0 and unit_price>=0 and discount>=0 and total=round(quantity*unit_price-discount,2))
);

create table agro360.procurement_invoice_matches (
 id uuid primary key, tenant_id uuid not null, billing_document_id uuid not null, purchase_order_id uuid not null,
 status varchar(24) not null, idempotency_key varchar(100) not null, tolerance_snapshot jsonb not null,
 contracted_total numeric(18,2) not null, accepted_total numeric(18,2) not null, billed_total numeric(18,2) not null,
 difference_total numeric(18,2) not null, difference_percent numeric(12,4),
 decided_at timestamptz, decided_by uuid, decision varchar(24), decision_reason varchar(1000),
 created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz not null default now(), updated_by uuid not null,
 deleted_at timestamptz, deleted_by uuid,
 unique(tenant_id,id), unique(tenant_id,billing_document_id), unique(tenant_id,idempotency_key),
 foreign key(tenant_id,billing_document_id) references agro360.procurement_billing_documents(tenant_id,id),
 foreign key(tenant_id,purchase_order_id) references agro360.procurement_purchase_orders(tenant_id,id),
 check(status in('MATCHED','PENDING_EXCEPTION','REJECTED','CANCELLED')),
 check(decision is null or decision in('APPROVED_EXCEPTION','REJECTED','CORRECTION_REQUESTED'))
);

create table agro360.procurement_match_divergences (
 id uuid primary key, tenant_id uuid not null, invoice_match_id uuid not null, billing_line_id uuid,
 purchase_order_item_id uuid, receipt_item_id uuid, type varchar(24) not null, origin varchar(24) not null,
 description varchar(1000) not null, expected_value numeric(20,6), actual_value numeric(20,6),
 difference_value numeric(20,6) not null, difference_percent numeric(12,4), operational_impact varchar(500) not null,
 required_action varchar(40) not null, responsible_id uuid, due_at timestamptz, evidence_reference varchar(500),
 status varchar(24) not null default 'OPEN', resolution varchar(40), resolution_reason varchar(1000),
 tolerance_snapshot jsonb not null, resolved_at timestamptz, resolved_by uuid,
 created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz not null default now(), updated_by uuid not null,
 deleted_at timestamptz, deleted_by uuid,
 unique(tenant_id,id), foreign key(tenant_id,invoice_match_id) references agro360.procurement_invoice_matches(tenant_id,id),
 foreign key(tenant_id,billing_line_id) references agro360.procurement_billing_lines(tenant_id,id),
 foreign key(tenant_id,purchase_order_item_id) references agro360.procurement_purchase_order_items(tenant_id,id),
 foreign key(tenant_id,receipt_item_id) references agro360.procurement_receipt_items(tenant_id,id),
 check(type in('QUANTITY','UNIT','PRICE','TOTAL','DELIVERY','QUALITY','DOCUMENT')),
 check(origin in('ORDER','RECEIPT','QUALITY','BILLING')), check(status in('OPEN','RESOLVED','REJECTED','CANCELLED')),
 check(length(trim(description))>=3), check(resolution is null or nullif(trim(resolution_reason),'') is not null)
);

create index ix_proc_billing_queue on agro360.procurement_billing_documents(tenant_id,status,issued_on,id);
create index ix_proc_match_divergence_queue on agro360.procurement_match_divergences(tenant_id,status,responsible_id,created_at,id);
do $$ declare t text; begin foreach t in array array['procurement_match_tolerances','procurement_billing_documents','procurement_billing_lines','procurement_invoice_matches','procurement_match_divergences'] loop perform agro360.platform_enable_tenant_rls('agro360.'||t); end loop; end $$;
insert into agro360.platform_schema_versions(version,description,installed_at) values('84.0.0','Conferência persistida de pedido, aceite e cobrança com divergências',now()) on conflict(version) do nothing;
commit;
