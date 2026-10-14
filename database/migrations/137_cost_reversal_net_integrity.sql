begin;

-- Incremento 2026-10-12 - Bloco A (custo liquido confiavel da safra).
-- Invariante canonica do livro de custos: o estorno (reversed_amount) nunca pode exceder
-- o valor reconhecido (recognized_amount). Assim o custo reconhecido LIQUIDO
-- (recognized_amount - reversed_amount) nunca fica negativo e nenhum valor negativo e
-- fabricado para contornar constraints. Complementation da migration 078, que garantia
-- apenas colunas >= 0 e allocated <= recognized.
do $$
begin
  if not exists(select 1 from pg_constraint where conname = 'cost_management_entries_reversed_le_recognized_check') then
    if exists(select 1 from agro360.cost_management_entries where reversed_amount > recognized_amount) then
      raise exception 'Migration 137: existem lancamentos com reversed_amount > recognized_amount. Corrija os dados antes de aplicar.';
    end if;
    execute 'alter table agro360.cost_management_entries add constraint cost_management_entries_reversed_le_recognized_check check (reversed_amount <= recognized_amount)';
  end if;
end $$;

comment on constraint cost_management_entries_reversed_le_recognized_check on agro360.cost_management_entries is
  'Custo reconhecido liquido = recognized_amount - reversed_amount (>= 0): estorno nao excede o reconhecido.';

insert into agro360.platform_schema_versions(version,description,installed_at)
values('137.0.0','Custo liquido: invariante de estorno no livro canonico de custos',now())
on conflict(version) do nothing;

commit;
