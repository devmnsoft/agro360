begin;

-- Interaction is user state only. Resolution always derives from the source operation.
create table if not exists agro360.operation_occurrence_states(
 tenant_id uuid not null references agro360.tenancy_tenants(id), occurrence_key varchar(180) not null,
 viewed_at timestamptz, viewed_by uuid, assigned_to uuid, assigned_at timestamptz,
 created_at timestamptz not null default now(), created_by uuid not null,
 updated_at timestamptz not null default now(), updated_by uuid not null,
 primary key(tenant_id,occurrence_key),
 foreign key(tenant_id,viewed_by) references agro360.identity_users(tenant_id,id),
 foreign key(tenant_id,assigned_to) references agro360.identity_users(tenant_id,id),
 check((assigned_to is null)=(assigned_at is null))
);
create table if not exists agro360.operation_occurrence_events(
 id uuid primary key default gen_random_uuid(), tenant_id uuid not null, occurrence_key varchar(180) not null,
 event_type varchar(20) not null check(event_type in('VIEWED','ASSIGNED')), actor_id uuid not null,
 responsible_id uuid, occurred_at timestamptz not null default now(),
 foreign key(tenant_id,occurrence_key) references agro360.operation_occurrence_states(tenant_id,occurrence_key),
 foreign key(tenant_id,actor_id) references agro360.identity_users(tenant_id,id),
 foreign key(tenant_id,responsible_id) references agro360.identity_users(tenant_id,id)
);
create index if not exists ix_operation_occurrence_assignment on agro360.operation_occurrence_states(tenant_id,assigned_to,updated_at desc);
create index if not exists ix_operation_occurrence_events on agro360.operation_occurrence_events(tenant_id,occurrence_key,occurred_at desc);
select agro360.platform_enable_tenant_rls('agro360.operation_occurrence_states');
select agro360.platform_enable_tenant_rls('agro360.operation_occurrence_events');
insert into agro360.platform_schema_versions(version,description,installed_at)
values('7.3.0','Central de Operações derivada e estado de interação auditável',now()) on conflict(version) do nothing;
commit;
