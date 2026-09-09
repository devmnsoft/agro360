begin;

insert into agro360.identity_permissions(code,module,description) values
 ('purchasing.receive.override-excess','Compras e Suprimentos','Autorizar recebimento acima da quantidade aprovada.'),
 ('purchasing.receipts.inspect','Compras e Suprimentos','Liberar ou reprovar materiais em inspeção.'),
 ('purchasing.receipts.cancel','Compras e Suprimentos','Cancelar recebimento com estorno operacional auditado.')
on conflict(code) do update set module=excluded.module,description=excluded.description;

insert into agro360.identity_role_permissions(tenant_id,role_id,permission_id)
select distinct existing.tenant_id,existing.role_id,added.id
from agro360.identity_role_permissions existing
join agro360.identity_permissions approval on approval.id=existing.permission_id and approval.code='purchasing.approve'
cross join agro360.identity_permissions added
where added.code in('purchasing.receive.override-excess','purchasing.receipts.inspect','purchasing.receipts.cancel')
on conflict do nothing;

alter table agro360.procurement_receipts
    add column if not exists payload_hash char(64),
    add column if not exists cancelled_at timestamptz,
    add column if not exists cancelled_by uuid,
    add column if not exists cancellation_reason varchar(1000);

create table if not exists agro360.procurement_receipt_quarantines(
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    receipt_item_id uuid not null,
    warehouse_id uuid not null,
    product_id uuid not null,
    quantity numeric(20,6) not null check(quantity>0),
    unit_cost numeric(18,4) not null check(unit_cost>=0),
    status varchar(20) not null default 'PENDING' check(status in('PENDING','RELEASED','REJECTED','CANCELLED')),
    reason varchar(1000) not null,
    decided_at timestamptz,
    decided_by uuid,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    primary key(tenant_id,receipt_item_id),
    foreign key(tenant_id,receipt_item_id) references agro360.procurement_receipt_items(tenant_id,id),
    foreign key(tenant_id,warehouse_id) references agro360.inventory_warehouses(tenant_id,id),
    foreign key(tenant_id,product_id) references agro360.inventory_products(tenant_id,id)
);

create table if not exists agro360.procurement_receipt_divergences(
    id uuid primary key default gen_random_uuid(),
    tenant_id uuid not null references agro360.tenancy_tenants(id),
    receipt_id uuid not null,
    receipt_item_id uuid,
    kind varchar(30) not null check(kind in('QUALITY_INSPECTION','EXCESS','DOCUMENT','QUANTITY')),
    status varchar(20) not null default 'OPEN' check(status in('OPEN','RESOLVED','CANCELLED')),
    reason varchar(1000) not null,
    routing varchar(40) not null check(routing in('QUALITY_REVIEW','RELEASED_TO_STOCK','SUPPLIER_RETURN','PROCUREMENT_REVIEW','CANCELLED')),
    responsible_id uuid not null,
    resolved_at timestamptz,
    resolved_by uuid,
    created_at timestamptz not null default now(),
    created_by uuid not null,
    foreign key(tenant_id,receipt_id) references agro360.procurement_receipts(tenant_id,id),
    foreign key(tenant_id,receipt_item_id) references agro360.procurement_receipt_items(tenant_id,id)
);

create index if not exists ix_procurement_receipt_divergences_open
    on agro360.procurement_receipt_divergences(tenant_id,receipt_id,status,created_at desc);

do $$
declare target text;
begin
    foreach target in array array['procurement_receipt_quarantines','procurement_receipt_divergences'] loop
        execute format('alter table agro360.%I enable row level security',target);
        execute format('alter table agro360.%I force row level security',target);
        execute format('drop policy if exists tenant_isolation on agro360.%I',target);
        execute format('create policy tenant_isolation on agro360.%I using (tenant_id=agro360.platform_current_tenant_id()) with check (tenant_id=agro360.platform_current_tenant_id())',target);
    end loop;
end $$;

insert into agro360.platform_schema_versions(version,description,installed_at)
values('6.5.1','Controles de excedente, quarentena, qualidade e cancelamento de recebimentos',now())
on conflict(version) do update set description=excluded.description;

commit;
