-- Entrega E2E de Fazendas e Talhões. Idempotente e compatível com dados legados.
begin;

do $$
begin
    if not exists (select 1 from pg_constraint where conname = 'ck_geo_farms_state_format') then
        alter table agro360.geo_farms
            add constraint ck_geo_farms_state_format
            check (state ~ '^[A-Z]{2}$') not valid;
    end if;
    if not exists (select 1 from pg_constraint where conname = 'ck_geo_farms_registration_not_blank') then
        alter table agro360.geo_farms
            add constraint ck_geo_farms_registration_not_blank
            check (registration_number is null or length(trim(registration_number)) > 0) not valid;
    end if;
    if not exists (select 1 from pg_constraint where conname = 'ck_geo_farms_car_not_blank') then
        alter table agro360.geo_farms
            add constraint ck_geo_farms_car_not_blank
            check (car_number is null or length(trim(car_number)) > 0) not valid;
    end if;
    if not exists (select 1 from pg_constraint where conname = 'ck_geo_fields_boundary_type') then
        alter table agro360.geo_fields
            add constraint ck_geo_fields_boundary_type
            check (boundary is null or boundary->>'type' in ('Polygon', 'MultiPolygon')) not valid;
    end if;
end $$;

create index if not exists ix_geo_farms_tenant_active_name
    on agro360.geo_farms (tenant_id, lower(name))
    where deleted_at is null;
create index if not exists ix_geo_fields_tenant_farm_active_name
    on agro360.geo_fields (tenant_id, farm_id, lower(name))
    where deleted_at is null;

insert into agro360.platform_schema_versions(version,description,installed_at)
values('5.1.1','Fluxo E2E de fazendas e talhões com validações cadastrais',now())
on conflict(version) do update set description=excluded.description;

commit;
