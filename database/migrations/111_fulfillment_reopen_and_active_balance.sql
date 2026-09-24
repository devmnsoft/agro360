begin;

-- A conferência é uma decisão explícita. Zero pode ser uma quantidade conferida
-- válida e, portanto, não pode continuar sendo usado como marcador de etapa.
alter table agro360.fulfillment_shipment_items
    add column if not exists check_completed boolean not null default false;

-- Preserva a interpretação dos documentos antigos que já haviam avançado.
update agro360.fulfillment_shipment_items i
set check_completed = true
from agro360.fulfillment_shipments s
where s.tenant_id=i.tenant_id and s.id=i.shipment_id
  and s.status not in ('PREPARING','CANCELLED');

create table if not exists agro360.fulfillment_preparation_reopens(
 id uuid primary key, tenant_id uuid not null, shipment_id uuid not null,
 item_id uuid not null, reason text not null, previous_picked numeric(20,6) not null,
 previous_checked numeric(20,6) not null, previous_check_completed boolean not null,
 request_id uuid not null, created_at timestamptz not null default now(), created_by uuid not null,
 unique(tenant_id,id), unique(tenant_id,request_id,item_id),
 foreign key(tenant_id,shipment_id) references agro360.fulfillment_shipments(tenant_id,id),
 foreign key(tenant_id,item_id) references agro360.fulfillment_shipment_items(tenant_id,id),
 foreign key(tenant_id,request_id) references agro360.fulfillment_operation_requests(tenant_id,id),
 check(nullif(trim(reason),'') is not null));

do $$ begin perform agro360.platform_enable_tenant_rls('agro360.fulfillment_preparation_reopens'); end $$;

insert into agro360.platform_schema_versions(version,description,installed_at)
values('11.1.0','Conferência explícita, reabertura auditável e saldo ativo canônico',now())
on conflict(version) do nothing;
commit;

-- Diagnóstico não destrutivo para bases legadas. Execute antes do upgrade e
-- investigue toda linha retornada; nenhuma compensação é aplicada automaticamente.
-- select tenant_id,id,quantity,consumed_quantity,released_quantity,status
-- from agro360.fulfillment_reservations
-- where quantity < 0 or consumed_quantity < 0 or released_quantity < 0
--    or consumed_quantity + released_quantity > quantity
--    or (status='ACTIVE' and quantity-consumed_quantity-released_quantity <= 0);
