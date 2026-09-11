-- Central de trabalho móvel e sincronização controlada (incremental, aditiva).
begin;

alter table agro360.mobile_devices add column if not exists access_status varchar(16) not null default 'ACTIVE';
alter table agro360.mobile_devices add column if not exists revoked_at timestamptz;
alter table agro360.mobile_devices add column if not exists revoked_by uuid;
alter table agro360.mobile_devices add column if not exists revocation_reason varchar(500);
alter table agro360.mobile_devices add column if not exists server_known_pending int not null default 0;
alter table agro360.mobile_devices add column if not exists server_known_failures int not null default 0;
alter table agro360.mobile_devices drop constraint if exists mobile_devices_access_status_check;
alter table agro360.mobile_devices add constraint mobile_devices_access_status_check
  check(access_status in ('ACTIVE','BLOCKED','REVOKED'));

alter table agro360.mobile_sessions add column if not exists expires_at timestamptz;
update agro360.mobile_sessions set expires_at=started_at+interval '12 hours' where expires_at is null;
alter table agro360.mobile_sessions alter column expires_at set not null;

alter table agro360.mobile_offline_commands add column if not exists contract_version int not null default 1;
alter table agro360.mobile_offline_commands add column if not exists origin_entity_type varchar(80);
alter table agro360.mobile_offline_commands add column if not exists origin_entity_id uuid;
alter table agro360.mobile_offline_commands add column if not exists known_entity_version bigint;
alter table agro360.mobile_offline_commands add column if not exists dependencies jsonb not null default '[]'::jsonb;
alter table agro360.mobile_offline_commands add column if not exists payload_hash char(64);
alter table agro360.mobile_offline_commands add column if not exists result_payload jsonb;
alter table agro360.mobile_offline_commands add column if not exists error_message varchar(1000);
alter table agro360.mobile_offline_commands add column if not exists received_at timestamptz;
alter table agro360.mobile_offline_commands add column if not exists attempts int not null default 0;
alter table agro360.mobile_offline_commands add column if not exists updated_at timestamptz not null default now();
update agro360.mobile_offline_commands
set payload_hash=encode(digest(command_type||'|'||payload::text,'sha256'),'hex')
where payload_hash is null;
alter table agro360.mobile_offline_commands alter column payload_hash set not null;
alter table agro360.mobile_offline_commands drop constraint if exists offline_commands_status_check;
alter table agro360.mobile_offline_commands add constraint offline_commands_status_check
  check(status in ('RECEIVED','PROCESSING','APPLIED','REJECTED','CONFLICT','CANCELLED','PENDING','SYNCING','SYNCED','FAILED'));
alter table agro360.mobile_offline_commands drop constraint if exists mobile_offline_commands_contract_version_check;
alter table agro360.mobile_offline_commands add constraint mobile_offline_commands_contract_version_check check(contract_version=1);
alter table agro360.mobile_offline_commands drop constraint if exists mobile_offline_commands_dependencies_check;
alter table agro360.mobile_offline_commands add constraint mobile_offline_commands_dependencies_check check(jsonb_typeof(dependencies)='array');
drop index if exists agro360.ux_mobile_idempotency_operation;
create unique index if not exists ux_mobile_command_actor_key
  on agro360.mobile_offline_commands(tenant_id,user_id,idempotency_key);
create index if not exists ix_mobile_devices_admin
  on agro360.mobile_devices(tenant_id,access_status,last_seen_at desc) where deleted_at is null;
create index if not exists ix_mobile_sessions_active
  on agro360.mobile_sessions(tenant_id,user_id,device_id,expires_at desc) where ended_at is null;

insert into agro360.platform_schema_versions(version,description,installed_at)
values('7.2.0','Central de trabalho móvel e sincronização controlada',now())
on conflict(version) do nothing;
commit;
