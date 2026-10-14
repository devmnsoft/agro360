-- Migration 143: Pecuaria P4 - protocolo sanidade ligado a execucao + idempotencia de aplicacao.
-- Puramente aditiva em agro360.livestock_health_events:
--   * protocol_id / protocol_version_label: referencia solta ao catalogo versionado
--     (mesmo criterio das colunas protocol_id/source_event_id ja existentes em
--      livestock_restrictions — validacao no servico, sem FK para nao acoplar o ledger);
--   * idempotency_key + unique parcial: mesmo contrato formal adotado nas
--     movimentacoes (141) e pesagens (via 139/140-era) — replay por chave, sem backfill.
begin;

do $$
begin
  if not exists (select 1 from information_schema.tables
                 where table_schema = 'agro360' and table_name = 'livestock_health_events') then
    raise exception 'livestock_health_events ausente; aplique antes a migration 068_livestock_herd_operations';
  end if;
end $$;

alter table agro360.livestock_health_events
    add column if not exists protocol_id uuid,
    add column if not exists protocol_version_label varchar(40),
    add column if not exists idempotency_key varchar(160);

create unique index if not exists ux_livestock_health_events_idempotency
    on agro360.livestock_health_events (tenant_id, idempotency_key)
    where idempotency_key is not null;

insert into agro360.platform_schema_versions(version, description, installed_at)
values('143.0.0', 'Pecuaria P4: aplicacao de sanidade ligada a protocolo versionado + idempotencia formal (livestock_health_events)', now())
on conflict(version) do nothing;

commit;
