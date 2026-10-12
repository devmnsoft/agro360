begin;

-- Quality Bridge: permite ao controle de consumo em campo bloquear materiais cujos
-- itens de recebimento de compras estejam em quarentena/rejeicao. A coluna e nula por
-- padrao (nenhum historico e alterado); o vinculo e gravado quando um item de
-- recebimento e entregue/atribuido a um material de ordem de campo.
alter table agro360.procurement_receipt_items
    add column if not exists work_order_material_id uuid null;

comment on column agro360.procurement_receipt_items.work_order_material_id
    is 'Vinculo opcional ao material da ordem de campo (agro360.field_work_order_materials) para rastreio de qualidade no ponto de uso.';

insert into agro360.platform_schema_versions(version,description,installed_at)
values('136.0.0','Quality Bridge: vinculo item de recebimento -> material da ordem de campo',now())
on conflict(version) do nothing;

commit;
