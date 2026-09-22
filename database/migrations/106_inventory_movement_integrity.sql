begin;

-- A função é a única porta de escrita usada pelos fluxos legados de
-- recebimento, consumo, manutenção e abastecimento. O bloqueio precisa viver
-- aqui (e não apenas na tela de inventário) para também cobrir jobs e integrações.
create or replace function agro360.inventory_apply_stock_movement(
    p_tenant uuid, p_warehouse uuid, p_product uuid, p_quantity numeric,
    p_cost numeric, p_type varchar, p_reference uuid, p_lot varchar,
    p_expires date, p_user uuid, p_reason text default null
) returns uuid language plpgsql as $$
declare
    b agro360.inventory_stock_balances%rowtype;
    movement uuid := gen_random_uuid();
    baseunit varchar(16);
    requireslot boolean;
    newbalance numeric;
    newcost numeric;
    lotbalance numeric;
begin
    if p_quantity = 0 or p_cost < 0 then
        raise exception using message = 'invalid stock movement', errcode = '22023';
    end if;

    select base_unit, requires_lot into baseunit, requireslot
      from agro360.inventory_products
     where tenant_id = p_tenant and id = p_product and deleted_at is null;
    if baseunit is null then
        raise exception using message = 'product not found', errcode = 'P0002';
    end if;
    if requireslot and nullif(trim(p_lot), '') is null then
        raise exception using message = 'lot is required for this product', errcode = '22023';
    end if;

    if exists (
        select 1 from agro360.inventory_counts
         where tenant_id = p_tenant
           and warehouse_id = p_warehouse
           and status in ('COUNTING', 'RECONCILING', 'AWAITING_APPROVAL')
           and (product_id is null or product_id = p_product)
           and (lot_number is null or lot_number = p_lot)
    ) then
        raise exception using
            message = 'physical count blocks movements in this scope',
            errcode = 'P0001', hint = 'count.movement_blocked';
    end if;

    -- Serializa criação/consumo do mesmo saldo, inclusive quando a linha ainda
    -- não existe. Evita duas entradas concorrentes criarem projeções paralelas.
    perform pg_advisory_xact_lock(hashtextextended(
        p_tenant::text || ':' || p_warehouse::text || ':' || p_product::text, 0));
    select * into b from agro360.inventory_stock_balances
     where tenant_id = p_tenant and warehouse_id = p_warehouse and product_id = p_product
     for update;

    if not found then
        if p_quantity < 0 then
            raise exception using message = 'insufficient stock', errcode = 'P0001';
        end if;
        insert into agro360.inventory_stock_balances
            (id, tenant_id, warehouse_id, product_id, unit, available, minimum, average_cost, version)
        select gen_random_uuid(), p_tenant, p_warehouse, p_product, baseunit,
               p_quantity, minimum_stock, p_cost, 1
          from agro360.inventory_products where tenant_id = p_tenant and id = p_product
        returning * into b;
        newbalance := p_quantity;
        newcost := p_cost;
    else
        newbalance := b.available + p_quantity;
        if newbalance < b.reserved then
            raise exception using
                message = 'movement would consume reserved stock',
                errcode = 'P0001', hint = 'inventory.reservation_shortage';
        end if;
        newcost := case when p_quantity > 0
            then ((b.available * b.average_cost) + (p_quantity * p_cost)) / nullif(newbalance, 0)
            else b.average_cost end;
        update agro360.inventory_stock_balances
           set available = newbalance, average_cost = newcost,
               updated_at = now(), version = version + 1
         where tenant_id = p_tenant and id = b.id;
    end if;

    if p_lot is not null and p_quantity < 0 then
        select quantity into lotbalance from agro360.inventory_stock_lots
         where tenant_id = p_tenant and warehouse_id = p_warehouse
           and product_id = p_product and lot_number = p_lot for update;
        if lotbalance is null or lotbalance + p_quantity < 0 then
            raise exception using message = 'insufficient stock in lot', errcode = 'P0001';
        end if;
    end if;

    insert into agro360.inventory_stock_movements
        (id, tenant_id, warehouse_id, product_id, movement_type, quantity, unit,
         unit_cost, total_cost, lot_number, expires_on, reference_type, reference_id,
         notes, balance_after, average_cost_after, balance_version, occurred_at, created_by)
    values
        (movement, p_tenant, p_warehouse, p_product,
         case when p_type in ('ENTRY','PURCHASE_RECEIPT') then 'RECEIPT'
              when p_type in ('EXIT','MAINTENANCE','FUEL') then 'CONSUMPTION'
              when p_type = 'ADJUST' and p_quantity > 0 then 'ADJUSTMENT_IN'
              when p_type = 'ADJUST' then 'ADJUSTMENT_OUT' else p_type end,
         abs(p_quantity), baseunit, p_cost, abs(p_quantity) * p_cost, p_lot, p_expires,
         p_type, p_reference, p_reason, newbalance, newcost, coalesce(b.version, 0) + 1,
         now(), p_user);

    if p_lot is not null then
        insert into agro360.inventory_stock_lots
            (id, tenant_id, warehouse_id, product_id, lot_number, expires_on, quantity)
        values (gen_random_uuid(), p_tenant, p_warehouse, p_product, p_lot, p_expires, p_quantity)
        on conflict (tenant_id, warehouse_id, product_id, lot_number) do update
          set quantity = agro360.inventory_stock_lots.quantity + excluded.quantity,
              expires_on = coalesce(excluded.expires_on, agro360.inventory_stock_lots.expires_on);
    end if;
    return movement;
end $$;

-- Defesa em profundidade para serviços antigos que ainda materializam o ledger
-- diretamente. Como o movimento e a projeção são gravados na mesma transação,
-- a rejeição do ledger também desfaz qualquer alteração anterior do saldo.
create or replace function agro360.inventory_reject_movement_during_count()
returns trigger language plpgsql as $$
begin
    if new.reference_type is distinct from 'PHYSICAL_COUNT_ADJUSTMENT' and exists (
        select 1 from agro360.inventory_counts
         where tenant_id = new.tenant_id
           and warehouse_id = new.warehouse_id
           and status in ('COUNTING', 'RECONCILING', 'AWAITING_APPROVAL')
           and (product_id is null or product_id = new.product_id)
           and (lot_number is null or lot_number = new.lot_number)
    ) then
        raise exception using
            message = 'physical count blocks movements in this scope',
            errcode = 'P0001', hint = 'count.movement_blocked';
    end if;
    return new;
end $$;

drop trigger if exists inventory_stock_movement_count_guard
    on agro360.inventory_stock_movements;
create trigger inventory_stock_movement_count_guard
before insert on agro360.inventory_stock_movements
for each row execute function agro360.inventory_reject_movement_during_count();

insert into agro360.platform_schema_versions(version, description, installed_at)
values ('10.6.0', 'Integridade canônica de movimentos durante inventário', now())
on conflict(version) do nothing;

commit;
