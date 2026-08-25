-- Execute após 009; idempotente e sem credenciais.
insert into inventory.products(id,tenant_id,sku,name,category,base_unit,requires_lot,created_by)
select gen_random_uuid(),t.id,'AMZ-'||x.sku,x.name,'AMAZON_REGIONAL',x.unit,true,(select id from identity.users where tenant_id=t.id order by created_at limit 1)
from tenancy.tenants t cross join (values('ACAI','Açaí','kg'),('TUCUPI','Tucupi','l'),('CACAU','Cacau','kg'),('CASTANHA','Castanha-do-pará','kg'),('MANDIOCA','Mandioca/Farinha','kg'),('GEN','Produto genérico','kg')) x(sku,name,unit)
where exists(select 1 from identity.users where tenant_id=t.id) and not exists(select 1 from inventory.products p where p.tenant_id=t.id and p.sku='AMZ-'||x.sku);
