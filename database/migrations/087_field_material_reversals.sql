begin;

alter table agro360.field_material_events
    drop constraint if exists field_material_events_event_type_check;

alter table agro360.field_material_events
    add constraint field_material_events_event_type_check
    check(event_type in('RESERVE','DELIVER','CONSUME','RETURN','LOSS','RELEASE','REVERSAL'));

alter table agro360.field_material_events
    add constraint field_material_events_reversal_source_check
    check(event_type<>'REVERSAL' or source_event_id is not null);

create unique index if not exists ux_field_material_single_reversal
    on agro360.field_material_events(tenant_id,source_event_id)
    where event_type='REVERSAL';

insert into agro360.platform_schema_versions(version,description,installed_at)
values('87.0.0','Estorno rastreável de consumo em ordens de campo',now())
on conflict(version) do nothing;

commit;
