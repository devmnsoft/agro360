begin;

-- Incremento 2026-10-12 - item 11 (integridade do vinculo de qualidade no ponto de uso).
-- A migration 136 adicionou procurement_receipt_items.work_order_material_id SEM constraint
-- para minimizar o impacto sobre Compras. Este incremento fecha a integridade: FK COMPOSTA
-- (tenant_id, work_order_material_id) -> field_work_order_materials(tenant_id, id), que usa o
-- unique(tenant_id,id) ja existente e garante a existencia do material DENTRO DO MESMO TENANT.
-- A coluna permanece nula por padrao (historico inalterado); o vinculo e gravado somente pela
-- aplicacao no mesmo tenant/propriedade, entao o registro continua opcional.
do $$
begin
  if not exists(select 1 from pg_constraint where conname = 'procurement_receipt_items_work_order_material_fk') then
    if exists(
      select 1 from agro360.procurement_receipt_items ri
      where ri.work_order_material_id is not null
        and not exists(
          select 1 from agro360.field_work_order_materials m
          where m.tenant_id = ri.tenant_id and m.id = ri.work_order_material_id
        )
    ) then
      raise exception 'Migration 138: existem itens com work_order_material_id sem material correspondente no mesmo tenant. Corrija os dados antes de aplicar.';
    end if;
    execute 'alter table agro360.procurement_receipt_items add constraint procurement_receipt_items_work_order_material_fk foreign key (tenant_id, work_order_material_id) references agro360.field_work_order_materials(tenant_id, id)';
  end if;
end $$;

insert into agro360.platform_schema_versions(version,description,installed_at)
values('138.0.0','Integridade: FK composta item de recebimento -> material da ordem (mesmo tenant)',now())
on conflict(version) do nothing;

commit;
