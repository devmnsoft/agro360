-- Dados demonstrativos opcionais. Execute somente em Development/local, depois do full install.
-- Não contém senha em texto puro; o usuário demo usa hash PBKDF2 da credencial dev documentada.
begin;
select set_config('app.tenant_id','20000000-0000-0000-0000-000000000001',true);

insert into agro360.organization_organizations(id,tenant_id,type,name,legal_name,document_number)
values ('20000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000001','ECONOMIC_GROUP','Grupo Santa Clara','Fazenda Santa Clara Ltda','00000000000191')
on conflict(id) do nothing;
insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status,normalized_document,document_type,must_change_password)
values ('20000000-0000-0000-0000-000000000003','20000000-0000-0000-0000-000000000001','Administrador Agro360','admin@agro360.local','pbkdf2-sha512$210000$QWdybzM2MERlbW9TZWVkIQ==$4VCMfY7wCNXW1YUuFkEKSgVnzQbUIYI0ThMD8anitDQ=','ACTIVE','00000000000191','CNPJ',true)
on conflict(id) do update set name=excluded.name,email=excluded.email,password_hash=excluded.password_hash,status='ACTIVE',deleted_at=null;
insert into agro360.identity_roles(id,tenant_id,code,name,is_system)
values ('20000000-0000-0000-0000-000000000004','20000000-0000-0000-0000-000000000001','tenant-administrator','Administrador do tenant',true)
on conflict(id) do nothing;
insert into agro360.identity_user_roles(tenant_id,user_id,role_id)
values ('20000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000003','20000000-0000-0000-0000-000000000004') on conflict do nothing;
insert into agro360.identity_role_permissions(tenant_id,role_id,permission_id)
select '20000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000004',id from agro360.identity_permissions on conflict do nothing;

insert into agro360.geo_farms(id,tenant_id,organization_id,name,state,municipality,total_area_ha,useful_area_ha,registration_number,created_by)
values ('20000000-0000-0000-0000-000000000010','20000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000002','Fazenda Santa Clara','GO','Rio Verde',850,720,'DEV-SC-001','20000000-0000-0000-0000-000000000003') on conflict(id) do nothing;
insert into agro360.geo_fields(id,tenant_id,farm_id,name,area_ha,created_by) values
 ('20000000-0000-0000-0000-000000000011','20000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000010','Talhão Norte',120,'20000000-0000-0000-0000-000000000003'),
 ('20000000-0000-0000-0000-000000000012','20000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000010','Talhão Sul',95,'20000000-0000-0000-0000-000000000003') on conflict(id) do nothing;
insert into agro360.agriculture_seasons(id,tenant_id,farm_id,name,crop,variety,start_date,end_date,status,planned_area_ha,expected_yield_per_ha,created_by)
values ('20000000-0000-0000-0000-000000000020','20000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000010','Safra Soja 2026/2027','Soja','BRS 284','2026-09-15','2027-03-31',1,215,60,'20000000-0000-0000-0000-000000000003') on conflict(id) do nothing;
insert into agro360.inventory_products(id,tenant_id,sku,name,category,base_unit,requires_lot,is_perishable,created_by)
values ('20000000-0000-0000-0000-000000000030','20000000-0000-0000-0000-000000000001','INS-SEM-SOJA','Semente de soja','SEED','kg',true,false,'20000000-0000-0000-0000-000000000003') on conflict(id) do nothing;
insert into agro360.inventory_warehouses(id,tenant_id,farm_id,code,name,type,created_by)
values ('20000000-0000-0000-0000-000000000031','20000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000010','DEP-INS','Depósito de Insumos','INPUTS','20000000-0000-0000-0000-000000000003') on conflict(id) do nothing;
insert into agro360.inventory_stock_balances(id,tenant_id,warehouse_id,product_id,unit,available,reserved,minimum,average_cost)
values ('20000000-0000-0000-0000-000000000032','20000000-0000-0000-0000-000000000001','20000000-0000-0000-0000-000000000031','20000000-0000-0000-0000-000000000030','kg',2500,0,500,8.75) on conflict(id) do nothing;
commit;
