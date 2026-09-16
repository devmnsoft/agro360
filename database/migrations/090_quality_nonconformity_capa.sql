begin;

-- Evolução incremental da não conformidade legada: o caso permanece a identidade central.
create sequence if not exists agro360.compliance_nc_number_seq;
alter table agro360.compliance_non_conformities add column if not exists number bigint;
update agro360.compliance_non_conformities set number=nextval('agro360.compliance_nc_number_seq') where number is null;
alter table agro360.compliance_non_conformities alter column number set default nextval('agro360.compliance_nc_number_seq');
alter table agro360.compliance_non_conformities alter column number set not null;
alter table agro360.compliance_non_conformities add column if not exists description text;
alter table agro360.compliance_non_conformities add column if not exists unit_id uuid;
alter table agro360.compliance_non_conformities add column if not exists product_id uuid;
alter table agro360.compliance_non_conformities add column if not exists lot_id uuid;
alter table agro360.compliance_non_conformities add column if not exists affected_quantity numeric(20,6);
alter table agro360.compliance_non_conformities add column if not exists affected_unit varchar(20);
alter table agro360.compliance_non_conformities add column if not exists identified_at timestamptz not null default now();
alter table agro360.compliance_non_conformities add column if not exists idempotency_key varchar(120);
alter table agro360.compliance_non_conformities add column if not exists request_hash char(64);
alter table agro360.compliance_non_conformities add column if not exists containment_status varchar(24) not null default 'NOT_REQUIRED';
alter table agro360.compliance_non_conformities add column if not exists version bigint not null default 1;
alter table agro360.compliance_non_conformities add column if not exists reopened_count integer not null default 0;
alter table agro360.compliance_non_conformities add column if not exists created_by uuid;
alter table agro360.compliance_non_conformities add column if not exists deleted_at timestamptz;
alter table agro360.compliance_non_conformities add column if not exists deleted_by uuid;
alter table agro360.compliance_non_conformities add column if not exists deletion_reason text;
alter table agro360.compliance_non_conformities drop constraint if exists compliance_non_conformities_status_check;
update agro360.compliance_non_conformities set status=case status when 'IN_PROGRESS' then 'IN_TREATMENT' when 'PENDING_APPROVAL' then 'AWAITING_VERIFICATION' else status end where status in('IN_PROGRESS','PENDING_APPROVAL');
alter table agro360.compliance_non_conformities add constraint compliance_non_conformities_status_check check(status in('OPEN','ANALYSIS','IN_TREATMENT','AWAITING_VERIFICATION','CLOSED','CANCELLED')) not valid;
alter table agro360.compliance_non_conformities add constraint compliance_nc_containment_check check(containment_status in('NOT_REQUIRED','PENDING','ACTIVE','PARTIALLY_RELEASED','RELEASED'));
alter table agro360.compliance_non_conformities add constraint compliance_nc_quantity_basis_check check((affected_quantity is null and affected_unit is null) or (affected_quantity>0 and nullif(trim(affected_unit),'') is not null));
create unique index if not exists ux_compliance_nc_number on agro360.compliance_non_conformities(tenant_id,number);
create unique index if not exists ux_compliance_nc_idempotency on agro360.compliance_non_conformities(tenant_id,idempotency_key) where idempotency_key is not null;
create index if not exists ix_compliance_nc_filters on agro360.compliance_non_conformities(tenant_id,status,severity,responsible_id,due_on,identified_at desc) where deleted_at is null;

create table if not exists agro360.compliance_nc_events(
 id uuid primary key, tenant_id uuid not null, non_conformity_id uuid not null, event_type varchar(40) not null,
 from_status varchar(24), to_status varchar(24), reason text, snapshot jsonb not null default '{}'::jsonb,
 occurred_at timestamptz not null default now(), actor_id uuid not null,
 unique(tenant_id,id), foreign key(non_conformity_id) references agro360.compliance_non_conformities(id));
create index if not exists ix_compliance_nc_events_timeline on agro360.compliance_nc_events(tenant_id,non_conformity_id,occurred_at,id);

create table if not exists agro360.compliance_nc_analyses(
 id uuid primary key, tenant_id uuid not null, non_conformity_id uuid not null, version integer not null,
 evidence_reviewed text, hypotheses text, identified_cause text, contributing_factors text, conclusion text not null,
 inconclusive boolean not null default false, inconclusive_reason text, analyzed_by uuid not null, analyzed_at timestamptz not null default now(),
 superseded_at timestamptz, unique(tenant_id,id), unique(tenant_id,non_conformity_id,version),
 foreign key(non_conformity_id) references agro360.compliance_non_conformities(id),
 check(not inconclusive or nullif(trim(inconclusive_reason),'') is not null));

create table if not exists agro360.compliance_nc_actions(
 id uuid primary key, tenant_id uuid not null, non_conformity_id uuid not null,
 type varchar(24) not null check(type in('CONTAINMENT','IMMEDIATE_CORRECTION','CORRECTIVE_ACTION','VERIFICATION')),
 description text not null, responsible_id uuid not null, due_on date not null, priority varchar(12) not null check(priority in('LOW','NORMAL','HIGH','CRITICAL')),
 expected_result text not null, evidence_required boolean not null default false, mandatory boolean not null default true,
 status varchar(20) not null default 'OPEN' check(status in('OPEN','IN_PROGRESS','COMPLETED','CANCELLED')),
 task_id uuid, version bigint not null default 1, completed_at timestamptz, completed_by uuid, completion_result text,
 created_at timestamptz not null default now(), created_by uuid not null, updated_at timestamptz,
 unique(tenant_id,id), foreign key(non_conformity_id) references agro360.compliance_non_conformities(id),
 foreign key(tenant_id,responsible_id) references agro360.identity_users(tenant_id,id),
 check(status<>'COMPLETED' or (completed_at is not null and completed_by is not null and nullif(trim(completion_result),'') is not null)));
create index if not exists ix_compliance_nc_actions_queue on agro360.compliance_nc_actions(tenant_id,responsible_id,status,due_on);

create table if not exists agro360.compliance_nc_action_due_history(
 id uuid primary key, tenant_id uuid not null, action_id uuid not null, previous_due_on date not null, new_due_on date not null,
 reason text not null, changed_at timestamptz not null default now(), changed_by uuid not null,
 foreign key(action_id) references agro360.compliance_nc_actions(id), check(new_due_on<>previous_due_on));

create table if not exists agro360.compliance_nc_verifications(
 id uuid primary key, tenant_id uuid not null, non_conformity_id uuid not null, criterion text not null,
 observation_started_on date, observation_ended_on date, result varchar(16) not null check(result in('EFFECTIVE','INEFFECTIVE','INCONCLUSIVE')),
 justification text not null, verified_by uuid not null, verified_at timestamptz not null default now(),
 action_snapshot jsonb not null, invalidated_at timestamptz, invalidation_reason text,
 unique(tenant_id,id), foreign key(non_conformity_id) references agro360.compliance_non_conformities(id),
 check(observation_ended_on is null or observation_started_on is not null and observation_ended_on>=observation_started_on));

-- Restrições são motivos independentes. Liberar um motivo nunca elimina os demais.
create table if not exists agro360.compliance_lot_restrictions(
 id uuid primary key, tenant_id uuid not null, lot_id uuid not null, non_conformity_id uuid,
 type varchar(24) not null check(type in('BLOCK_LOT','SUSPEND_USE','BLOCK_SHIPMENT','SEND_TO_INSPECTION','SEGREGATE','OTHER')),
 reason text not null, applied_at timestamptz not null default now(), applied_by uuid not null,
 released_at timestamptz, released_by uuid, release_reason text, version bigint not null default 1,
 unique(tenant_id,id), foreign key(lot_id) references agro360.storage_lots(id), foreign key(non_conformity_id) references agro360.compliance_non_conformities(id),
 check((released_at is null and released_by is null and release_reason is null) or (released_at is not null and released_by is not null and nullif(trim(release_reason),'') is not null)));
create index if not exists ix_compliance_lot_active_restrictions on agro360.compliance_lot_restrictions(tenant_id,lot_id,type) where released_at is null;

create table if not exists agro360.compliance_nc_links(
 id uuid primary key, tenant_id uuid not null, non_conformity_id uuid not null, entity_type varchar(32) not null,
 entity_id uuid not null, relation varchar(24) not null default 'ORIGIN', created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,non_conformity_id,entity_type,entity_id,relation), foreign key(non_conformity_id) references agro360.compliance_non_conformities(id));

create or replace function agro360.compliance_lot_has_active_restriction(p_tenant_id uuid,p_lot_id uuid,p_operation text)
returns boolean language sql stable security invoker set search_path=pg_catalog,agro360 as $$
 select exists(select 1 from agro360.compliance_lot_restrictions r where r.tenant_id=p_tenant_id and r.lot_id=p_lot_id and r.released_at is null
 and (r.type='BLOCK_LOT' or (upper(p_operation)='SHIPMENT' and r.type='BLOCK_SHIPMENT') or (upper(p_operation)='USE' and r.type='SUSPEND_USE')))
$$;

do $$ declare t text; begin foreach t in array array['compliance_nc_events','compliance_nc_analyses','compliance_nc_actions','compliance_nc_action_due_history','compliance_nc_verifications','compliance_lot_restrictions','compliance_nc_links'] loop perform agro360.platform_enable_tenant_rls('agro360.'||t); end loop; end $$;
insert into agro360.platform_schema_versions(version,description,installed_at) values('9.0.0','Central de qualidade, não conformidades e ações corretivas',now()) on conflict(version) do nothing;
commit;
