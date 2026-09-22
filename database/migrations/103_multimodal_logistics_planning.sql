begin;

-- Planejamento não reserva nem baixa estoque. A alocação referencia a expedição
-- canônica e somente impede que seu saldo operacional seja programado duas vezes.
do $$ begin
 if not exists(select 1 from pg_constraint where conname='uq_logistics_trips_tenant_id') then
  alter table agro360.logistics_trips add constraint uq_logistics_trips_tenant_id unique(tenant_id,id);
 end if;
end $$;

create table if not exists agro360.logistics_trip_plans(
 id uuid primary key, tenant_id uuid not null, trip_id uuid not null,
 idempotency_key varchar(120) not null, request_hash char(64) not null,
 version bigint not null default 1, created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,trip_id), unique(tenant_id,idempotency_key),
 foreign key(tenant_id,trip_id) references agro360.logistics_trips(tenant_id,id));

create table if not exists agro360.logistics_trip_stops(
 id uuid primary key, tenant_id uuid not null, trip_id uuid not null, sequence integer not null check(sequence>0),
 type varchar(24) not null check(type in('ORIGIN','PICKUP','TRANSFER','DELIVERY','RETURN','DESTINATION')),
 name varchar(240) not null, operational_window text, planned_arrival timestamptz, planned_departure timestamptz,
 actual_arrival timestamptz, actual_departure timestamptz, created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,trip_id,sequence),
 foreign key(tenant_id,trip_id) references agro360.logistics_trips(tenant_id,id),
 check(planned_departure is null or planned_arrival is null or planned_departure>=planned_arrival),
 check(actual_departure is null or actual_arrival is null or actual_departure>=actual_arrival));

create table if not exists agro360.logistics_trip_legs(
 id uuid primary key, tenant_id uuid not null, trip_id uuid not null, sequence integer not null check(sequence>0),
 origin_stop_sequence integer not null, destination_stop_sequence integer not null,
 mode varchar(16) not null check(mode in('ROAD','RIVER','MIXED')), asset_id uuid,
 capacity_total numeric(20,6) not null check(capacity_total>0), capacity_unit varchar(20) not null,
 navigation_source text, navigation_valid_until timestamptz, navigation_responsible_id uuid,
 restrictions text, created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,trip_id,sequence),
 foreign key(tenant_id,trip_id) references agro360.logistics_trips(tenant_id,id),
 check(origin_stop_sequence<destination_stop_sequence),
 check(mode<>'RIVER' or (navigation_source is not null and navigation_valid_until is not null and navigation_responsible_id is not null)));

create table if not exists agro360.logistics_trip_allocations(
 id uuid primary key, tenant_id uuid not null, trip_id uuid not null, shipment_item_id uuid not null,
 quantity numeric(20,6) not null check(quantity>0), unit varchar(20) not null,
 loading_stop_sequence integer not null, unloading_stop_sequence integer not null,
 weight numeric(20,6), weight_unit varchar(20), volume numeric(20,6), volume_unit varchar(20),
 status varchar(20) not null check(status in('PLANNED','LOADED','IN_TRANSIT','UNLOADED','CANCELLED')),
 created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,id), foreign key(tenant_id,trip_id) references agro360.logistics_trips(tenant_id,id),
 foreign key(tenant_id,shipment_item_id) references agro360.fulfillment_shipment_items(tenant_id,id),
 check(loading_stop_sequence<unloading_stop_sequence),
 check((weight is null and weight_unit is null) or (weight>=0 and weight_unit is not null)),
 check((volume is null and volume_unit is null) or (volume>=0 and volume_unit is not null)));

create table if not exists agro360.logistics_trip_revisions(
 id uuid primary key, tenant_id uuid not null, trip_id uuid not null, version bigint not null,
 reason text not null, previous_plan jsonb not null, revised_plan jsonb not null,
 created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,trip_id,version),
 foreign key(tenant_id,trip_id) references agro360.logistics_trips(tenant_id,id));

create index if not exists ix_logistics_trip_allocations_pending on agro360.logistics_trip_allocations(tenant_id,shipment_item_id) where status<>'CANCELLED';
create index if not exists ix_logistics_trip_legs_asset_period on agro360.logistics_trip_legs(tenant_id,asset_id) where asset_id is not null;
create index if not exists ix_logistics_trip_stops_schedule on agro360.logistics_trip_stops(tenant_id,planned_arrival);

do $$ declare t text; begin
 foreach t in array array['logistics_trip_plans','logistics_trip_stops','logistics_trip_legs','logistics_trip_allocations','logistics_trip_revisions'] loop
  perform agro360.platform_enable_tenant_rls('agro360.'||t);
 end loop;
end $$;

grant select,insert,update,delete on
 agro360.logistics_trip_plans,agro360.logistics_trip_stops,agro360.logistics_trip_legs,
 agro360.logistics_trip_allocations,agro360.logistics_trip_revisions to agro360_app;

insert into agro360.platform_schema_versions(version,description,installed_at)
values('10.3.0','Planejamento logístico multimodal, paradas, trechos e alocações concorrentes',now())
on conflict(version) do nothing;
commit;
