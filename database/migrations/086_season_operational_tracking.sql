begin;

-- Operations are children of the existing Agriculture 360 plan record. They do
-- not introduce another season/task aggregate.
create table agro360.agriculture_plan_operations (
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id),
 plan_record_id uuid not null, season_id uuid not null, farm_id uuid not null, field_id uuid not null,
 name varchar(160) not null, operation_type varchar(80) not null,
 planned_start timestamptz not null, planned_end timestamptz not null,
 original_start timestamptz not null, original_end timestamptz not null,
 planned_area_ha numeric(14,4) not null, original_area_ha numeric(14,4) not null,
 planned_hours numeric(14,2), responsible_id uuid, status varchar(24) not null default 'PLANNED',
 outside_season_reason varchar(1000), version bigint not null default 1,
 created_at timestamptz not null default now(), created_by uuid not null,
 updated_at timestamptz not null default now(), updated_by uuid not null,
 deleted_at timestamptz, deleted_by uuid, unique(tenant_id,id),
 foreign key(tenant_id,plan_record_id) references agro360.agriculture_records(tenant_id,id),
 foreign key(tenant_id,season_id) references agro360.agriculture_seasons(tenant_id,id),
 foreign key(tenant_id,farm_id) references agro360.geo_farms(tenant_id,id),
 foreign key(tenant_id,field_id) references agro360.geo_fields(tenant_id,id),
 foreign key(tenant_id,responsible_id) references agro360.identity_users(tenant_id,id),
 check(planned_end>=planned_start), check(original_end>=original_start),
 check(planned_area_ha>0 and original_area_ha>0),
 check(status in('PLANNED','PARTIAL','IN_PROGRESS','COMPLETED','CANCELLED'))
);
create index ix_plan_operations_season on agro360.agriculture_plan_operations(tenant_id,season_id,planned_start,id) where deleted_at is null;

create table agro360.agriculture_operation_dependencies (
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id),
 operation_id uuid not null, predecessor_id uuid not null,
 blocking_type varchar(16) not null, release_condition varchar(24) not null default 'COMPLETED',
 exception_reason varchar(1000), exception_authorized_by uuid, exception_authorized_at timestamptz,
 version bigint not null default 1, created_at timestamptz not null default now(), created_by uuid not null,
 updated_at timestamptz not null default now(), updated_by uuid not null,
 deleted_at timestamptz, deleted_by uuid, unique(tenant_id,id), unique(tenant_id,operation_id,predecessor_id),
 foreign key(tenant_id,operation_id) references agro360.agriculture_plan_operations(tenant_id,id),
 foreign key(tenant_id,predecessor_id) references agro360.agriculture_plan_operations(tenant_id,id),
 foreign key(tenant_id,exception_authorized_by) references agro360.identity_users(tenant_id,id),
 check(operation_id<>predecessor_id), check(blocking_type in('REQUIRED','ADVISORY')),
 check(release_condition in('COMPLETED','REVIEWED')),
 check((exception_authorized_by is null and exception_authorized_at is null and exception_reason is null) or
       (exception_authorized_by is not null and exception_authorized_at is not null and nullif(trim(exception_reason),'') is not null))
);

create table agro360.agriculture_operation_orders (
 tenant_id uuid not null, operation_id uuid not null, work_order_id uuid not null,
 covered_area_ha numeric(14,4) not null, idempotency_key varchar(100) not null,
 created_at timestamptz not null default now(), created_by uuid not null,
 primary key(tenant_id,operation_id,work_order_id), unique(tenant_id,idempotency_key),
 foreign key(tenant_id,operation_id) references agro360.agriculture_plan_operations(tenant_id,id),
 foreign key(tenant_id,work_order_id) references agro360.agriculture_records(tenant_id,id),
 check(covered_area_ha>0)
);

create table agro360.agriculture_plan_revisions (
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id),
 operation_id uuid not null, version bigint not null, reason varchar(1000) not null,
 before_value jsonb not null, after_value jsonb not null, impact jsonb not null,
 created_at timestamptz not null default now(), created_by uuid not null, unique(tenant_id,operation_id,version),
 foreign key(tenant_id,operation_id) references agro360.agriculture_plan_operations(tenant_id,id)
);

do $$ declare t text; begin foreach t in array array['agriculture_plan_operations','agriculture_operation_dependencies','agriculture_operation_orders','agriculture_plan_revisions'] loop perform agro360.platform_enable_tenant_rls('agro360.'||t); end loop; end $$;
insert into agro360.platform_schema_versions(version,description,installed_at) values('86.0.0','Acompanhamento de safra, dependencias e versoes do planejamento',now()) on conflict(version) do nothing;
commit;
