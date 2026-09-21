begin;

-- 9.4.0: Destinação de devoluções físicas com suporte a unidade e custo de perda (AG-E8-RET-001).
-- Permite registrar unidade do item e custo contábil/financeiro opcional nas destinações de perda (DISPOSE),
-- sem inferir ou gerar duplicidades fiscais/títulos a pagar.

alter table agro360.fulfillment_return_decisions
  add column if not exists unit varchar(20);

alter table agro360.fulfillment_return_decisions
  add column if not exists cost numeric(18,4);

insert into agro360.platform_schema_versions(version, description, installed_at)
values('9.4.0', 'Destinação de devoluções com unidade e custo de perda (AG-E8-RET-001)', now())
on conflict(version) do nothing;

commit;
