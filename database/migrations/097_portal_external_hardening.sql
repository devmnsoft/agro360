-- 097_portal_external_hardening.sql
-- AG-PORTAL-EXT-001: Portal Externo SaaS, Autoatendimento e Rastreabilidade Segura
begin;

create table if not exists agro360.portal_profiles (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    code varchar(40) not null,
    name varchar(100) not null,
    active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, id),
    unique(tenant_id, code)
);

create table if not exists agro360.portal_permissions (
    id uuid primary key,
    tenant_id uuid not null,
    profile_id uuid not null,
    permission varchar(100) not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, id),
    unique(tenant_id, profile_id, permission),
    foreign key(tenant_id, profile_id) references agro360.portal_profiles(tenant_id, id)
);

create table if not exists agro360.portal_external_users (
    id uuid primary key,
    tenant_id uuid not null,
    profile_id uuid not null,
    name varchar(160) not null,
    email varchar(254) not null,
    password_hash text not null,
    status varchar(20) not null check(status in('PENDING','ACTIVE','BLOCKED','INACTIVE')),
    terms_accepted_at timestamptz,
    last_login_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, id),
    unique(tenant_id, email),
    foreign key(tenant_id, profile_id) references agro360.portal_profiles(tenant_id, id),
    check(email=lower(email) and email~'^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$')
);

create table if not exists agro360.portal_external_user_links (
    id uuid primary key,
    tenant_id uuid not null,
    external_user_id uuid not null,
    entity_type varchar(40) not null,
    entity_id uuid not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, id),
    unique(tenant_id, external_user_id, entity_type, entity_id),
    foreign key(tenant_id, external_user_id) references agro360.portal_external_users(tenant_id, id)
);

create table if not exists agro360.portal_invitations (
    id uuid primary key,
    tenant_id uuid not null,
    profile_id uuid not null,
    name varchar(160) not null,
    email varchar(254) not null,
    entity_type varchar(40) not null,
    entity_id uuid not null,
    entity_label varchar(180) not null,
    token_hash char(64) not null unique,
    expires_at timestamptz not null,
    accepted_at timestamptz,
    accepted_user_id uuid,
    revoked_at timestamptz,
    revoked_by uuid,
    revoke_reason text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, id),
    foreign key(tenant_id, profile_id) references agro360.portal_profiles(tenant_id, id),
    foreign key(tenant_id, accepted_user_id) references agro360.portal_external_users(tenant_id, id),
    check(email=lower(email) and email~'^[^[:space:]@]+@[^[:space:]@]+\.[^[:space:]@]+$'),
    check(accepted_at is null or revoked_at is null),
    check(revoked_at is null or revoke_reason is not null)
);

create table if not exists agro360.portal_terms (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    version varchar(30) not null,
    title varchar(180) not null,
    content text not null,
    active boolean not null default true,
    effective_at timestamptz not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, id),
    unique(tenant_id, version)
);

create table if not exists agro360.portal_terms_acceptances (
    id uuid primary key,
    tenant_id uuid not null,
    external_user_id uuid not null,
    term_id uuid not null,
    accepted_at timestamptz not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    unique(tenant_id, id),
    unique(tenant_id, external_user_id, term_id),
    foreign key(tenant_id, external_user_id) references agro360.portal_external_users(tenant_id, id),
    foreign key(tenant_id, term_id) references agro360.portal_terms(tenant_id, id)
);

create table if not exists agro360.portal_dashboard_cards (
    id uuid primary key,
    tenant_id uuid not null,
    profile_id uuid not null,
    code varchar(60) not null,
    title varchar(120) not null,
    position int not null check(position >= 0),
    active boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, id),
    unique(tenant_id, profile_id, code),
    foreign key(tenant_id, profile_id) references agro360.portal_profiles(tenant_id, id)
);

create table if not exists agro360.portal_announcements (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    title varchar(180) not null,
    summary varchar(500) not null,
    content text not null,
    audience varchar(40) not null,
    severity varchar(15) not null default 'INFO' check(severity in('INFO','SUCCESS','WARNING','CRITICAL')),
    status varchar(20) not null check(status in('DRAFT','PUBLISHED','ARCHIVED')),
    published_at timestamptz,
    expires_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, id),
    check(expires_at is null or published_at is null or expires_at > published_at)
);

create table if not exists agro360.portal_announcement_reads (
    id uuid primary key,
    tenant_id uuid not null,
    announcement_id uuid not null,
    external_user_id uuid not null,
    read_at timestamptz not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    unique(tenant_id, id),
    unique(tenant_id, announcement_id, external_user_id),
    foreign key(tenant_id, announcement_id) references agro360.portal_announcements(tenant_id, id),
    foreign key(tenant_id, external_user_id) references agro360.portal_external_users(tenant_id, id)
);

create table if not exists agro360.portal_requests (
    id uuid primary key,
    tenant_id uuid not null,
    external_user_id uuid not null,
    protocol varchar(30) not null,
    type varchar(40) not null,
    subject varchar(160) not null,
    description text not null,
    status varchar(24) not null check(status in('OPEN','IN_REVIEW','WAITING_RESPONSE','RESOLVED','REJECTED','CANCELLED')),
    priority varchar(12) not null check(priority in('LOW','MEDIUM','HIGH','CRITICAL')),
    resolution text,
    cancellation_reason text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, id),
    unique(tenant_id, protocol),
    foreign key(tenant_id, external_user_id) references agro360.portal_external_users(tenant_id, id),
    check(status not in ('RESOLVED','DONE') or resolution is not null),
    check(status <> 'CANCELLED' or cancellation_reason is not null)
);

create table if not exists agro360.portal_request_events (
    id uuid primary key,
    tenant_id uuid not null,
    request_id uuid not null,
    event_type varchar(40) not null,
    message text not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    unique(tenant_id, id),
    foreign key(tenant_id, request_id) references agro360.portal_requests(tenant_id, id)
);

create table if not exists agro360.portal_messages (
    id uuid primary key,
    tenant_id uuid not null,
    external_user_id uuid not null,
    request_id uuid,
    author_type varchar(12) not null check(author_type in('INTERNAL','EXTERNAL')),
    body text not null,
    read_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, id),
    foreign key(tenant_id, external_user_id) references agro360.portal_external_users(tenant_id, id),
    foreign key(tenant_id, request_id) references agro360.portal_requests(tenant_id, id)
);

create table if not exists agro360.portal_marketplace_catalogs (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    name varchar(160) not null,
    status varchar(20) not null check(status in('DRAFT','PUBLISHED','ARCHIVED')),
    valid_from timestamptz,
    valid_until timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, id)
);

create table if not exists agro360.portal_marketplace_catalog_items (
    id uuid primary key,
    tenant_id uuid not null,
    catalog_id uuid not null,
    product_id uuid,
    display_name varchar(180) not null,
    position int not null default 0,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, id),
    foreign key(tenant_id, catalog_id) references agro360.portal_marketplace_catalogs(tenant_id, id)
);

create table if not exists agro360.portal_marketplace_listings (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    catalog_item_id uuid,
    product_name varchar(180) not null,
    crop varchar(100),
    harvest varchar(80),
    region varchar(120),
    unit varchar(20) not null,
    available_quantity numeric(18,4) not null check(available_quantity >= 0),
    unit_price numeric(18,4) check(unit_price > 0),
    commercial_terms text not null,
    status varchar(20) not null check(status in('DRAFT','AVAILABLE','PAUSED','SOLD_OUT','ARCHIVED')),
    origin_summary varchar(300),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, id),
    foreign key(tenant_id, catalog_item_id) references agro360.portal_marketplace_catalog_items(tenant_id, id)
);

create table if not exists agro360.portal_marketplace_listing_certificates (
    id uuid primary key,
    tenant_id uuid not null,
    listing_id uuid not null,
    certificate_id uuid not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    unique(tenant_id, id),
    unique(tenant_id, listing_id, certificate_id),
    foreign key(tenant_id, listing_id) references agro360.portal_marketplace_listings(tenant_id, id),
    foreign key(tenant_id, certificate_id) references agro360.documents_certificates(tenant_id, id)
);

create table if not exists agro360.portal_marketplace_quote_requests (
    id uuid primary key,
    tenant_id uuid not null,
    external_user_id uuid not null,
    protocol varchar(30) not null,
    contact_name varchar(160) not null,
    contact_email varchar(254) not null,
    notes text,
    status varchar(20) not null check(status in('REQUESTED','IN_REVIEW','PROPOSED','ACCEPTED','REJECTED','CONVERTED','CANCELLED')),
    valid_until timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, id),
    unique(tenant_id, protocol),
    foreign key(tenant_id, external_user_id) references agro360.portal_external_users(tenant_id, id)
);

create table if not exists agro360.portal_marketplace_quote_request_items (
    id uuid primary key,
    tenant_id uuid not null,
    quote_request_id uuid not null,
    listing_id uuid not null,
    quantity numeric(18,4) not null check(quantity > 0),
    unit varchar(20) not null,
    offered_unit_price numeric(18,4) check(offered_unit_price > 0),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    unique(tenant_id, id),
    foreign key(tenant_id, quote_request_id) references agro360.portal_marketplace_quote_requests(tenant_id, id),
    foreign key(tenant_id, listing_id) references agro360.portal_marketplace_listings(tenant_id, id)
);

create table if not exists agro360.portal_marketplace_quote_events (
    id uuid primary key,
    tenant_id uuid not null,
    quote_request_id uuid not null,
    event_type varchar(40) not null,
    notes text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    unique(tenant_id, id),
    foreign key(tenant_id, quote_request_id) references agro360.portal_marketplace_quote_requests(tenant_id, id)
);

create table if not exists agro360.portal_supplier_prequalifications (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    supplier_id uuid not null,
    status varchar(20) not null check(status in('PENDING','IN_REVIEW','APPROVED','REJECTED','BLOCKED')),
    notes text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, id)
);

create table if not exists agro360.portal_supplier_document_requirements (
    id uuid primary key,
    tenant_id uuid not null,
    prequalification_id uuid not null,
    name varchar(160) not null,
    required boolean not null default true,
    status varchar(20) not null check(status in('PENDING','SUBMITTED','APPROVED','REJECTED')),
    due_at timestamptz,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, id),
    foreign key(tenant_id, prequalification_id) references agro360.portal_supplier_prequalifications(tenant_id, id)
);

create table if not exists agro360.portal_transporter_delivery_updates (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    external_user_id uuid not null,
    delivery_id uuid not null,
    status varchar(30) not null check(status in('AWAITING_PICKUP','PICKING_UP','COLLECTED','IN_TRANSIT','DELIVERED','DELIVERY_INCIDENT','CANCELLED')),
    description text,
    evidence_document_id uuid,
    occurred_at timestamptz not null,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    unique(tenant_id, id),
    foreign key(tenant_id, external_user_id) references agro360.portal_external_users(tenant_id, id),
    check(status <> 'DELIVERY_INCIDENT' or description is not null)
);

create table if not exists agro360.portal_external_document_submissions (
    id uuid primary key,
    tenant_id uuid not null,
    external_user_id uuid not null,
    document_id uuid not null,
    entity_type varchar(40) not null,
    entity_id uuid not null,
    status varchar(20) not null check(status in('SUBMITTED','IN_REVIEW','APPROVED','REJECTED')),
    review_notes text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, id),
    foreign key(tenant_id, external_user_id) references agro360.portal_external_users(tenant_id, id)
);

create table if not exists agro360.portal_external_audit_events (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    external_user_id uuid,
    event_type varchar(60) not null,
    entity_type varchar(40),
    entity_id uuid,
    metadata jsonb not null default '{}',
    occurred_at timestamptz not null default now(),
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    unique(tenant_id, id),
    check(jsonb_typeof(metadata) = 'object')
);

create table if not exists agro360.portal_document_permissions (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    document_id uuid not null,
    profile_code varchar(40),
    external_user_id uuid,
    entity_type varchar(40),
    entity_id uuid,
    can_download boolean not null default true,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid,
    updated_by uuid,
    deleted_at timestamptz,
    unique(tenant_id, id)
);

-- Índices operacionais
create index if not exists ix_portal_users_tenant_profile_status on agro360.portal_external_users(tenant_id, profile_id, status) where deleted_at is null;
create index if not exists ix_portal_links_tenant_entity on agro360.portal_external_user_links(tenant_id, entity_type, entity_id) where deleted_at is null;
create index if not exists ix_portal_invitations_tenant_status_date on agro360.portal_invitations(tenant_id, expires_at, revoked_at, accepted_at) where deleted_at is null;
create index if not exists ix_portal_announcements_tenant_audience_date on agro360.portal_announcements(tenant_id, audience, status, published_at desc) where deleted_at is null;
create index if not exists ix_portal_requests_tenant_user_status on agro360.portal_requests(tenant_id, external_user_id, status, updated_at desc) where deleted_at is null;
create index if not exists ix_marketplace_listings_filters on agro360.portal_marketplace_listings(tenant_id, status, crop, region, unit, unit_price) where deleted_at is null;
create index if not exists ix_marketplace_quotes_tenant_user_status on agro360.portal_marketplace_quote_requests(tenant_id, external_user_id, status, created_at desc) where deleted_at is null;
create index if not exists ix_portal_doc_perms_tenant_entity on agro360.portal_document_permissions(tenant_id, entity_type, entity_id) where deleted_at is null;
create index if not exists ix_external_audit_date on agro360.portal_external_audit_events(tenant_id, external_user_id, occurred_at desc);

-- RLS forçado em todas as tabelas do Portal
select agro360.platform_enable_tenant_rls('agro360.portal_profiles');
select agro360.platform_enable_tenant_rls('agro360.portal_permissions');
select agro360.platform_enable_tenant_rls('agro360.portal_external_users');
select agro360.platform_enable_tenant_rls('agro360.portal_external_user_links');
select agro360.platform_enable_tenant_rls('agro360.portal_invitations');
select agro360.platform_enable_tenant_rls('agro360.portal_terms');
select agro360.platform_enable_tenant_rls('agro360.portal_terms_acceptances');
select agro360.platform_enable_tenant_rls('agro360.portal_dashboard_cards');
select agro360.platform_enable_tenant_rls('agro360.portal_announcements');
select agro360.platform_enable_tenant_rls('agro360.portal_announcement_reads');
select agro360.platform_enable_tenant_rls('agro360.portal_requests');
select agro360.platform_enable_tenant_rls('agro360.portal_request_events');
select agro360.platform_enable_tenant_rls('agro360.portal_messages');
select agro360.platform_enable_tenant_rls('agro360.portal_marketplace_catalogs');
select agro360.platform_enable_tenant_rls('agro360.portal_marketplace_catalog_items');
select agro360.platform_enable_tenant_rls('agro360.portal_marketplace_listings');
select agro360.platform_enable_tenant_rls('agro360.portal_marketplace_listing_certificates');
select agro360.platform_enable_tenant_rls('agro360.portal_marketplace_quote_requests');
select agro360.platform_enable_tenant_rls('agro360.portal_marketplace_quote_request_items');
select agro360.platform_enable_tenant_rls('agro360.portal_marketplace_quote_events');
select agro360.platform_enable_tenant_rls('agro360.portal_supplier_prequalifications');
select agro360.platform_enable_tenant_rls('agro360.portal_supplier_document_requirements');
select agro360.platform_enable_tenant_rls('agro360.portal_transporter_delivery_updates');
select agro360.platform_enable_tenant_rls('agro360.portal_external_document_submissions');
select agro360.platform_enable_tenant_rls('agro360.portal_external_audit_events');
select agro360.platform_enable_tenant_rls('agro360.portal_document_permissions');

-- Grants para role operacional
do $$
begin
    if exists (select 1 from pg_roles where rolname = 'agro360_app') then
        execute 'grant select, insert, update, delete on all tables in schema agro360 to agro360_app';
    end if;
end $$;

-- Perfis padrão
insert into agro360.portal_profiles(id, tenant_id, code, name, created_by)
select gen_random_uuid(), t.id, p.code, p.name, null
from agro360.tenancy_tenants t
cross join (values
    ('PRODUCER', 'Produtor'),
    ('COOPERATIVE_MEMBER', 'Cooperado'),
    ('B2B_CUSTOMER', 'Cliente B2B'),
    ('BUYER', 'Comprador'),
    ('SUPPLIER', 'Fornecedor'),
    ('TRANSPORTER', 'Transportador'),
    ('EXTERNAL_REPRESENTATIVE', 'Representante externo'),
    ('EXTERNAL_AUDITOR', 'Auditor externo'),
    ('PARTNER_TECHNICIAN', 'Técnico parceiro')
) p(code, name)
on conflict(tenant_id, code) do nothing;

-- Termos padrão de uso
insert into agro360.portal_terms(id, tenant_id, version, title, content, active, effective_at)
select gen_random_uuid(), id, '1.0', 'Termos de uso do Portal Agro360',
    'Uso restrito às operações autorizadas pela organização. O acesso é pessoal, auditável e sujeito à política de privacidade.', true, now()
from agro360.tenancy_tenants
on conflict(tenant_id, version) do nothing;

-- Registro de versão do schema
insert into agro360.platform_schema_versions(version, description, installed_at)
values('9.7.0', 'Portal Externo SaaS, Autoatendimento e Rastreabilidade Segura (AG-PORTAL-EXT-001)', now())
on conflict(version) do update set description=excluded.description;

commit;
