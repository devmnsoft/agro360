begin;

-- 9.2.0: modelos de inspeção versionados, execução guiada e agendamento (sem Hangfire).
-- approval_condition é JSON estruturado (passValues/min/max); sem fórmulas/scripts.
-- weight opcional: respostas N/A ficam fora do cálculo de score na aplicação.

create table if not exists agro360.quality_inspection_models(
 id uuid primary key,
 tenant_id uuid not null,
 code varchar(40) not null,
 name varchar(180) not null,
 purpose text,
 process_code varchar(40) not null check(process_code in('PURCHASE_RECEIPT','HARVEST_RECEIPT','PRODUCTION','STORAGE','SHIPMENT','RETURN')),
 product_category varchar(80),
 product_id uuid,
 unit_id uuid,
 instructions text,
 review_responsible_id uuid,
 status varchar(12) not null default 'ACTIVE' check(status in('ACTIVE','INACTIVE')),
 allow_manual_selection boolean not null default false,
 precedence int not null default 100 check(precedence>0),
 current_published_version int,
 row_version bigint not null default 1,
 created_at timestamptz not null default now(),
 created_by uuid,
 updated_at timestamptz not null default now(),
 updated_by uuid,
 deleted_at timestamptz,
 deleted_by uuid,
 deletion_reason text,
 unique(tenant_id,id),
 foreign key(tenant_id,review_responsible_id) references agro360.identity_users(tenant_id,id));
create unique index if not exists ux_quality_inspection_models_code
 on agro360.quality_inspection_models(tenant_id,code) where deleted_at is null;
create index if not exists ix_quality_inspection_models_process
 on agro360.quality_inspection_models(tenant_id,process_code,status,precedence) where deleted_at is null;

create table if not exists agro360.quality_inspection_model_versions(
 id uuid primary key,
 tenant_id uuid not null,
 model_id uuid not null,
 version int not null check(version>0),
 status varchar(16) not null check(status in('DRAFT','IN_REVIEW','PUBLISHED','SUPERSEDED','INACTIVE')),
 valid_from date not null,
 valid_until date,
 change_reason text,
 published_at timestamptz,
 published_by uuid,
 superseded_at timestamptz,
 superseded_by uuid,
 content_hash char(64),
 instructions text,
 review_responsible_id uuid,
 row_version bigint not null default 1,
 created_at timestamptz not null default now(),
 created_by uuid,
 updated_at timestamptz not null default now(),
 updated_by uuid,
 unique(tenant_id,id),
 unique(tenant_id,model_id,version),
 foreign key(tenant_id,model_id) references agro360.quality_inspection_models(tenant_id,id),
 foreign key(tenant_id,review_responsible_id) references agro360.identity_users(tenant_id,id),
 foreign key(tenant_id,published_by) references agro360.identity_users(tenant_id,id),
 foreign key(tenant_id,superseded_by) references agro360.identity_users(tenant_id,id),
 check(valid_until is null or valid_until>=valid_from));
create unique index if not exists ux_quality_inspection_model_versions_published
 on agro360.quality_inspection_model_versions(tenant_id,model_id) where status='PUBLISHED';
create index if not exists ix_quality_inspection_model_versions_status
 on agro360.quality_inspection_model_versions(tenant_id,model_id,status,version);

create table if not exists agro360.quality_inspection_model_sections(
 id uuid primary key,
 tenant_id uuid not null,
 version_id uuid not null,
 code varchar(40) not null,
 name varchar(160) not null,
 sequence int not null,
 instructions text,
 created_at timestamptz not null default now(),
 created_by uuid,
 updated_at timestamptz not null default now(),
 updated_by uuid,
 unique(tenant_id,id),
 unique(tenant_id,version_id,sequence),
 unique(tenant_id,version_id,code),
 foreign key(tenant_id,version_id) references agro360.quality_inspection_model_versions(tenant_id,id));

create table if not exists agro360.quality_inspection_model_criteria(
 id uuid primary key,
 tenant_id uuid not null,
 version_id uuid not null,
 section_id uuid not null,
 stable_key varchar(80) not null,
 name varchar(180) not null,
 explanation text,
 criterion_type varchar(24) not null check(criterion_type in('PASS_FAIL','SINGLE_CHOICE','MULTI_CHOICE','TEXT','NUMBER','DATE','DOCUMENT_EVIDENCE')),
 required boolean not null default true,
 unit varchar(30),
 options jsonb not null default '[]'::jsonb,
 approval_condition jsonb not null default '{}'::jsonb,
 require_justification boolean not null default false,
 require_evidence boolean not null default false,
 criticality varchar(12) not null default 'NORMAL' check(criticality in('LOW','NORMAL','HIGH','CRITICAL')),
 allow_not_applicable boolean not null default false,
 not_applicable_requires_justification boolean not null default false,
 weight numeric(10,4) check(weight is null or weight>0),
 on_fail_create_nc boolean not null default false,
 on_fail_restriction_type varchar(24) check(on_fail_restriction_type is null or on_fail_restriction_type in('BLOCK_LOT','SUSPEND_USE','BLOCK_SHIPMENT','SEND_TO_INSPECTION','SEGREGATE','OTHER')),
 on_fail_create_action boolean not null default false,
 on_fail_require_review boolean not null default false,
 sequence int not null,
 created_at timestamptz not null default now(),
 created_by uuid,
 updated_at timestamptz not null default now(),
 updated_by uuid,
 unique(tenant_id,id),
 unique(tenant_id,version_id,stable_key),
 unique(tenant_id,section_id,sequence),
 foreign key(tenant_id,version_id) references agro360.quality_inspection_model_versions(tenant_id,id),
 foreign key(tenant_id,section_id) references agro360.quality_inspection_model_sections(tenant_id,id));
create index if not exists ix_quality_inspection_model_criteria_section
 on agro360.quality_inspection_model_criteria(tenant_id,section_id,sequence);

create table if not exists agro360.quality_inspection_schedules(
 id uuid primary key,
 tenant_id uuid not null,
 name varchar(180) not null,
 model_id uuid not null,
 process_code varchar(40) not null check(process_code in('PURCHASE_RECEIPT','HARVEST_RECEIPT','PRODUCTION','STORAGE','SHIPMENT','RETURN')),
 schedule_type varchar(16) not null check(schedule_type in('ONCE','PERIODIC','EVENT')),
 timezone varchar(80) not null default 'America/Sao_Paulo',
 cron_expression varchar(80),
 interval_days int check(interval_days is null or interval_days>0),
 event_code varchar(60),
 responsible_id uuid,
 unit_id uuid,
 product_id uuid,
 product_category varchar(80),
 context_label varchar(200),
 notes text,
 starts_on date,
 ends_on date,
 due_within_hours int not null default 24 check(due_within_hours>0),
 status varchar(12) not null default 'ACTIVE' check(status in('ACTIVE','INACTIVE')),
 next_run_at timestamptz,
 last_generated_at timestamptz,
 last_generation_key varchar(120),
 allow_catchup boolean not null default false,
 row_version bigint not null default 1,
 created_at timestamptz not null default now(),
 created_by uuid,
 updated_at timestamptz not null default now(),
 updated_by uuid,
 deleted_at timestamptz,
 deleted_by uuid,
 deletion_reason text,
 unique(tenant_id,id),
 foreign key(tenant_id,model_id) references agro360.quality_inspection_models(tenant_id,id),
 foreign key(tenant_id,responsible_id) references agro360.identity_users(tenant_id,id),
 check(schedule_type<>'PERIODIC' or interval_days is not null or nullif(trim(cron_expression),'') is not null),
 check(schedule_type<>'EVENT' or nullif(trim(event_code),'') is not null),
 check(ends_on is null or starts_on is null or ends_on>=starts_on));
create index if not exists ix_quality_inspection_schedules_due
 on agro360.quality_inspection_schedules(tenant_id,status,next_run_at) where deleted_at is null and status='ACTIVE';

create sequence if not exists agro360.quality_inspection_run_number_seq;
create table if not exists agro360.quality_inspection_runs(
 id uuid primary key,
 tenant_id uuid not null,
 number varchar(40) not null,
 model_id uuid not null,
 model_version_id uuid not null,
 model_version_number int not null check(model_version_number>0),
 process_code varchar(40) not null check(process_code in('PURCHASE_RECEIPT','HARVEST_RECEIPT','PRODUCTION','STORAGE','SHIPMENT','RETURN')),
 origin_type varchar(40) not null default 'MANUAL',
 origin_id uuid,
 origin_label varchar(200),
 product_id uuid,
 lot_id uuid,
 unit_id uuid,
 inspector_id uuid not null,
 started_at timestamptz not null default now(),
 observation_at timestamptz,
 status varchar(20) not null check(status in('IN_PROGRESS','PENDING_REVIEW','COMPLETED','CANCELLED')),
 overall_result varchar(20) check(overall_result is null or overall_result in('CONFORMING','NON_CONFORMING','INCONCLUSIVE')),
 result_summary text,
 determining_criteria jsonb not null default '[]'::jsonb,
 weighted_score_percent numeric(7,2),
 selection_rule text,
 selection_mode varchar(20) not null check(selection_mode in('AUTOMATIC','MANUAL','FORCED_CHOICE')),
 parent_run_id uuid,
 reinspection_reason text,
 last_saved_at timestamptz not null default now(),
 row_version bigint not null default 1,
 completed_at timestamptz,
 completed_by uuid,
 cancelled_at timestamptz,
 cancelled_by uuid,
 cancellation_reason text,
 schedule_id uuid,
 schedule_generation_key varchar(120),
 idempotency_key varchar(120),
 request_hash char(64),
 created_at timestamptz not null default now(),
 created_by uuid,
 updated_at timestamptz not null default now(),
 updated_by uuid,
 deleted_at timestamptz,
 deleted_by uuid,
 deletion_reason text,
 unique(tenant_id,id),
 unique(tenant_id,number),
 foreign key(tenant_id,model_id) references agro360.quality_inspection_models(tenant_id,id),
 foreign key(tenant_id,model_version_id) references agro360.quality_inspection_model_versions(tenant_id,id),
 foreign key(tenant_id,inspector_id) references agro360.identity_users(tenant_id,id),
 foreign key(tenant_id,completed_by) references agro360.identity_users(tenant_id,id),
 foreign key(tenant_id,cancelled_by) references agro360.identity_users(tenant_id,id),
 foreign key(tenant_id,parent_run_id) references agro360.quality_inspection_runs(tenant_id,id),
 foreign key(tenant_id,schedule_id) references agro360.quality_inspection_schedules(tenant_id,id),
 check(status<>'COMPLETED' or (overall_result is not null and completed_at is not null and completed_by is not null)),
 check(status<>'CANCELLED' or (cancelled_at is not null and cancelled_by is not null and nullif(trim(cancellation_reason),'') is not null)),
 check(parent_run_id is null or nullif(trim(reinspection_reason),'') is not null));
create unique index if not exists ux_quality_inspection_runs_idempotency
 on agro360.quality_inspection_runs(tenant_id,idempotency_key) where idempotency_key is not null;
create unique index if not exists ux_quality_inspection_runs_schedule_gen
 on agro360.quality_inspection_runs(tenant_id,schedule_id,schedule_generation_key)
 where schedule_id is not null and schedule_generation_key is not null;
create index if not exists ix_quality_inspection_runs_status
 on agro360.quality_inspection_runs(tenant_id,status,started_at desc) where deleted_at is null;
create index if not exists ix_quality_inspection_runs_origin
 on agro360.quality_inspection_runs(tenant_id,origin_type,origin_id) where deleted_at is null;
create index if not exists ix_quality_inspection_runs_lot
 on agro360.quality_inspection_runs(tenant_id,lot_id,status) where deleted_at is null and lot_id is not null;

create table if not exists agro360.quality_inspection_answers(
 id uuid primary key,
 tenant_id uuid not null,
 run_id uuid not null,
 criterion_id uuid not null,
 stable_key varchar(80) not null,
 not_applicable boolean not null default false,
 text_value text,
 number_value numeric(18,6),
 number_unit varchar(30),
 date_value date,
 choice_values jsonb,
 pass_fail varchar(16),
 evidence_document_id uuid,
 justification text,
 observation text,
 observation_at timestamptz,
 recorded_at timestamptz not null default now(),
 conforming boolean,
 answered_by uuid,
 created_at timestamptz not null default now(),
 created_by uuid,
 updated_at timestamptz not null default now(),
 updated_by uuid,
 unique(tenant_id,id),
 unique(tenant_id,run_id,criterion_id),
 foreign key(tenant_id,run_id) references agro360.quality_inspection_runs(tenant_id,id),
 foreign key(tenant_id,criterion_id) references agro360.quality_inspection_model_criteria(tenant_id,id),
 foreign key(tenant_id,answered_by) references agro360.identity_users(tenant_id,id),
 check(
  not_applicable
  or conforming is null
  or text_value is not null
  or number_value is not null
  or date_value is not null
  or choice_values is not null
  or pass_fail is not null
  or evidence_document_id is not null));
create index if not exists ix_quality_inspection_answers_run
 on agro360.quality_inspection_answers(tenant_id,run_id,stable_key);

create table if not exists agro360.quality_inspection_effects(
 id uuid primary key,
 tenant_id uuid not null,
 run_id uuid not null,
 criterion_id uuid not null,
 effect_type varchar(16) not null check(effect_type in('NC','RESTRICTION','ACTION','REVIEW')),
 non_conformity_id uuid,
 restriction_id uuid,
 action_id uuid,
 status varchar(12) not null check(status in('PENDING','APPLIED','FAILED')),
 error_message text,
 payload jsonb not null default '{}'::jsonb,
 created_at timestamptz not null default now(),
 created_by uuid,
 updated_at timestamptz not null default now(),
 updated_by uuid,
 unique(tenant_id,id),
 unique(tenant_id,run_id,criterion_id,effect_type),
 foreign key(tenant_id,run_id) references agro360.quality_inspection_runs(tenant_id,id),
 foreign key(tenant_id,criterion_id) references agro360.quality_inspection_model_criteria(tenant_id,id));
create index if not exists ix_quality_inspection_effects_run
 on agro360.quality_inspection_effects(tenant_id,run_id,status);

create table if not exists agro360.quality_inspection_model_audits(
 id uuid primary key,
 tenant_id uuid not null,
 model_id uuid not null,
 version_id uuid,
 action varchar(40) not null,
 before_data jsonb not null default '{}'::jsonb,
 after_data jsonb not null default '{}'::jsonb,
 actor_id uuid not null,
 occurred_at timestamptz not null default now(),
 unique(tenant_id,id),
 foreign key(tenant_id,model_id) references agro360.quality_inspection_models(tenant_id,id),
 foreign key(tenant_id,version_id) references agro360.quality_inspection_model_versions(tenant_id,id),
 foreign key(tenant_id,actor_id) references agro360.identity_users(tenant_id,id));
create index if not exists ix_quality_inspection_model_audits_timeline
 on agro360.quality_inspection_model_audits(tenant_id,model_id,occurred_at desc,id);

alter table agro360.quality_inspections add column if not exists model_version_id uuid;
alter table agro360.quality_inspections add column if not exists run_id uuid;
alter table agro360.quality_inspections drop constraint if exists fk_quality_inspections_model_version;
alter table agro360.quality_inspections add constraint fk_quality_inspections_model_version
 foreign key(tenant_id,model_version_id) references agro360.quality_inspection_model_versions(tenant_id,id);
alter table agro360.quality_inspections drop constraint if exists fk_quality_inspections_run;
alter table agro360.quality_inspections add constraint fk_quality_inspections_run
 foreign key(tenant_id,run_id) references agro360.quality_inspection_runs(tenant_id,id);

do $$ declare t text; begin
 foreach t in array array[
  'quality_inspection_models',
  'quality_inspection_model_versions',
  'quality_inspection_model_sections',
  'quality_inspection_model_criteria',
  'quality_inspection_schedules',
  'quality_inspection_runs',
  'quality_inspection_answers',
  'quality_inspection_effects',
  'quality_inspection_model_audits'
 ] loop
  perform agro360.platform_enable_tenant_rls('agro360.'||t);
 end loop;
end $$;

insert into agro360.identity_permissions(code,module,description) values
 ('compliance.inspection-models.write','Compliance','Criar e editar modelos/checklists de inspeção.'),
 ('compliance.inspection-models.publish','Compliance','Publicar, superseder e inativar versões de modelos de inspeção.'),
 ('compliance.inspections.execute','Compliance','Executar inspeções guiadas e registrar respostas.')
on conflict(code) do update set module=excluded.module,description=excluded.description;
insert into agro360.identity_role_permissions(tenant_id,role_id,permission_id)
select r.tenant_id,r.id,p.id from agro360.identity_roles r
cross join agro360.identity_permissions p
where lower(r.code)='tenant-administrator'
  and p.code in('compliance.inspection-models.write','compliance.inspection-models.publish','compliance.inspections.execute')
on conflict do nothing;

insert into agro360.platform_schema_versions(version,description,installed_at)
values('9.2.0','Modelos de inspeção versionados, execução guiada e agendamento',now())
on conflict(version) do nothing;

commit;
