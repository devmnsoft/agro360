-- Sprint 45 - camada SaaS Enterprise (PostgreSQL 15+)
create schema if not exists platform;

alter table identity.users add column if not exists normalized_document varchar(14);
create unique index if not exists ux_identity_users_tenant_document on identity.users(tenant_id, normalized_document) where normalized_document is not null and deleted_at is null;

create table if not exists platform_super_admins (
 id uuid primary key default gen_random_uuid(), user_id uuid not null unique, active boolean not null default true,
 created_at timestamptz not null default now(), updated_at timestamptz, created_by uuid, updated_by uuid, deleted_at timestamptz
);
create unique index if not exists ux_platform_single_active_super_admin on platform_super_admins ((active)) where active and deleted_at is null;

create table if not exists platform_languages (
 culture varchar(10) primary key, native_name varchar(80) not null, active boolean not null default true,
 is_fallback boolean not null default false, created_at timestamptz not null default now(), updated_at timestamptz, created_by uuid, updated_by uuid
);
create unique index if not exists ux_platform_fallback_language on platform_languages ((is_fallback)) where is_fallback;
insert into platform_languages(culture,native_name,is_fallback) values ('pt-BR','Português (Brasil)',true),('en-US','English (United States)',false),('es-ES','Español',false) on conflict do nothing;

create table if not exists platform_saas_plans (
 id uuid primary key default gen_random_uuid(), code varchar(50) not null unique, name varchar(120) not null, description text not null,
 user_limit integer not null check(user_limit>0), property_limit integer not null check(property_limit>0), module_limit integer not null check(module_limit>0),
 storage_limit_mb bigint not null check(storage_limit_mb>0), multilingual boolean not null default false, features jsonb not null default '[]',
 monthly_price numeric(14,2) not null check(monthly_price>=0), annual_price numeric(14,2) not null check(annual_price>=0), active boolean not null default true,
 notes text, created_at timestamptz not null default now(), updated_at timestamptz, created_by uuid, updated_by uuid, deleted_at timestamptz
);

create table if not exists platform_tenants (
 id uuid primary key references tenancy.tenants(id), legal_name varchar(180) not null, trade_name varchar(180), normalized_document varchar(14) not null unique,
 customer_type varchar(40) not null, primary_segment varchar(80) not null, country char(2) not null default 'BR', state varchar(80), city varchar(120),
 primary_email varchar(254) not null, phone varchar(30), legal_contact varchar(160) not null, operational_contact varchar(160), plan_id uuid references platform_saas_plans(id),
 default_language varchar(10) not null default 'pt-BR' references platform_languages(culture), default_currency char(3) not null default 'BRL', time_zone varchar(80) not null default 'America/Sao_Paulo',
 status varchar(20) not null check(status in ('REGISTERING','ACTIVE','IMPLEMENTING','SUSPENDED','BLOCKED','DELINQUENT','CANCELLED','CLOSED')),
 block_reason text, read_only_when_blocked boolean not null default true, notes text, created_at timestamptz not null default now(), updated_at timestamptz,
 created_by uuid, updated_by uuid, deleted_at timestamptz, check(status not in ('BLOCKED','SUSPENDED') or length(trim(block_reason))>=5)
);
create index if not exists ix_platform_tenants_status on platform_tenants(status) where deleted_at is null;

create table if not exists platform_tenant_settings (
 tenant_id uuid primary key references platform_tenants(id), language varchar(10) not null references platform_languages(culture), currency char(3) not null,
 time_zone varchar(80) not null, overdue_blocks_critical_actions boolean not null default false, preferences jsonb not null default '{}',
 created_at timestamptz not null default now(), updated_at timestamptz, created_by uuid, updated_by uuid
);
create table if not exists platform_tenant_status_events (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null references platform_tenants(id), previous_status varchar(20), new_status varchar(20) not null,
 reason text not null check(length(trim(reason))>=5), created_at timestamptz not null default now(), created_by uuid not null
);
create index if not exists ix_platform_tenant_status_events on platform_tenant_status_events(tenant_id,created_at desc);

create table if not exists platform_permissions_catalog (code varchar(100) primary key, module_code varchar(60) not null, name varchar(120) not null, sensitive boolean not null default false, created_at timestamptz not null default now());
create table if not exists platform_profiles (id uuid primary key default gen_random_uuid(), tenant_id uuid not null references platform_tenants(id), name varchar(120) not null check(length(trim(name))>0), is_template boolean not null default false, active boolean not null default true, created_at timestamptz not null default now(), updated_at timestamptz, created_by uuid, updated_by uuid, deleted_at timestamptz, unique(tenant_id,name));
create table if not exists platform_profile_permissions (tenant_id uuid not null references platform_tenants(id), profile_id uuid not null references platform_profiles(id), permission_code varchar(100) not null references platform_permissions_catalog(code), allowed boolean not null, created_at timestamptz not null default now(), updated_at timestamptz, created_by uuid, updated_by uuid, primary key(tenant_id,profile_id,permission_code));
create table if not exists platform_user_profiles (tenant_id uuid not null references platform_tenants(id), user_id uuid not null, profile_id uuid not null references platform_profiles(id), is_primary boolean not null default false, created_at timestamptz not null default now(), created_by uuid, primary key(tenant_id,user_id,profile_id));
create unique index if not exists ux_platform_user_primary_profile on platform_user_profiles(tenant_id,user_id) where is_primary;

create table if not exists platform_module_catalog (id uuid primary key default gen_random_uuid(), code varchar(60) not null unique, name varchar(120) not null, description text not null, active boolean not null default true, created_at timestamptz not null default now(), updated_at timestamptz, created_by uuid, updated_by uuid);
create table if not exists platform_module_dependencies (module_id uuid not null references platform_module_catalog(id), depends_on_id uuid not null references platform_module_catalog(id), primary key(module_id,depends_on_id), check(module_id<>depends_on_id));
create table if not exists platform_tenant_modules (tenant_id uuid not null references platform_tenants(id), module_id uuid not null references platform_module_catalog(id), status varchar(20) not null check(status in ('CONTRACTED','ACTIVE','BLOCKED','TRIAL','DELINQUENT','SUSPENDED')), reason text, activated_at timestamptz, created_at timestamptz not null default now(), updated_at timestamptz, created_by uuid, updated_by uuid, primary key(tenant_id,module_id));
create index if not exists ix_platform_tenant_modules_status on platform_tenant_modules(tenant_id,status);

create table if not exists platform_billing_charges (
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null references platform_tenants(id), plan_id uuid not null references platform_saas_plans(id), competence date not null,
 base_amount numeric(14,2) not null check(base_amount>=0), additional_amount numeric(14,2) not null default 0 check(additional_amount>=0), discount numeric(14,2) not null default 0 check(discount>=0),
 total numeric(14,2) generated always as (base_amount+additional_amount-discount) stored,
 due_on date not null, status varchar(24) not null check(status in ('OPEN','OVERDUE','PAID_EXTERNALLY','CANCELLED','RENEGOTIATED','EXEMPT','UNDER_REVIEW')),
 paid_at timestamptz, external_proof_reference varchar(240), notes text, created_at timestamptz not null default now(), updated_at timestamptz, created_by uuid, updated_by uuid, deleted_at timestamptz,
 check(discount<=base_amount+additional_amount), check(status<>'PAID_EXTERNALLY' or (paid_at is not null and length(trim(notes))>0))
);
create unique index if not exists ux_platform_charge_competence on platform_billing_charges(tenant_id,plan_id,competence) where status<>'CANCELLED' and deleted_at is null;
create index if not exists ix_platform_charges_status_due on platform_billing_charges(tenant_id,status,due_on);
create table if not exists platform_billing_events (id uuid primary key default gen_random_uuid(), tenant_id uuid not null references platform_tenants(id), charge_id uuid not null references platform_billing_charges(id), event_type varchar(40) not null, reason text not null, details jsonb not null default '{}', created_at timestamptz not null default now(), created_by uuid not null);

create table if not exists platform_translations (culture varchar(10) not null references platform_languages(culture), resource_key varchar(180) not null, value text not null, updated_at timestamptz, updated_by uuid, primary key(culture,resource_key));
create table if not exists platform_contextual_help (screen_key varchar(120) not null, culture varchar(10) not null references platform_languages(culture), audience varchar(20) not null check(audience in ('GLOBAL','SUPER_ADMIN','TENANT')), title varchar(160) not null, content text not null, required_permissions text[] not null default '{}', active boolean not null default true, created_at timestamptz not null default now(), updated_at timestamptz, created_by uuid, updated_by uuid, primary key(screen_key,culture,audience));
create table if not exists platform_global_audit_events (id uuid primary key default gen_random_uuid(), tenant_id uuid references platform_tenants(id), actor_id uuid not null, action varchar(100) not null, resource_type varchar(80) not null, resource_id uuid, reason text, safe_details jsonb not null default '{}', correlation_id varchar(100), created_at timestamptz not null default now());
create index if not exists ix_platform_audit_tenant_date on platform_global_audit_events(tenant_id,created_at desc);
create table if not exists platform_support_access_events (id uuid primary key default gen_random_uuid(), tenant_id uuid not null references platform_tenants(id), super_admin_id uuid not null references platform_super_admins(id), reason text not null check(length(trim(reason))>=10), scope text[] not null check(cardinality(scope)>0), started_at timestamptz not null default now(), ended_at timestamptz);
create table if not exists platform_health_events (id uuid primary key default gen_random_uuid(), component varchar(100) not null, status varchar(20) not null, safe_message text not null, occurred_at timestamptz not null default now());
create table if not exists platform_report_exports (id uuid primary key default gen_random_uuid(), tenant_id uuid references platform_tenants(id), requested_by uuid not null, report_code varchar(80) not null, filters jsonb not null default '{}', row_count integer, status varchar(20) not null, created_at timestamptz not null default now(), completed_at timestamptz);

-- Toda tabela tenant-scoped rejeita acesso fora do contexto definido pela API.
do $$ declare t text; begin foreach t in array array['platform_tenant_settings','platform_tenant_status_events','platform_profiles','platform_profile_permissions','platform_user_profiles','platform_tenant_modules','platform_billing_charges','platform_billing_events'] loop execute format('alter table %I enable row level security',t); execute format('drop policy if exists tenant_isolation on %I',t); execute format('create policy tenant_isolation on %I using (tenant_id = nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id = nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',t); end loop; end $$;
create table if not exists platform_tenant_users (tenant_id uuid not null references platform_tenants(id), user_id uuid not null, status varchar(24) not null check(status in ('ACTIVE','INACTIVE','BLOCKED','INVITED','FIRST_ACCESS','SUSPENDED')), job_title varchar(120), phone varchar(30), first_access boolean not null default true, last_access_at timestamptz, created_at timestamptz not null default now(), updated_at timestamptz, created_by uuid, updated_by uuid, deleted_at timestamptz, primary key(tenant_id,user_id));
create table if not exists platform_user_tenant_links (tenant_id uuid not null references platform_tenants(id), user_id uuid not null, active boolean not null default true, created_at timestamptz not null default now(), updated_at timestamptz, created_by uuid, updated_by uuid, primary key(tenant_id,user_id));
create table if not exists platform_tenant_plan_history (id uuid primary key default gen_random_uuid(), tenant_id uuid not null references platform_tenants(id), plan_id uuid not null references platform_saas_plans(id), starts_at timestamptz not null, ends_at timestamptz, reason text not null, created_at timestamptz not null default now(), created_by uuid not null, check(ends_at is null or ends_at>starts_at));
create table if not exists platform_feature_flags (tenant_id uuid not null references platform_tenants(id), feature_code varchar(100) not null, enabled boolean not null, reason text not null, expires_at timestamptz, created_at timestamptz not null default now(), updated_at timestamptz, created_by uuid, updated_by uuid, primary key(tenant_id,feature_code));
create table if not exists platform_user_preferences (tenant_id uuid not null references platform_tenants(id), user_id uuid not null, language varchar(10) not null references platform_languages(culture), time_zone varchar(80) not null, preferences jsonb not null default '{}', created_at timestamptz not null default now(), updated_at timestamptz, created_by uuid, updated_by uuid, primary key(tenant_id,user_id));
create index if not exists ix_platform_tenant_users_status on platform_tenant_users(tenant_id,status) where deleted_at is null;
create index if not exists ix_platform_plan_history on platform_tenant_plan_history(tenant_id,starts_at desc);
do $$ declare t text; begin foreach t in array array['platform_tenant_users','platform_user_tenant_links','platform_tenant_plan_history','platform_feature_flags','platform_user_preferences'] loop execute format('alter table %I enable row level security',t); execute format('drop policy if exists tenant_isolation on %I',t); execute format('create policy tenant_isolation on %I using (tenant_id = nullif(current_setting(''app.tenant_id'',true),'''')::uuid) with check (tenant_id = nullif(current_setting(''app.tenant_id'',true),'''')::uuid)',t); end loop; end $$;
