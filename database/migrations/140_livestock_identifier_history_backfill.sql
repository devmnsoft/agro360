begin;

-- Pecuaria P1 (2026-10-12): historico canonico de identificadores - backfill do estado atual.
-- `livestock_identifier_history` (criada em 068_integrated_livestock_foundation) era schema-only:
-- zero INSERT/UPDATE/SELECT em toda a base (src, seeds, migrations). A auditoria do sprint P1
-- materializa o estado vigente como linha ATIVA por tipo (conforme o CHECK da tabela:
-- INTERNAL/EAR_TAG/RFID), com valid_from = created_at do animal e motivo REGISTRATION:
--   * EAR_TAG  <- animals.tag                 (sempre presente);
--   * RFID     <- animals.rfid                (quando nao nulo/vazio);
--   * INTERNAL <- animals.internal_identifier (quando nao nulo/vazio; pode coincidir com a tag -
--     o unico parcial ux_livestock_identifier_current e por (tenant,tipo,valor), entao nao conflita).
-- Idempotente e seguro a colisao: usa ON CONFLICT contra o unico parcial do proprio ledger
-- ((tenant_id, identifier_type, lower(identifier)) where valid_until is null) -> reexecucao ou
-- valor duplicado entre animais (ex.: rfid reutilizada apos tombamento - o unico parcial da
-- tabela animals nao cobre linhas deleted) simplesmente nao gera segunda linha aberta; a primeira
-- insercao vence. Nao encerra nem altera linhas existentes.
-- SISBOV permanece fora do ledger historico (o CHECK da tabela cobre apenas INTERNAL/EAR_TAG/RFID)
-- e segue coberto pela coluna animals.sisbov + livestock_animal_identifiers ate decisao futura.
-- Animais soft-deletados tambem recebem historico (a identidade vigora ate o tombamento).

do $$
begin
  if not exists (select 1 from information_schema.tables
                 where table_schema = 'agro360' and table_name = 'livestock_identifier_history') then
    raise exception 'livestock_identifier_history ausente; aplique antes a migration 068_integrated_livestock_foundation';
  end if;
end $$;

insert into agro360.livestock_identifier_history (id, tenant_id, animal_id, identifier_type, identifier, valid_from, valid_until, change_reason, created_at, created_by)
select gen_random_uuid(), a.tenant_id, a.id, 'EAR_TAG', a.tag, a.created_at, null, 'REGISTRATION', now(), a.created_by
from agro360.livestock_animals a
on conflict (tenant_id, identifier_type, lower(identifier)) where valid_until is null do nothing;

insert into agro360.livestock_identifier_history (id, tenant_id, animal_id, identifier_type, identifier, valid_from, valid_until, change_reason, created_at, created_by)
select gen_random_uuid(), a.tenant_id, a.id, 'RFID', a.rfid, a.created_at, null, 'REGISTRATION', now(), a.created_by
from agro360.livestock_animals a
where a.rfid is not null and trim(a.rfid) <> ''
on conflict (tenant_id, identifier_type, lower(identifier)) where valid_until is null do nothing;

insert into agro360.livestock_identifier_history (id, tenant_id, animal_id, identifier_type, identifier, valid_from, valid_until, change_reason, created_at, created_by)
select gen_random_uuid(), a.tenant_id, a.id, 'INTERNAL', a.internal_identifier, a.created_at, null, 'REGISTRATION', now(), a.created_by
from agro360.livestock_animals a
where a.internal_identifier is not null and trim(a.internal_identifier) <> ''
on conflict (tenant_id, identifier_type, lower(identifier)) where valid_until is null do nothing;

insert into agro360.platform_schema_versions(version, description, installed_at)
values('140.0.0', 'Pecuaria P1: backfill do historico canonico de identificadores (livestock_identifier_history)', now())
on conflict(version) do nothing;

commit;
