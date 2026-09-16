begin;

-- Distingue a parcela expedida da parcela liberada de uma reserva parcialmente conferida.
alter table agro360.fulfillment_reservations
    add column if not exists consumed_quantity numeric(20,6) not null default 0,
    add column if not exists released_quantity numeric(20,6) not null default 0;

update agro360.fulfillment_reservations
set consumed_quantity = quantity
where status = 'CONSUMED' and consumed_quantity = 0 and released_quantity = 0;

alter table agro360.fulfillment_reservations
    drop constraint if exists ck_fulfillment_reservation_quantities;
alter table agro360.fulfillment_reservations
    add constraint ck_fulfillment_reservation_quantities check (
        consumed_quantity >= 0 and released_quantity >= 0
        and consumed_quantity + released_quantity <= quantity
    );

create index if not exists ix_fulfillment_reservation_order_item
    on agro360.fulfillment_reservations(tenant_id, order_item_id, status);
create index if not exists ix_fulfillment_reservation_lot
    on agro360.fulfillment_reservations(tenant_id, stock_lot_id, status);

insert into agro360.platform_schema_versions(version,description,installed_at)
values('8.8.0','Integridade de reservas e expedições parciais',now()) on conflict(version) do nothing;

commit;
