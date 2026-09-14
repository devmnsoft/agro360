begin;
create table if not exists agro360.harvest_closing_runs(
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), season_id uuid not null, farm_id uuid not null,
 cutoff_date date not null, criteria_version varchar(20) not null, issues jsonb not null, idempotency_key varchar(160) not null,
 generated_at timestamptz not null default now(), created_by uuid not null, unique(tenant_id,id), unique(tenant_id,idempotency_key),
 foreign key(tenant_id,season_id) references agro360.agriculture_seasons(tenant_id,id), foreign key(tenant_id,farm_id) references agro360.geo_farms(tenant_id,id),
 check(jsonb_typeof(issues)='array')
);
create table if not exists agro360.harvest_closing_versions(
 id uuid primary key, tenant_id uuid not null references agro360.tenancy_tenants(id), season_id uuid not null, farm_id uuid not null,
 cutoff_date date not null, version integer not null check(version>0), state varchar(20) not null check(state in('PREPARATION','CHECKED','BLOCKED','APPROVED','CLOSED')),
 responsible_id uuid not null, supersedes_id uuid, reason varchar(1000), notes varchar(2000), criteria jsonb not null, indicators jsonb not null, issues jsonb not null,
 idempotency_key varchar(160) not null, generated_at timestamptz not null default now(), closed_at timestamptz, closed_by uuid, row_version bigint not null default 1, created_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,season_id,version), unique(tenant_id,idempotency_key),
 foreign key(tenant_id,season_id) references agro360.agriculture_seasons(tenant_id,id), foreign key(tenant_id,farm_id) references agro360.geo_farms(tenant_id,id),
 foreign key(tenant_id,supersedes_id) references agro360.harvest_closing_versions(tenant_id,id),
 check(jsonb_typeof(criteria)='object' and jsonb_typeof(indicators)='array' and jsonb_typeof(issues)='array'), check(supersedes_id is null or nullif(trim(reason),'') is not null),
 check(state<>'CLOSED' or (closed_at is not null and closed_by is not null))
);
create index if not exists ix_harvest_closing_runs_scope on agro360.harvest_closing_runs(tenant_id,season_id,cutoff_date,generated_at desc);
create index if not exists ix_harvest_closing_versions_scope on agro360.harvest_closing_versions(tenant_id,season_id,version desc);
select agro360.platform_enable_tenant_rls('agro360.harvest_closing_runs');
select agro360.platform_enable_tenant_rls('agro360.harvest_closing_versions');
insert into agro360.platform_schema_versions(version,description,installed_at) values('7.7.0','Conferência e fechamento gerencial versionado da safra',now()) on conflict(version) do nothing;
commit;
