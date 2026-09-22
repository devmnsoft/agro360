-- Dados comerciais demonstrativos da Sprint 21. Execute após agro360-postgres-full.sql.
-- Usa o tenant de demonstração criado pelo instalador canônico e não cria credenciais.
begin;
select set_config('app.tenant_id','20000000-0000-0000-0000-000000000001',true);
insert into agro360.deployment_onboardings(id,tenant_id,segment,template_code,status,payload,created_by)
values('21000000-0000-0000-0000-000000000002','20000000-0000-0000-0000-000000000001','GRAINS','GRAINS','COMPLETED',
'{"property":"Fazenda Horizonte","fields":["Talhão Norte","Talhão Sul"],"cycle":"Safra 2026/27","cultures":["Soja","Milho"],"suppliers":["Insumos Cerrado"],"stock":["Semente de soja","Fertilizante"],"herds":["Lote Corte 01"],"purchases":["Compra de insumos"],"sales":["Venda futura soja"],"finance":["Custeio safra"],"weighingTickets":["Romaneio 001"],"traceableLots":["SOJA-2026-001"],"processing":["Secagem"],"routes":["Fazenda-Armazém"],"indicators":["Produtividade"],"compliance":["CAR verificado"],"cooperativeMembers":["Produtor João"]}',
'20000000-0000-0000-0000-000000000003') on conflict(id) do nothing;
insert into agro360.deployment_checklist(tenant_id,item_code,label,required,completed,completed_at,updated_by,sort_order)
select '20000000-0000-0000-0000-000000000001',code,label,required,true,now(),'20000000-0000-0000-0000-000000000003',sort_order from agro360.deployment_checklist_catalog on conflict(tenant_id,item_code) do nothing;
commit;
