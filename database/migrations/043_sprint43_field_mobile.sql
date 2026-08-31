-- Sprint 43 - Campo Mobile PWA, operacao offline e governanca de evidencias
begin;
create schema if not exists field_mobile;
alter table field_operations.occurrences add column if not exists responsible_id uuid;
do $$ begin if not exists(select 1 from pg_constraint where conname='fk_field_occurrences_responsible') then alter table field_operations.occurrences add constraint fk_field_occurrences_responsible foreign key(tenant_id,responsible_id) references identity.users(tenant_id,id); end if; end $$;

create table if not exists field_mobile.field_mobile_profiles(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), user_id uuid not null,
 profile varchar(32) not null check(profile in('PRODUCER','FIELD_TECHNICIAN','AGRICULTURAL_OPERATOR','LIVESTOCK_OPERATOR','WAREHOUSE','PROCUREMENT','QUALITY','PRODUCTION','LOGISTICS','MAINTENANCE','AUDITOR','MANAGER')),
 active boolean not null default true, created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, deleted_at timestamptz,
 unique(tenant_id,user_id), unique(tenant_id,id), foreign key(tenant_id,user_id) references identity.users(tenant_id,id));
create table if not exists field_mobile.field_mobile_shortcuts(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), profile_id uuid not null, code varchar(60) not null, label varchar(100) not null, route varchar(240) not null,
 permission_code varchar(120) not null, position smallint not null default 0 check(position>=0), active boolean not null default true,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, deleted_at timestamptz,
 unique(tenant_id,profile_id,code), foreign key(tenant_id,profile_id) references field_mobile.field_mobile_profiles(tenant_id,id));
create table if not exists field_mobile.field_checklists(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), name varchar(180) not null, category varchar(32) not null check(category in('AGRICULTURE','LIVESTOCK','INVENTORY','PROCUREMENT','PRODUCTION','QUALITY','LOGISTICS','MAINTENANCE','ESG','FISCAL','EXPORT','AUDIT','OPERATIONAL_SAFETY','OTHER')),
 origin_module varchar(60) not null, description text, status varchar(20) not null check(status in('DRAFT','ACTIVE','APPROVED','INACTIVE')), current_version int not null default 1 check(current_version>0),
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, deleted_at timestamptz, unique(tenant_id,id));
create table if not exists field_mobile.field_checklist_versions(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), checklist_id uuid not null, version int not null check(version>0), status varchar(20) not null check(status in('DRAFT','APPROVED','SUPERSEDED')),
 approved_at timestamptz, approved_by uuid, content_hash char(64) not null, created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, unique(tenant_id,checklist_id,version), unique(tenant_id,id), foreign key(tenant_id,checklist_id) references field_mobile.field_checklists(tenant_id,id));
create table if not exists field_mobile.field_checklist_items(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), version_id uuid not null, text varchar(500) not null check(length(trim(text))>0), response_type varchar(24) not null check(response_type in('YES_NO','CONFORMITY','TEXT','INTEGER','DECIMAL','DATE','EVIDENCE','SINGLE_SELECT','MULTI_SELECT')),
 required boolean not null default false, evidence_required boolean not null default false, observation_required boolean not null default false, options jsonb, position int not null check(position>=0),
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, unique(tenant_id,id), unique(tenant_id,version_id,position), foreign key(tenant_id,version_id) references field_mobile.field_checklist_versions(tenant_id,id));
create table if not exists field_mobile.field_checklist_runs(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), checklist_id uuid not null, version_id uuid not null, responsible_id uuid not null, property_id uuid,
 origin_module varchar(60) not null, entity_type varchar(60), entity_id uuid, started_at timestamptz not null default now(), completed_at timestamptz, status varchar(28) not null check(status in('DRAFT','IN_PROGRESS','COMPLETED','PENDING','REJECTED','CANCELLED','SYNC_PENDING','SYNC_CONFLICT')),
 cancellation_reason text, rejection_reason text, signature_required boolean not null default false, location_required boolean not null default false, row_version bigint not null default 1 check(row_version>0),
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, deleted_at timestamptz, unique(tenant_id,id),
 foreign key(tenant_id,checklist_id) references field_mobile.field_checklists(tenant_id,id), foreign key(tenant_id,version_id) references field_mobile.field_checklist_versions(tenant_id,id), foreign key(tenant_id,responsible_id) references identity.users(tenant_id,id), check(status<>'CANCELLED' or length(trim(coalesce(cancellation_reason,'')))>0), check(status<>'REJECTED' or length(trim(coalesce(rejection_reason,'')))>0));
create table if not exists field_mobile.field_checklist_answers(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), run_id uuid not null, item_id uuid not null, answer jsonb, observation text, evidence_id uuid, answered_at timestamptz not null default now(), answered_by uuid not null,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, unique(tenant_id,run_id,item_id), foreign key(tenant_id,run_id) references field_mobile.field_checklist_runs(tenant_id,id), foreign key(tenant_id,item_id) references field_mobile.field_checklist_items(tenant_id,id));
create table if not exists field_mobile.field_evidences(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), type varchar(24) not null check(type in('PHOTO','DOCUMENT','OBSERVATION','SIGNATURE','QR_CODE','LOCATION','REPORT','RECEIPT','OTHER')), description text, origin_module varchar(60) not null, entity_type varchar(60) not null, entity_id uuid not null,
 document_id uuid, storage_reference varchar(500), file_hash char(64), latitude numeric(10,7), longitude numeric(10,7), collected_at timestamptz not null, collected_by uuid not null, status varchar(20) not null check(status in('PENDING','APPROVED','REJECTED','ARCHIVED')), reviewed_at timestamptz, reviewed_by uuid, rejection_reason text,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, deleted_at timestamptz, unique(tenant_id,id), foreign key(tenant_id,collected_by) references identity.users(tenant_id,id), check((latitude is null)=(longitude is null)), check(latitude is null or latitude between -90 and 90), check(longitude is null or longitude between -180 and 180), check(status<>'REJECTED' or length(trim(coalesce(rejection_reason,'')))>0), check(type not in('PHOTO','DOCUMENT','REPORT','RECEIPT') or storage_reference is not null));
create table if not exists field_mobile.field_qr_codes(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), entity_type varchar(32) not null check(entity_type in('LOT','ANIMAL','EQUIPMENT','PROPERTY','PRODUCTION_ORDER','RECEIPT','DOCUMENT','CHECKLIST')), entity_id uuid not null, token_hash char(64) not null, status varchar(20) not null check(status in('ACTIVE','INACTIVE','BLOCKED','EXPIRED')), expires_at timestamptz, last_scanned_at timestamptz,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, deleted_at timestamptz, unique(tenant_id,id), unique(tenant_id,token_hash));
create table if not exists field_mobile.field_quick_records(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), type varchar(40) not null, origin_module varchar(60) not null, property_id uuid, entity_type varchar(60), entity_id uuid, description text not null, priority varchar(12) not null check(priority in('LOW','MEDIUM','HIGH','CRITICAL')), responsible_id uuid, due_at timestamptz, status varchar(24) not null check(status in('OPEN','ANALYSIS','IN_PROGRESS','RESOLVED','CANCELLED','SYNC_PENDING')), resolution_comment text, cancellation_reason text, evidence_required boolean not null default false,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, deleted_at timestamptz, unique(tenant_id,id), foreign key(tenant_id,responsible_id) references identity.users(tenant_id,id), check(priority<>'CRITICAL' or responsible_id is not null), check(status<>'RESOLVED' or length(trim(coalesce(resolution_comment,'')))>0), check(status<>'CANCELLED' or length(trim(coalesce(cancellation_reason,'')))>0));
create table if not exists field_mobile.field_sync_queue(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), idempotency_key varchar(160) not null, operation_type varchar(40) not null, entity_type varchar(60) not null, entity_id uuid, payload jsonb not null, payload_hash char(64) not null, status varchar(20) not null check(status in('PENDING','PROCESSING','SYNCED','ERROR','CONFLICT','CANCELLED')), attempts int not null default 0 check(attempts>=0), last_error varchar(1000), synchronized_at timestamptz, user_id uuid not null,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, unique(tenant_id,user_id,idempotency_key), unique(tenant_id,id));
create table if not exists field_mobile.field_sync_conflicts(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), queue_id uuid not null, local_payload jsonb not null, remote_payload jsonb not null, status varchar(20) not null check(status in('OPEN','RESOLVED','CANCELLED')), resolution varchar(24) check(resolution in('KEEP_LOCAL','KEEP_REMOTE','MERGED','CANCELLED')), resolution_comment text, resolved_at timestamptz, resolved_by uuid,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, unique(tenant_id,id), foreign key(tenant_id,queue_id) references field_mobile.field_sync_queue(tenant_id,id), check(status<>'RESOLVED' or (resolved_by is not null and resolved_at is not null and length(trim(coalesce(resolution_comment,'')))>0)));
create table if not exists field_mobile.field_signatures(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), signer_name varchar(180) not null, signer_document varchar(40), signer_role varchar(100) not null, signed_at timestamptz not null, origin_module varchar(60) not null, entity_type varchar(60) not null, entity_id uuid not null, content_hash char(64) not null, status varchar(20) not null check(status in('VALID','CONTENT_CHANGED','REVOKED')), invalidated_at timestamptz,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, unique(tenant_id,id));
create table if not exists field_mobile.field_locations(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), latitude numeric(10,7), longitude numeric(10,7), accuracy numeric(10,2), collected_at timestamptz not null, source varchar(24) not null check(source in('BROWSER','MANUAL','NOT_AVAILABLE')), permission_status varchar(20) not null check(permission_status in('GRANTED','DENIED','UNAVAILABLE','PROMPT')), entity_type varchar(60) not null, entity_id uuid not null,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, unique(tenant_id,id), check((latitude is null)=(longitude is null)), check(source='NOT_AVAILABLE' or latitude is not null), check(latitude is null or latitude between -90 and 90), check(longitude is null or longitude between -180 and 180), check(accuracy is null or accuracy>=0));
create table if not exists field_mobile.field_report_exports(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), report_type varchar(40) not null, filters jsonb not null default '{}', status varchar(20) not null check(status in('REQUESTED','PROCESSING','READY','ERROR','EXPIRED')), storage_reference varchar(500), row_count int check(row_count>=0), requested_by uuid not null, completed_at timestamptz, error_message varchar(1000),
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid not null, updated_by uuid not null, unique(tenant_id,id));

create index if not exists ix_field_checklists_filter on field_mobile.field_checklists(tenant_id,status,category,origin_module,updated_at desc) where deleted_at is null;
create index if not exists ix_field_runs_filter on field_mobile.field_checklist_runs(tenant_id,status,responsible_id,started_at desc) where deleted_at is null;
create index if not exists ix_field_runs_entity on field_mobile.field_checklist_runs(tenant_id,origin_module,entity_type,entity_id);
create index if not exists ix_field_evidences_filter on field_mobile.field_evidences(tenant_id,status,origin_module,collected_at desc) where deleted_at is null;
create index if not exists ix_field_qr_recent on field_mobile.field_qr_codes(tenant_id,status,created_at desc) where deleted_at is null;
create index if not exists ix_field_records_filter on field_mobile.field_quick_records(tenant_id,status,priority,responsible_id,created_at desc) where deleted_at is null;
create index if not exists ix_field_sync_filter on field_mobile.field_sync_queue(tenant_id,status,user_id,created_at);
create index if not exists ix_field_conflicts_open on field_mobile.field_sync_conflicts(tenant_id,status,created_at desc);
create index if not exists ix_field_signatures_entity on field_mobile.field_signatures(tenant_id,origin_module,entity_type,entity_id,signed_at desc);
create index if not exists ix_field_locations_entity on field_mobile.field_locations(tenant_id,entity_type,entity_id,collected_at desc);
create index if not exists ix_field_exports_user on field_mobile.field_report_exports(tenant_id,requested_by,status,created_at desc);

do $$ declare tab text; begin foreach tab in array array['field_mobile_profiles','field_mobile_shortcuts','field_checklists','field_checklist_versions','field_checklist_items','field_checklist_runs','field_checklist_answers','field_evidences','field_qr_codes','field_quick_records','field_sync_queue','field_sync_conflicts','field_signatures','field_locations','field_report_exports'] loop
 execute format('alter table field_mobile.%I enable row level security',tab); execute format('alter table field_mobile.%I force row level security',tab);
 if not exists(select 1 from pg_policies where schemaname='field_mobile' and tablename=tab and policyname=tab||'_tenant') then execute format('create policy %I on field_mobile.%I using (tenant_id=platform.current_tenant_id()) with check (tenant_id=platform.current_tenant_id())',tab||'_tenant',tab); end if;
end loop; end $$;
insert into identity.permissions(code,module,description) values
 ('field-mobile.read','Campo Mobile','Consultar dashboard, checklists e histórico de campo.'),
 ('field-mobile.write','Campo Mobile','Registrar atividades, evidências e ocorrências.'),
 ('field-mobile.approve','Campo Mobile','Aprovar evidências, checklists e assinaturas gerenciais.'),
 ('field-mobile.sync','Campo Mobile','Sincronizar operações offline.'),
 ('field-mobile.conflicts.resolve','Campo Mobile','Resolver conflitos com trilha de auditoria.'),
 ('field-mobile.reports','Campo Mobile','Exportar relatórios CSV do tenant.')
on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into platform.schema_versions(version,description,installed_at) values('4.3.0','Sprint 43 - Campo Mobile PWA e fluxos offline',now()) on conflict(version) do nothing;
commit;
