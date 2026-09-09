begin;

alter table agro360.sales_order_items
    add column if not exists price_table_id uuid,
    add column if not exists base_unit_price numeric(18,4),
    add column if not exists pricing_snapshot jsonb not null default '{}';

do $$
begin
    alter table agro360.sales_order_items
        add constraint fk_sales_order_item_price_table
        foreign key (tenant_id,price_table_id) references agro360.sales_price_tables(tenant_id,id);
exception when duplicate_object then null;
end $$;

insert into agro360.platform_schema_versions(version,description,installed_at)
values('6.6.0','Snapshot de politica de preco comercial nos itens do pedido',now())
on conflict(version) do update set description=excluded.description;

commit;
