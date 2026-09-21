begin;

alter table agro360.operational_genealogy_links alter column status set default 'PENDING_REVIEW';

create table if not exists agro360.public_trace_publications (
    id uuid primary key,
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    entity_type varchar(40) not null,
    entity_id uuid not null,
    public_code varchar(64) not null,
    status varchar(20) not null default 'PUBLISHED' check(status in ('PUBLISHED','REVOKED')),
    safe_payload jsonb not null check(jsonb_typeof(safe_payload)='object'),
    idempotency_key varchar(160) not null,
    published_at timestamptz not null,
    revoked_at timestamptz,
    revocation_reason varchar(1000),
    created_at timestamptz not null default now(),
    created_by uuid not null,
    updated_at timestamptz not null default now(),
    updated_by uuid not null,
    unique (tenant_id, id),
    unique (public_code),
    unique (tenant_id, idempotency_key),
    check ((status='PUBLISHED' and revoked_at is null and revocation_reason is null)
        or (status='REVOKED' and revoked_at is not null and length(trim(revocation_reason)) >= 3))
);

create index if not exists ix_public_trace_entity
    on agro360.public_trace_publications(tenant_id, entity_type, entity_id);
create index if not exists ix_public_trace_active_code
    on agro360.public_trace_publications(public_code) where status='PUBLISHED';

select agro360.platform_enable_tenant_rls('agro360.public_trace_publications');

insert into agro360.identity_permissions(code,module,description) values
 ('traceability.publish','Traceability','Publicar e revogar a rastreabilidade pública de lotes aprovados.')
on conflict(code) do update set description=excluded.description;
insert into agro360.identity_role_permissions(tenant_id,role_id,permission_id)
select r.tenant_id,r.id,p.id from agro360.identity_roles r
cross join agro360.identity_permissions p
where lower(r.code) in ('tenant-administrator','tenant_admin','super_admin') and p.code='traceability.publish'
on conflict do nothing;

insert into agro360.platform_schema_versions(version, description, installed_at)
values('9.6.0', 'Rastreabilidade pública segura e pendências operacionais (AG-E6-GEN-002)', now())
on conflict(version) do nothing;

commit;
