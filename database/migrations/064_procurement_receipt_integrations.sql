begin;

alter table agro360.procurement_receipts
    add column if not exists finance_integration_status varchar(20) not null default 'PENDING',
    add column if not exists idempotency_key varchar(100),
    add column if not exists warehouse_id uuid;

do $$
begin
    alter table agro360.procurement_receipts
        add constraint ck_procurement_receipt_finance_status
        check (finance_integration_status in ('PENDING','COMPLETED','FAILED','NOT_APPLICABLE'));
exception when duplicate_object then null;
end $$;

do $$
begin
    alter table agro360.procurement_receipts
        add constraint fk_procurement_receipt_warehouse
        foreign key (tenant_id,warehouse_id) references agro360.inventory_warehouses(tenant_id,id);
exception when duplicate_object then null;
end $$;

create unique index if not exists uq_procurement_receipt_idempotency
    on agro360.procurement_receipts(tenant_id,idempotency_key)
    where idempotency_key is not null;

create unique index if not exists uq_procurement_receipt_items_tenant_id
    on agro360.procurement_receipt_items(tenant_id,id);

create unique index if not exists uq_finance_payables_tenant_id
    on agro360.finance_payables(tenant_id,id);

create table if not exists agro360.procurement_receipt_stock_links(
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    receipt_item_id uuid not null,
    stock_movement_id uuid not null,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    primary key(tenant_id,receipt_item_id),
    unique(tenant_id,stock_movement_id),
    foreign key(tenant_id,receipt_item_id) references agro360.procurement_receipt_items(tenant_id,id),
    foreign key(tenant_id,stock_movement_id) references agro360.inventory_stock_movements(tenant_id,id)
);

create table if not exists agro360.procurement_order_financial_links(
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    purchase_order_id uuid not null,
    installment integer not null check(installment>0),
    payable_id uuid not null,
    amount numeric(18,2) not null check(amount>0),
    created_at timestamptz not null default now(),
    created_by uuid not null,
    primary key(tenant_id,purchase_order_id,installment),
    unique(tenant_id,payable_id),
    foreign key(tenant_id,purchase_order_id) references agro360.procurement_purchase_orders(tenant_id,id),
    foreign key(tenant_id,payable_id) references agro360.finance_payables(tenant_id,id)
);

select agro360.platform_enable_tenant_rls('agro360.procurement_receipt_stock_links');
select agro360.platform_enable_tenant_rls('agro360.procurement_order_financial_links');

insert into agro360.platform_schema_versions(version,description,installed_at)
values('6.4.1','Recebimento de compras integrado a estoque e previsão financeira idempotente',now())
on conflict(version) do update set description=excluded.description;

commit;
