-- Migration 128: associa cada JWT de acesso à sua sessão renovável para revogação imediata.
set local search_path to agro360, public;

alter table agro360.identity_refresh_tokens
    add column if not exists access_token_jti uuid;

create unique index if not exists uq_identity_refresh_tokens_access_jti
    on agro360.identity_refresh_tokens(tenant_id, access_token_jti)
    where access_token_jti is not null;

insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.18.0', 'Vínculo entre JWT de acesso e sessão renovável para validação e revogação imediata', now())
on conflict (version) do update set description = excluded.description;
