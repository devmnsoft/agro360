begin;

alter table agro360.procurement_receipts
    add column if not exists request_fingerprint varchar(64);

do $$
begin
    alter table agro360.procurement_receipts add constraint ck_procurement_receipt_fingerprint
        check (request_fingerprint is null or request_fingerprint ~ '^[0-9a-f]{64}$');
exception when duplicate_object then null;
end $$;

insert into agro360.identity_permissions(code,module,description)
values ('purchasing.receipts.override-excess','Compras e Suprimentos','Autorizar recebimento acima da quantidade do pedido.')
on conflict(code) do update set module=excluded.module,description=excluded.description;

insert into agro360.identity_role_permissions(tenant_id,role_id,permission_id)
select r.tenant_id,r.id,p.id
from agro360.identity_roles r
cross join agro360.identity_permissions p
where r.code in ('SUPER_ADMIN','TENANT_ADMIN') and p.code='purchasing.receipts.override-excess'
on conflict do nothing;

insert into agro360.platform_schema_versions(version,description,installed_at)
values('6.7.0','Integridade de idempotencia, unidade e autorizacao no recebimento',now())
on conflict(version) do update set description=excluded.description;

commit;
