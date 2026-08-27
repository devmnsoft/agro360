-- Sprint 25: BI executivo, relatórios, mapas operacionais e preferências de UI.
begin;
create schema if not exists bi;
create schema if not exists geo;
create schema if not exists ui;

insert into identity.permissions(code,module,description) values
('bi.read','Inteligência Agro360','Consultar dashboards e relatórios gerenciais.'),
('bi.export','Inteligência Agro360','Exportar relatórios gerenciais autorizados.'),
('bi.manage','Inteligência Agro360','Gerenciar filtros, widgets e definições de relatório.'),
('geo.read','Mapas','Consultar camadas e objetos geográficos operacionais.'),
('geo.manage','Mapas','Gerenciar camadas, áreas e rotas geográficas.')
on conflict(code) do update set module=excluded.module,description=excluded.description;

create table if not exists bi.saved_filters(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), user_id uuid not null,
 name varchar(120) not null, module varchar(40) not null, filters jsonb not null default '{}', is_default boolean not null default false,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), unique(tenant_id,user_id,module,name), check(jsonb_typeof(filters)='object'));
create table if not exists bi.dashboard_widgets(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), code varchar(80) not null, title varchar(160) not null,
 module varchar(40) not null, visualization varchar(20) not null check(visualization in('KPI','LINE','BAR','DONUT','RANKING','TABLE')),
 query_key varchar(100) not null, configuration jsonb not null default '{}', enabled boolean not null default true,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), unique(tenant_id,code), check(jsonb_typeof(configuration)='object'));
create table if not exists bi.report_definitions(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), code varchar(80) not null, name varchar(160) not null,
 module varchar(40) not null, description text, query_key varchar(100) not null, allowed_formats text[] not null default '{CSV}', enabled boolean not null default true,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), unique(tenant_id,code), check(cardinality(allowed_formats)>0));
create table if not exists bi.report_exports(
 id uuid primary key, tenant_id uuid not null, report_definition_id uuid not null, requested_by uuid not null,
 format varchar(10) not null check(format in('CSV','PDF')), status varchar(20) not null check(status in('PENDING','PROCESSING','COMPLETED','FAILED','EXPIRED')),
 filters jsonb not null default '{}', storage_key varchar(500), row_count int check(row_count>=0), error_message varchar(500), expires_at timestamptz,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), foreign key(tenant_id,report_definition_id) references bi.report_definitions(tenant_id,id), check(jsonb_typeof(filters)='object'));
create table if not exists bi.user_dashboard_preferences(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), user_id uuid not null, dashboard varchar(50) not null,
 widget_order jsonb not null default '[]', preferences jsonb not null default '{}',
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), unique(tenant_id,user_id,dashboard), check(jsonb_typeof(widget_order)='array'), check(jsonb_typeof(preferences)='object'));

create table if not exists geo.geo_layers(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), name varchar(140) not null, layer_type varchar(30) not null,
 color char(7) not null default '#43A276' check(color~'^#[0-9A-Fa-f]{6}$'), visible boolean not null default true,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), unique(tenant_id,name));
create table if not exists geo.geo_locations(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), layer_id uuid, entity_type varchar(40) not null, entity_id uuid,
 name varchar(180) not null, latitude numeric(9,6) not null check(latitude between -90 and 90), longitude numeric(9,6) not null check(longitude between -180 and 180),
 metadata jsonb not null default '{}', observed_at timestamptz, status varchar(30) not null default 'ACTIVE',
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), foreign key(tenant_id,layer_id) references geo.geo_layers(tenant_id,id), check(jsonb_typeof(metadata)='object'));
create table if not exists geo.geo_areas(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), layer_id uuid, entity_type varchar(40) not null, entity_id uuid,
 name varchar(180) not null, boundary jsonb not null, area_hectares numeric(16,4) check(area_hectares>=0), status varchar(30) not null default 'ACTIVE',
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), foreign key(tenant_id,layer_id) references geo.geo_layers(tenant_id,id), check(jsonb_typeof(boundary)='object'));
create table if not exists geo.geo_routes(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), layer_id uuid, name varchar(180) not null,
 route_type varchar(30) not null check(route_type in('ROAD','RURAL_ROAD','RIVER','COLLECTION','DELIVERY','FIELD')),
 status varchar(20) not null check(status in('DRAFT','ACTIVE','INACTIVE')), distance_km numeric(14,3) check(distance_km>=0),
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), foreign key(tenant_id,layer_id) references geo.geo_layers(tenant_id,id));
create table if not exists geo.geo_route_points(
 id uuid primary key, tenant_id uuid not null, route_id uuid not null, sequence int not null check(sequence>=0), name varchar(180),
 point_type varchar(30) not null check(point_type in('WAYPOINT','COLLECTION','DELIVERY','OCCURRENCE')),
 latitude numeric(9,6) not null check(latitude between -90 and 90), longitude numeric(9,6) not null check(longitude between -180 and 180), occurred_at timestamptz,
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid, deleted_at timestamptz,
 unique(tenant_id,id), unique(tenant_id,route_id,sequence), foreign key(tenant_id,route_id) references geo.geo_routes(tenant_id,id));
create table if not exists ui.ui_audit_events(
 id uuid primary key, tenant_id uuid not null references tenancy.tenants(id), user_id uuid, event_type varchar(60) not null,
 route varchar(240) not null, component varchar(100), metadata jsonb not null default '{}', occurred_at timestamptz not null default now(),
 created_at timestamptz not null default now(), updated_at timestamptz not null default now(), created_by uuid, updated_by uuid,
 unique(tenant_id,id), check(jsonb_typeof(metadata)='object'));

create index if not exists ix_bi_saved_filters_tenant_user on bi.saved_filters(tenant_id,user_id,module) where deleted_at is null;
create index if not exists ix_bi_widgets_tenant_module on bi.dashboard_widgets(tenant_id,module,enabled) where deleted_at is null;
create index if not exists ix_bi_reports_tenant_module on bi.report_definitions(tenant_id,module,enabled) where deleted_at is null;
create index if not exists ix_bi_exports_tenant_status_date on bi.report_exports(tenant_id,status,created_at desc) where deleted_at is null;
create index if not exists ix_bi_preferences_tenant_user on bi.user_dashboard_preferences(tenant_id,user_id,dashboard) where deleted_at is null;
create index if not exists ix_geo_locations_tenant_type_status on geo.geo_locations(tenant_id,entity_type,status) where deleted_at is null;
create index if not exists ix_geo_locations_tenant_coordinates on geo.geo_locations(tenant_id,latitude,longitude) where deleted_at is null;
create index if not exists ix_geo_areas_tenant_type_status on geo.geo_areas(tenant_id,entity_type,status) where deleted_at is null;
create index if not exists ix_geo_routes_tenant_type_status on geo.geo_routes(tenant_id,route_type,status) where deleted_at is null;
create index if not exists ix_geo_route_points_tenant_route on geo.geo_route_points(tenant_id,route_id,sequence) where deleted_at is null;
create index if not exists ix_ui_audit_tenant_date_type on ui.ui_audit_events(tenant_id,occurred_at desc,event_type);

do $$ declare item record; begin
 for item in select * from (values ('bi','saved_filters'),('bi','dashboard_widgets'),('bi','report_definitions'),('bi','report_exports'),('bi','user_dashboard_preferences'),('geo','geo_layers'),('geo','geo_locations'),('geo','geo_areas'),('geo','geo_routes'),('geo','geo_route_points'),('ui','ui_audit_events')) as scoped(schema_name,table_name) loop
  execute format('alter table %I.%I enable row level security',item.schema_name,item.table_name);
  execute format('drop policy if exists tenant_isolation on %I.%I',item.schema_name,item.table_name);
  execute format('create policy tenant_isolation on %I.%I using (tenant_id=platform.current_tenant_id()) with check (tenant_id=platform.current_tenant_id())',item.schema_name,item.table_name);
 end loop;
end $$;
commit;
