begin;

-- A confirmação de saída possui identidade própria. A chave é tenant-scoped e o
-- hash permite distinguir retry legítimo de reutilização com conteúdo diferente.
alter table agro360.fulfillment_shipments
    add column if not exists dispatch_idempotency_key varchar(120),
    add column if not exists dispatch_request_hash char(64);

alter table agro360.fulfillment_shipments
    drop constraint if exists ck_fulfillment_dispatch_identity;
alter table agro360.fulfillment_shipments
    add constraint ck_fulfillment_dispatch_identity check (
        (dispatch_idempotency_key is null and dispatch_request_hash is null)
        or (nullif(trim(dispatch_idempotency_key),'') is not null and dispatch_request_hash ~ '^[0-9a-f]{64}$')
    );

create unique index if not exists uq_fulfillment_dispatch_idempotency
    on agro360.fulfillment_shipments(tenant_id, dispatch_idempotency_key)
    where dispatch_idempotency_key is not null;

insert into agro360.platform_schema_versions(version,description,installed_at)
values('10.9.0','Visão operacional e idempotência da confirmação de expedição',now())
on conflict(version) do nothing;

commit;
