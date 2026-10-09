-- Migration 133: devolução ao fornecedor (jornada de suprimentos). O recebimento canônico integra
-- estoque, qualidade e financeiro, mas não havia como devolver mercadoria recebida: o saldo físico
-- ficava errado, o recebido_quantity do pedido continuava inflado e o crédito do fornecedor não era
-- registrado. Esta migration cria o documento de devolução com aprovação segregada (quem registra é
-- distinto de quem aprova), o tipo próprio de saída no ledger (a devolução não é consumo nem
-- ajuste), a nota de crédito do fornecedor em aberto (sem baixa automática de títulos) e as chaves
-- de idempotência no mesmo padrão do recebimento.
begin;

-- 1) Ledger: devolução física ao fornecedor é uma categoria própria de movimento. Reutilizar
-- CONSUMPTION/ADJUSTMENT_OUT misturaria retorno de compra com consumo operacional e impediria a
-- conciliação entre compras e estoque. A ampliação do CHECK é aditiva; linhas históricas não mudam.
alter table agro360.inventory_stock_movements drop constraint if exists ck_stock_movements_type;
alter table agro360.inventory_stock_movements add constraint ck_stock_movements_type check (movement_type in('RECEIPT','CONSUMPTION','TRANSFER_IN','TRANSFER_OUT','ADJUSTMENT_IN','ADJUSTMENT_OUT','PRODUCTION','SALE','RETURN_TO_SUPPLIER'));

-- 2) Unicidade composta para FKs nominais de itens de recebimento (mesmo padrão dos itens de pedido).
alter table agro360.procurement_receipt_items drop constraint if exists procurement_receipt_items_tenant_id_id_key;
alter table agro360.procurement_receipt_items add constraint procurement_receipt_items_tenant_id_id_key unique(tenant_id,id);

create table if not exists agro360.procurement_supplier_returns(
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    number varchar(30) not null,
    purchase_order_id uuid not null,
    receipt_id uuid not null,
    warehouse_id uuid,
    status varchar(24) not null default 'PENDING_APPROVAL' check(status in('PENDING_APPROVAL','APPROVED','REJECTED','CANCELLED')),
    reason text not null check(length(trim(reason))>=5),
    idempotency_key varchar(100),
    request_fingerprint varchar(64),
    decided_at timestamptz,
    decided_by uuid,
    decision_reason text,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid not null,
    updated_by uuid not null,
    deleted_at timestamptz,
    unique(tenant_id,id),
    unique(tenant_id,number),
    foreign key(tenant_id,purchase_order_id) references agro360.procurement_purchase_orders(tenant_id,id),
    foreign key(tenant_id,receipt_id) references agro360.procurement_receipts(tenant_id,id)
);
create unique index if not exists uq_procurement_supplier_return_idempotency on agro360.procurement_supplier_returns(tenant_id,idempotency_key) where idempotency_key is not null;
create index if not exists ix_proc_supplier_returns_queue on agro360.procurement_supplier_returns(tenant_id,status,created_at);

-- Itens da devolução sempre apontam para a linha do recebimento (origem física do lote) e para a
-- linha do pedido (destino da reversão de quantidade). Custo unitário é snapshot no momento do
-- registro: alterações posteriores de preço não reescrevem a devolução.
create table if not exists agro360.procurement_supplier_return_items(
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    supplier_return_id uuid not null,
    receipt_item_id uuid not null,
    purchase_order_item_id uuid not null,
    product_id uuid,
    quantity numeric(18,4) not null check(quantity>0),
    unit varchar(20) not null,
    unit_cost numeric(18,4) not null check(unit_cost>=0),
    lot_number varchar(120),
    expires_on date,
    stock_movement_id uuid,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid not null,
    updated_by uuid not null,
    deleted_at timestamptz,
    unique(tenant_id,id),
    unique(tenant_id,supplier_return_id,receipt_item_id),
    foreign key(tenant_id,supplier_return_id) references agro360.procurement_supplier_returns(tenant_id,id),
    foreign key(tenant_id,receipt_item_id) references agro360.procurement_receipt_items(tenant_id,id),
    foreign key(tenant_id,purchase_order_item_id) references agro360.procurement_purchase_order_items(tenant_id,id)
);
create index if not exists ix_proc_supplier_return_items_return on agro360.procurement_supplier_return_items(tenant_id,supplier_return_id);
create index if not exists ix_proc_supplier_return_items_receipt_item on agro360.procurement_supplier_return_items(tenant_id,receipt_item_id);

-- Crédito do fornecedor criado na aprovação da devolução. Fica OPEN até decisão manual do financeiro
-- (aproveitamento em título futuro ou reembolso); nenhuma compensação automática é feita aqui.
create table if not exists agro360.procurement_supplier_credits(
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    number varchar(30) not null,
    supplier_return_id uuid not null,
    supplier_id uuid not null,
    purchase_order_id uuid not null,
    amount numeric(18,2) not null check(amount>0),
    status varchar(16) not null default 'OPEN' check(status in('OPEN','APPLIED','CANCELLED')),
    application_reference text,
    applied_at timestamptz,
    applied_by uuid,
    created_at timestamptz not null default now(),
    updated_at timestamptz not null default now(),
    created_by uuid not null,
    updated_by uuid not null,
    deleted_at timestamptz,
    unique(tenant_id,id),
    unique(tenant_id,number),
    unique(tenant_id,supplier_return_id),
    foreign key(tenant_id,supplier_return_id) references agro360.procurement_supplier_returns(tenant_id,id),
    foreign key(tenant_id,supplier_id) references agro360.procurement_suppliers(tenant_id,id),
    foreign key(tenant_id,purchase_order_id) references agro360.procurement_purchase_orders(tenant_id,id)
);

select agro360.platform_enable_tenant_rls('agro360.procurement_supplier_returns');
select agro360.platform_enable_tenant_rls('agro360.procurement_supplier_return_items');
select agro360.platform_enable_tenant_rls('agro360.procurement_supplier_credits');

insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.23.0', 'Devolução ao fornecedor: documento com aprovação segregada, tipo RETURN_TO_SUPPLIER no ledger, reversão de received_quantity, nota de crédito aberta do fornecedor e idempotência no padrão do recebimento', now())
on conflict (version) do update set description = excluded.description;
commit;
