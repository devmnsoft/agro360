-- Sprint 50 — UX, formulários, mensagens, ajuda e eventos auditáveis (PostgreSQL 15+)
create schema if not exists agro360;

create table if not exists agro360.ui_contextual_help (
    id uuid primary key default gen_random_uuid(), tenant_id uuid references agro360.tenancy_tenants(id),
    page_key varchar(100) not null, module varchar(60) not null,
    culture varchar(5) not null check (culture in ('pt-BR', 'en-US', 'es-ES')),
    audience varchar(24) not null check (audience in ('ADMIN', 'TENANT', 'OPERATIONAL')),
    title varchar(160) not null, content text not null,
    status varchar(20) not null default 'ACTIVE' check (status in ('DRAFT', 'ACTIVE', 'INACTIVE')),
    created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
    created_by uuid references agro360.identity_users(id), updated_by uuid references agro360.identity_users(id), deleted_at timestamptz,
    unique nulls not distinct (tenant_id, page_key, culture, audience)
);

create table if not exists agro360.ui_message_templates (
    id uuid primary key default gen_random_uuid(), tenant_id uuid references agro360.tenancy_tenants(id),
    code varchar(100) not null, module varchar(60) not null,
    culture varchar(5) not null check (culture in ('pt-BR', 'en-US', 'es-ES')),
    message_type varchar(24) not null check (message_type in ('SUCCESS', 'ERROR', 'WARNING', 'INFO', 'CONFIRMATION', 'BLOCK', 'PERMISSION_DENIED', 'VALIDATION', 'EVENT_RECORDED', 'CRITICAL_ACTION')),
    title varchar(160) not null, message text not null,
    status varchar(20) not null default 'ACTIVE' check (status in ('DRAFT', 'ACTIVE', 'INACTIVE')),
    created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
    created_by uuid references agro360.identity_users(id), updated_by uuid references agro360.identity_users(id), deleted_at timestamptz,
    unique nulls not distinct (tenant_id, code, culture)
);

create table if not exists agro360.ui_form_validation_rules (
    id uuid primary key default gen_random_uuid(), tenant_id uuid references agro360.tenancy_tenants(id),
    page_key varchar(100) not null, field_key varchar(100) not null, rule_type varchar(30) not null,
    parameters jsonb not null default '{}', backend_rule varchar(160) not null, message_code varchar(100) not null,
    status varchar(20) not null default 'ACTIVE', created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
    created_by uuid references agro360.identity_users(id), updated_by uuid references agro360.identity_users(id), deleted_at timestamptz,
    unique nulls not distinct (tenant_id, page_key, field_key, rule_type)
);

create table if not exists agro360.ui_action_confirmations (
    id uuid primary key default gen_random_uuid(), tenant_id uuid references agro360.tenancy_tenants(id),
    action_key varchar(100) not null, module varchar(60) not null,
    culture varchar(5) not null check (culture in ('pt-BR', 'en-US', 'es-ES')),
    title varchar(160) not null, consequence text not null, reason_required boolean not null default false,
    permission varchar(120) not null, audit_action varchar(100) not null,
    status varchar(20) not null default 'ACTIVE', created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
    created_by uuid references agro360.identity_users(id), updated_by uuid references agro360.identity_users(id), deleted_at timestamptz,
    unique nulls not distinct (tenant_id, action_key, culture)
);

create table if not exists agro360.ui_page_events (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null references agro360.tenancy_tenants(id),
    page_key varchar(100) not null, module varchar(60) not null, event_type varchar(40) not null,
    entity_type varchar(80), entity_id uuid, details jsonb not null default '{}', correlation_id varchar(100),
    created_at timestamptz not null default now(), created_by uuid references agro360.identity_users(id)
);

create table if not exists agro360.ui_feedback_events (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null references agro360.tenancy_tenants(id),
    page_key varchar(100) not null, feedback_type varchar(30) not null, message varchar(1000) not null,
    created_at timestamptz not null default now(), created_by uuid references agro360.identity_users(id)
);

create table if not exists agro360.ui_validation_audit (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null references agro360.tenancy_tenants(id),
    page_key varchar(100) not null, field_key varchar(100), rule_type varchar(30) not null, accepted boolean not null,
    correlation_id varchar(100), created_at timestamptz not null default now(), created_by uuid references agro360.identity_users(id)
);

create table if not exists agro360.ui_component_audit (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null references agro360.tenancy_tenants(id),
    page_key varchar(100) not null, component_key varchar(100) not null, event_type varchar(40) not null,
    created_at timestamptz not null default now(), created_by uuid references agro360.identity_users(id)
);

create table if not exists agro360.ui_report_exports (
    id uuid primary key default gen_random_uuid(), tenant_id uuid not null references agro360.tenancy_tenants(id),
    page_key varchar(100) not null, module varchar(60) not null,
    format varchar(10) not null check (format in ('CSV', 'XLSX', 'PDF', 'JSON')),
    filters jsonb not null default '{}', status varchar(20) not null, storage_key varchar(500),
    created_at timestamptz not null default now(), updated_at timestamptz not null default now(),
    created_by uuid not null references agro360.identity_users(id), updated_by uuid references agro360.identity_users(id)
);

create index if not exists ix_ui_help_lookup on agro360.ui_contextual_help(tenant_id, page_key, culture, audience, status) where deleted_at is null;
create index if not exists ix_ui_messages_lookup on agro360.ui_message_templates(tenant_id, module, code, culture, status) where deleted_at is null;
create index if not exists ix_ui_rules_lookup on agro360.ui_form_validation_rules(tenant_id, page_key, field_key, status) where deleted_at is null;
create index if not exists ix_ui_confirmations_lookup on agro360.ui_action_confirmations(tenant_id, module, action_key, culture, status) where deleted_at is null;
create index if not exists ix_ui_page_events_audit on agro360.ui_page_events(tenant_id, module, page_key, created_at desc);
create index if not exists ix_ui_validation_audit on agro360.ui_validation_audit(tenant_id, page_key, created_at desc);

do $$
declare
    item text;
begin
    foreach item in array array[
        'ui_contextual_help', 'ui_message_templates', 'ui_form_validation_rules',
        'ui_action_confirmations', 'ui_page_events', 'ui_feedback_events',
        'ui_validation_audit', 'ui_component_audit', 'ui_report_exports'
    ] loop
        execute format('alter table agro360.%I enable row level security', item);
        execute format('alter table agro360.%I force row level security', item);
        execute format('drop policy if exists tenant_isolation on agro360.%I', item);
        execute format(
            'create policy tenant_isolation on agro360.%I using (tenant_id is null or tenant_id = agro360.platform_current_tenant_id()) with check (tenant_id is null or tenant_id = agro360.platform_current_tenant_id())',
            item
        );
    end loop;
end $$;

insert into agro360.platform_schema_versions(version, description)
values ('5.0.0', 'Sprint 50 - formulários, validações, mensagens e ajuda contextual')
on conflict (version) do nothing;
