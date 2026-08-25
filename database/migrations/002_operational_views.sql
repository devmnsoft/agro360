create or replace view analytics.season_profitability
with (security_invoker = true)
as
select
    s.tenant_id,
    s.id as season_id,
    s.farm_id,
    s.name as season_name,
    s.crop,
    s.planned_area_ha,
    coalesce(c.total_cost, 0) as total_cost,
    coalesce(v.total_revenue, 0) as total_revenue,
    coalesce(v.total_revenue, 0) - coalesce(c.total_cost, 0) as margin,
    case when s.planned_area_ha > 0 then coalesce(c.total_cost, 0) / s.planned_area_ha else 0 end as cost_per_ha
from agriculture.seasons s
left join lateral (
    select sum(e.amount) as total_cost
    from cost.entries e
    where e.tenant_id = s.tenant_id and e.season_id = s.id
) c on true
left join lateral (
    select sum(sa.total_amount) as total_revenue
    from commercial.sales sa
    where sa.tenant_id = s.tenant_id
      and sa.origin_id = s.id
      and sa.status in ('CONFIRMED', 'FULFILLED')
) v on true
where s.deleted_at is null;

create or replace view analytics.inventory_position
with (security_invoker = true)
as
select
    b.tenant_id,
    w.farm_id,
    b.warehouse_id,
    b.product_id,
    p.sku,
    p.name as product_name,
    p.category,
    b.unit,
    b.available,
    b.reserved,
    b.available - b.reserved as free_quantity,
    b.average_cost,
    b.available * b.average_cost as inventory_value,
    b.available < b.minimum as below_minimum,
    b.version
from inventory.stock_balances b
join inventory.products p on p.id = b.product_id and p.tenant_id = b.tenant_id
join inventory.warehouses w on w.id = b.warehouse_id and w.tenant_id = b.tenant_id
where p.deleted_at is null and w.deleted_at is null;

create or replace view analytics.animal_performance
with (security_invoker = true)
as
select
    a.tenant_id,
    a.farm_id,
    a.id as animal_id,
    a.tag,
    a.species,
    a.breed,
    a.current_weight_kg,
    a.last_weight_date,
    previous.previous_weight,
    previous.previous_date,
    case
        when previous.previous_date is not null and a.last_weight_date > previous.previous_date
        then round((a.current_weight_kg - previous.previous_weight) / (a.last_weight_date - previous.previous_date), 4)
        else null
    end as daily_gain_kg,
    coalesce(costs.total_cost, 0) as accumulated_cost
from livestock.animals a
left join lateral (
    select
        cast(e.data->>'weightKg' as numeric) as previous_weight,
        e.occurred_on as previous_date
    from livestock.animal_events e
    where e.tenant_id = a.tenant_id
      and e.animal_id = a.id
      and e.event_type = 'WEIGHING'
      and e.occurred_on < a.last_weight_date
    order by e.occurred_on desc, e.created_at desc
    limit 1
) previous on true
left join lateral (
    select sum(c.amount) as total_cost
    from cost.entries c
    where c.tenant_id = a.tenant_id and c.animal_id = a.id
) costs on true
where a.deleted_at is null;

comment on view analytics.season_profitability is 'Margem de safra derivada de custos e vendas autorizados pelo RLS.';
comment on view analytics.inventory_position is 'Posição de estoque com disponibilidade, custo médio e alerta mínimo.';
comment on view analytics.animal_performance is 'Peso, GMD e custo acumulado por animal.';
