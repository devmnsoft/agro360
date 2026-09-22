begin;

-- Normaliza a modalidade coletiva legada e torna a individualização parcial,
-- concorrente e repetível sem dupla contagem.
alter table agro360.livestock_herds
    alter column control_mode set default 'INDIVIDUAL';

update agro360.livestock_herds
set control_mode = 'QUANTITY'
where control_mode = 'COLLECTIVE';

alter table agro360.livestock_herds
    drop constraint if exists ck_livestock_herds_control_mode,
    drop constraint if exists ck_herds_control_mode;
alter table agro360.livestock_herds
    add constraint ck_livestock_herds_control_mode
    check (control_mode in ('INDIVIDUAL', 'QUANTITY'));

alter table agro360.livestock_individualization_reconciliations
    add column if not exists idempotency_key varchar(120);

do $$ declare v_constraint record; begin
    for v_constraint in
        select c.conname
        from pg_constraint c
        where c.conrelid = 'agro360.livestock_individualization_reconciliations'::regclass
          and c.contype = 'c'
          and pg_get_constraintdef(c.oid) ilike '%collective_quantity = identified_quantity%'
    loop
        execute format(
            'alter table agro360.livestock_individualization_reconciliations drop constraint %I',
            v_constraint.conname);
    end loop;
end $$;

alter table agro360.livestock_individualization_reconciliations
    drop constraint if exists ck_livestock_individualization_quantity;
alter table agro360.livestock_individualization_reconciliations
    add constraint ck_livestock_individualization_quantity
    check (identified_quantity <= collective_quantity);

create unique index if not exists ux_livestock_individualization_idempotency
    on agro360.livestock_individualization_reconciliations(tenant_id, idempotency_key)
    where idempotency_key is not null;

insert into agro360.platform_schema_versions(version, description, installed_at)
values ('10.5.0', 'Integridade da individualização parcial do rebanho', now())
on conflict(version) do nothing;

commit;
