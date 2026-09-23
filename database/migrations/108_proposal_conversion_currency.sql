begin;

-- Historical orders have no authoritative currency source. Keep them null rather than
-- silently labelling them BRL; all proposal conversions populate the accepted currency.
alter table agro360.sales_orders add column if not exists currency char(3);
update agro360.sales_orders o set currency=v.currency
from agro360.sales_proposal_conversions c
join agro360.sales_proposal_versions v on v.tenant_id=c.tenant_id and v.proposal_id=c.proposal_id and v.version_number=c.version_number
where o.tenant_id=c.tenant_id and o.id=c.order_id and o.currency is null;
alter table agro360.sales_orders drop constraint if exists ck_sales_orders_currency_format;
alter table agro360.sales_orders add constraint ck_sales_orders_currency_format
    check(currency is null or currency ~ '^[A-Z]{3}$') not valid;
alter table agro360.sales_orders validate constraint ck_sales_orders_currency_format;

insert into agro360.platform_schema_versions(version,description,installed_at)
values('10.8.0','Moeda canônica e reconciliação na conversão de propostas',now())
on conflict(version) do nothing;

commit;
