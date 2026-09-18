begin;

-- 9.3.0: intents de inspeção acionados por eventos operacionais (AG-Q-EVT-001).
-- Desacoplamento pós-commit: falha de modelo não cancela o recebimento/apontamento de origem.
-- Idempotência por tenant + chave EVT:{process}:{originId:N}.

create table if not exists agro360.quality_inspection_event_intents(
  id uuid primary key,
  tenant_id uuid not null,
  process_code varchar(40) not null check(process_code in('PURCHASE_RECEIPT','HARVEST_RECEIPT','PRODUCTION','STORAGE','SHIPMENT','RETURN')),
  origin_type varchar(60) not null,
  origin_id uuid not null,
  product_id uuid,
  lot_id uuid,
  unit_id uuid,
  actor_id uuid,
  idempotency_key varchar(120) not null,
  request_hash varchar(128),
  status varchar(24) not null check(status in('PENDING','STARTED','AMBIGUOUS','PENDING_MODEL','SKIPPED_NO_ACTOR')),
  run_id uuid,
  model_id uuid,
  model_version_id uuid,
  notes text,
  row_version bigint not null default 1,
  created_at timestamptz not null default now(),
  created_by uuid,
  updated_at timestamptz not null default now(),
  updated_by uuid,
  deleted_at timestamptz,
  deleted_by uuid,
  deletion_reason text,
  unique(tenant_id, id),
  foreign key(tenant_id, run_id) references agro360.quality_inspection_runs(tenant_id, id));

create unique index if not exists ux_quality_inspection_event_intents_key
  on agro360.quality_inspection_event_intents(tenant_id, idempotency_key)
  where deleted_at is null;

create index if not exists ix_quality_inspection_event_intents_origin
  on agro360.quality_inspection_event_intents(tenant_id, origin_type, origin_id)
  where deleted_at is null;

create index if not exists ix_quality_inspection_event_intents_process_status
  on agro360.quality_inspection_event_intents(tenant_id, process_code, status, created_at desc)
  where deleted_at is null;

select agro360.platform_enable_tenant_rls('agro360.quality_inspection_event_intents');

do $$
begin
  if exists (select 1 from pg_roles where rolname = 'agro360_app') then
    execute 'grant select, insert, update, delete on agro360.quality_inspection_event_intents to agro360_app';
  end if;
exception when undefined_object then
  null;
end $$;

insert into agro360.platform_schema_versions(version, description, installed_at)
values('9.3.0', 'Intents operacionais de inspeção por evento (AG-Q-EVT-001)', now())
on conflict(version) do nothing;

commit;
