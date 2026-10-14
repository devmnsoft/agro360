begin;

-- Pecuaria P2 (2026-10-13): idempotencia formal da movimentacao individual de animal.
-- Auditoria do sprint: o endpoint POST /api/livestock/movements validava replay cruzando a
-- coluna LIVRE `notes` (select id ... where notes=@Key), mas o registro nunca persistia a
-- chave la -> replay com IdempotencyKey nunca deduplicava (novo movimento + novo evento).
-- O grupo por quantidade (livestock_herd_movements) ja tem a coluna + unique parcial; esta
-- migration alinha o individuo ao mesmo padrao adotado em Compras/Inspecao/Jornada J:
--   * coluna dedicada idempotency_key varchar(160) (null quando ausente);
--   * unique parcial (tenant_id, idempotency_key) where idempotency_key is not null
--     -> garante "um efeito" mesmo em concorrencia (segundo inseridor recebe conflito);
--   * sem backfill: `notes` sempre foi texto livre (nenhum fluxo gravava a chave nela) e
--     nao e possivel distinguir chave de observacao em dados existentes. Limites documentados.
-- Aditiva e reexecutavel (if not exists); zero DDL destrutivo; nao altera comportamento de
-- linhas antigas (idempotency_key permanece null nelas).

do $$
begin
  if not exists (select 1 from information_schema.tables
                 where table_schema = 'agro360' and table_name = 'livestock_animal_movements') then
    raise exception 'livestock_animal_movements ausente; aplique antes a migration 068_livestock_herd_operations';
  end if;
end $$;

alter table agro360.livestock_animal_movements
    add column if not exists idempotency_key varchar(160);

create unique index if not exists ux_livestock_animal_movements_idempotency
    on agro360.livestock_animal_movements (tenant_id, idempotency_key)
    where idempotency_key is not null;

insert into agro360.platform_schema_versions(version, description, installed_at)
values('141.0.0', 'Pecuaria P2: idempotencia formal da movimentacao individual de animal (coluna + unique parcial)', now())
on conflict(version) do nothing;

commit;
