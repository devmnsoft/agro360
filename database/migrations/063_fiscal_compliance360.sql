-- Sprint 63: base segura para adapters fiscais. Idempotente e sem credenciais ou autorizações simuladas.
begin;

alter table agro360.fiscal_documents drop constraint if exists fiscal_documents_status_check;
alter table agro360.fiscal_documents
    add column if not exists provider_key varchar(80),
    add column if not exists provider_document_id varchar(160),
    add column if not exists verification_code varchar(160),
    add column if not exists cancelled_at timestamptz,
    add column if not exists idempotency_key varchar(240);
update agro360.fiscal_documents set idempotency_key = tenant_id::text || ':legacy:' || id::text where idempotency_key is null;
alter table agro360.fiscal_documents alter column idempotency_key set not null;
alter table agro360.fiscal_documents add constraint fiscal_documents_status_check check(status in(
    'DRAFT','PENDING_VALIDATION','READY_TO_SUBMIT','PENDING_SUBMISSION','SUBMITTED',
    'PROVIDER_PENDING','NOT_CONFIGURED','AWAITING_EXTERNAL_ISSUANCE','EXTERNALLY_ISSUED',
    'EXTERNALLY_AUTHORIZED','REJECTED','CANCELLATION_REQUESTED','CANCELLED',
    'SUBSTITUTION_PENDING','SUBSTITUTED','PROVIDER_UNAVAILABLE','FAILED','ARCHIVED'));
create unique index if not exists ux_fiscal_documents_idempotency on agro360.fiscal_documents(tenant_id,idempotency_key);

create table if not exists agro360.fiscal_profiles(
    id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), branch_id uuid,
    legal_name varchar(200) not null, trade_name varchar(200), document_number varchar(20) not null,
    municipal_registration varchar(40), state_registration varchar(40), tax_regime_key varchar(60) not null,
    municipality_code varchar(10) not null, municipality_name varchar(120) not null, state_code char(2) not null,
    provider_key varchar(80), environment varchar(20) not null check(environment in('SANDBOX','HOMOLOGATION','PRODUCTION')),
    credential_reference varchar(240), status varchar(20) not null check(status in('DRAFT','ACTIVE','SUSPENDED','ARCHIVED')),
    created_by uuid, created_at timestamptz not null default now(), updated_at timestamptz not null default now(), deleted_at timestamptz,
    unique(tenant_id,id)
);
create unique index if not exists ux_fiscal_profiles_active_branch on agro360.fiscal_profiles(tenant_id,coalesce(branch_id,'00000000-0000-0000-0000-000000000000'::uuid)) where status='ACTIVE' and deleted_at is null;

create table if not exists agro360.fiscal_provider_configs(
    id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), branch_id uuid,
    provider_key varchar(80) not null, document_type varchar(40) not null, environment varchar(20) not null,
    configuration_json jsonb not null default '{}'::jsonb, credential_reference varchar(240),
    status varchar(20) not null default 'NOT_CONFIGURED', last_health_check_at timestamptz,
    created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
    unique(tenant_id,branch_id,provider_key,document_type,environment),
    check(not (configuration_json ?| array['secret','token','password','apiKey','privateKey','certificatePassword']))
);

create table if not exists agro360.fiscal_provider_attempts(
    id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), fiscal_document_id uuid not null,
    provider_key varchar(80) not null, operation_type varchar(20) not null check(operation_type in('SUBMIT','QUERY','CANCEL')),
    attempt_number integer not null check(attempt_number>0), status varchar(40) not null, http_status_code integer,
    provider_code varchar(120), provider_message varchar(1000), started_at timestamptz not null, finished_at timestamptz,
    trace_id varchar(160), created_at timestamptz not null default now(),
    foreign key(tenant_id,fiscal_document_id) references agro360.fiscal_documents(tenant_id,id),
    unique(tenant_id,fiscal_document_id,operation_type,attempt_number)
);
create index if not exists ix_fiscal_provider_attempts_document on agro360.fiscal_provider_attempts(tenant_id,fiscal_document_id,created_at desc);

insert into agro360.platform_schema_versions(version,description,installed_at)
values('6.3.0','Sprint 63 - Fiscal Compliance 360 adapters seguros',now()) on conflict(version) do nothing;
commit;
