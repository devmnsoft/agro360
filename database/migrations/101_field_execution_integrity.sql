begin;

-- Existing rows remain untouched; NOT VALID still protects every new or changed
-- row and allows operators to reconcile legacy readings before validation.
alter table agro360.field_work_logs
    drop constraint if exists ck_field_work_logs_meter_pair;
alter table agro360.field_work_logs
    add constraint ck_field_work_logs_meter_pair
    check ((initial_meter is null) = (final_meter is null)) not valid;

alter table agro360.field_work_logs
    drop constraint if exists ck_field_work_logs_interruption_duration;
alter table agro360.field_work_logs
    add constraint ck_field_work_logs_interruption_duration
    check (
        interruption_minutes <= extract(epoch from (ends_at - starts_at)) / 60
        and (interruption_minutes = 0 or nullif(trim(interruption_reason), '') is not null)
    ) not valid;

insert into agro360.platform_schema_versions(version, description, installed_at)
values ('101.0.0', 'Integridade de horimetro, paradas e idempotencia dos apontamentos de campo', now())
on conflict (version) do nothing;

commit;
