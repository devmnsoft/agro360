set search_path to agro360, public;

alter table agro360.platform_marketplace_requests
    add column if not exists offer_snapshot jsonb not null default '{"version":1,"pricePublished":false}'::jsonb;

create unique index if not exists ux_platform_marketplace_request_pending
    on agro360.platform_marketplace_requests(tenant_id, module_id)
    where status = 'PENDING' and deleted_at is null;

comment on column agro360.platform_marketplace_requests.offer_snapshot is
    'Immutable conditions shown to the requester. Catalog changes do not rewrite this snapshot.';
