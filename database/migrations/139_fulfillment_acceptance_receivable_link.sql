begin;

-- Incremento 2026-10-12 - Bloco B (comercializacao do lote disponivel -> venda + resultado comercial).
-- O caminho canonico de venda (sales_orders -> fulfillment -> dispatch -> aceite formal) nao emitia
-- recebivel financeiro: o ciclo financeiro da venda ficava aberto (a migration 126 fixou explicitamente
-- que o SETTLE de programacao de entrega e administrativo, sem escritura financeira/fiscal). Este
-- incremento cria o elo de integridade para emitir UM recebivel POR ACEITE formal (tentativa de entrega
-- com quantidade aceita > 0), fechando o loop fisico -> financeiro na simetria de Compras:
--   * coluna fulfillment_attempt_id nula por padrao (historico dos outros caminhos inalterado);
--   * FK COMPOSTA (tenant_id, fulfillment_attempt_id) -> fulfillment_delivery_attempts(tenant_id, id),
--     usando o unique(tenant_id,id) ja existente, garantindo o elo dentro do MESMO TENANT;
--   * unique parcial (tenant_id, fulfillment_attempt_id) onde nao nulo: no maximo UM recebivel por
--     tentativa — a emissao fica idempotente mesmo fora do replay por chave+hash da tentativa.
-- Base de preco da emissao = quantidade aceita x unit_price registrado no item comercial, a MESMA base
-- da receita reconhecida na safra (HarvestService): faturamento e reconhecimento nao divergem.

do $$
begin
  if not exists (select 1 from information_schema.columns
                 where table_schema = 'agro360' and table_name = 'finance_receivables' and column_name = 'fulfillment_attempt_id') then
    alter table agro360.finance_receivables add column fulfillment_attempt_id uuid;
  end if;
end $$;

do $$
begin
  if not exists(select 1 from pg_constraint where conname = 'finance_receivables_fulfillment_attempt_fk') then
    if exists(
      select 1 from agro360.finance_receivables fr
      where fr.fulfillment_attempt_id is not null
        and not exists(
          select 1 from agro360.fulfillment_delivery_attempts a
          where a.tenant_id = fr.tenant_id and a.id = fr.fulfillment_attempt_id
        )
    ) then
      raise exception 'ha recebiveis com fulfillment_attempt_id apontando para tentativa inexistente no mesmo tenant';
    end if;
    alter table agro360.finance_receivables
      add constraint finance_receivables_fulfillment_attempt_fk
      foreign key (tenant_id, fulfillment_attempt_id)
      references agro360.fulfillment_delivery_attempts (tenant_id, id);
  end if;
end $$;

create unique index if not exists ux_finance_receivables_fulfillment_attempt
  on agro360.finance_receivables (tenant_id, fulfillment_attempt_id)
  where fulfillment_attempt_id is not null;

insert into agro360.platform_schema_versions(version, description, installed_at)
values('139.0.0', 'Bloco B: recebivel financeiro idempotente por aceite formal (finance_receivables.fulfillment_attempt_id)', now())
on conflict(version) do nothing;

commit;
