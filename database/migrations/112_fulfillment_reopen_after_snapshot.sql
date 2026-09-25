begin;

-- A reabertura precisa ser autoexplicativa mesmo sem reconstruir o estado atual
-- do item. Os campos anteriores permanecem imutáveis e o resultado da operação
-- fica gravado no mesmo evento auditável.
alter table agro360.fulfillment_preparation_reopens
    add column if not exists new_picked numeric(20,6) not null default 0,
    add column if not exists new_checked numeric(20,6) not null default 0,
    add column if not exists new_check_completed boolean not null default false;

alter table agro360.fulfillment_preparation_reopens
    drop constraint if exists ck_fulfillment_reopen_after_quantities;
alter table agro360.fulfillment_preparation_reopens
    add constraint ck_fulfillment_reopen_after_quantities
    check(new_picked >= 0 and new_checked >= 0 and new_checked <= new_picked);

insert into agro360.platform_schema_versions(version,description,installed_at)
values('11.2.0','Snapshot posterior e protocolo concorrente da reabertura operacional',now())
on conflict(version) do nothing;

commit;
