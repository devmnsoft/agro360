begin;

-- Pecuaria P3 (2026-10-13): evidencia de precisao da mediacao na pesagem.
-- Auditoria do sprint: o DDL e o DTO de pesagens ja expoem equipamento e responsavel
-- (conferidos), mas precisao nao existe em lugar nenhum do schema (scan exaustivo).
-- Campo dedicado para que o registro leve "qual balaca leu, com qual precisao" junto da
-- trilha de evidencias (equipamento/metodo/responsavel/correcao). Aditiva e reexecutavel
-- (if not exists); zero DDL destrutivo; linhas antigas permanecem null; nao altera a
-- semantica de weight_kg numeric(12,3), que segue canonica em kg.

do $$
begin
  if not exists (select 1 from information_schema.tables
                 where table_schema = 'agro360' and table_name = 'livestock_weighings') then
    raise exception 'livestock_weighings ausente; aplique antes a migration 068_livestock_herd_operations';
  end if;
end $$;

alter table agro360.livestock_weighings
    add column if not exists measurement_precision varchar(20);

insert into agro360.platform_schema_versions(version, description, installed_at)
values('142.0.0', 'Pecuaria P3: evidencia de precisao da mediacao em pesagens (livestock_weighings.measurement_precision)', now())
on conflict(version) do nothing;

commit;
