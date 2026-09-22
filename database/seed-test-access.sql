-- Acessos determinísticos exclusivamente para desenvolvimento e homologação.
-- Os hashes são PBKDF2-HMAC-SHA512, 210.000 iterações, no formato de PasswordHasher.
begin;

-- Conta global vive no tenant técnico porque identity_users é tenant-scoped.
select set_config('app.tenant_id','00000000-0000-0000-0000-000000000001',true);
insert into agro360.identity_users
 (id,tenant_id,name,email,password_hash,status,normalized_document,document_type,must_change_password,mfa_enabled,deleted_at,updated_at)
values
 ('00000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000001','Super Admin Agro360','superadmin@agro360.local','pbkdf2-sha512$210000$QWdybzM2MFN1cGVyQWRtIQ==$Rw4HKe5g05CZwbA/Qiob2Q5i4oX/RfWIT6HzLtIduRo=','ACTIVE','00000000000','CPF',false,false,null,now())
on conflict(id) do update set name=excluded.name,email=excluded.email,password_hash=excluded.password_hash,status='ACTIVE',normalized_document=excluded.normalized_document,document_type='CPF',must_change_password=false,mfa_enabled=false,deleted_at=null,updated_at=now();
insert into agro360.identity_user_roles(tenant_id,user_id,role_id)
values ('00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000003') on conflict do nothing;
insert into agro360.platform_super_admins(id,user_id,active,deleted_at,updated_at)
values ('00000000-0000-0000-0000-000000000004','00000000-0000-0000-0000-000000000002',true,null,now())
on conflict(user_id) do update set active=true,deleted_at=null,updated_at=now();

-- Tenant demonstrativo e seu administrador real.
select set_config('app.tenant_id','30000000-0000-0000-0000-000000000001',true);
update agro360.tenancy_tenants set name='Fazenda Santa Clara',slug='santa-clara',status=1,plan_code='ENTERPRISE',deleted_at=null,updated_at=now()
where id='30000000-0000-0000-0000-000000000001';
update agro360.platform_tenants set legal_name='Fazenda Santa Clara',trade_name='Fazenda Santa Clara',normalized_document='11222333000181',plan_id=(select id from agro360.platform_saas_plans where code='ENTERPRISE'),status='ACTIVE',block_reason=null,deleted_at=null,updated_at=now()
where id='30000000-0000-0000-0000-000000000001';
insert into agro360.identity_users
 (id,tenant_id,name,email,password_hash,status,normalized_document,document_type,must_change_password,mfa_enabled,deleted_at,updated_at)
values
 ('30000000-0000-0000-0000-000000000003','30000000-0000-0000-0000-000000000001','Administrador Fazenda Santa Clara','admin.cliente@agro360.local','pbkdf2-sha512$210000$QWdybzM2MENsaWVudGUh$tljrR4u+oXvvHnMM3fcTR/HtHJ8iuX5pcXqXehWteH4=','ACTIVE','11222333000181','CNPJ',false,false,null,now())
on conflict(id) do update set tenant_id=excluded.tenant_id,name=excluded.name,email=excluded.email,password_hash=excluded.password_hash,status='ACTIVE',normalized_document=excluded.normalized_document,document_type='CNPJ',must_change_password=false,deleted_at=null,updated_at=now();
insert into agro360.identity_roles(id,tenant_id,code,name,is_system)
values ('30000000-0000-0000-0000-000000000004','30000000-0000-0000-0000-000000000001','tenant-administrator','Administrador do Cliente',true)
on conflict(id) do update set code=excluded.code,name=excluded.name,is_system=true;
insert into agro360.identity_user_roles(tenant_id,user_id,role_id)
values ('30000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000003','30000000-0000-0000-0000-000000000004') on conflict do nothing;
insert into agro360.identity_role_permissions(tenant_id,role_id,permission_id)
select '30000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000004',id from agro360.identity_permissions on conflict do nothing;

insert into agro360.platform_module_catalog(code,name,description,active) values
 ('agriculture','Operação Agro','Produtores, fazendas, talhões, safras e manejo.',true),
 ('inventory','Estoque','Lotes, saldos e movimentações.',true),
 ('commercial','Comercial','Contratos, pedidos e clientes.',true),
 ('logistics','Logística','Expedições e entregas.',true),
 ('finance','Financeiro','Faturamento e contas a receber.',true),
 ('traceability','Rastreabilidade','Cadeia de origem até a entrega.',true),
 ('environment-esg','Compliance','Documentos, evidências e validade.',true),
 ('analytics','Relatórios','Indicadores e relatórios.',true),
 ('platform','Administração','Usuários, perfis e configurações.',true)
on conflict(code) do update set name=excluded.name,description=excluded.description,active=true,updated_at=now();
insert into agro360.platform_tenant_module_entitlements(tenant_id,module_id,status,reason,activated_at)
select '30000000-0000-0000-0000-000000000001',id,'ACTIVE','Ambiente de demonstração Agro360',now()
from agro360.platform_module_catalog where code in ('agriculture','inventory','commercial','logistics','finance','traceability','environment-esg','analytics','platform')
on conflict(tenant_id,module_id) do update set status='ACTIVE',reason=excluded.reason,activated_at=coalesce(agro360.platform_tenant_module_entitlements.activated_at,excluded.activated_at),updated_at=now();
commit;
