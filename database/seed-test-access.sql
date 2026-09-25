-- Fixtures deterministicas de acesso, exclusivas para Development/Homologation.
-- Execute somente depois do schema consolidado. O script usa slug, documento,
-- e-mail e codigo de perfil/modulo como chaves naturais; UUIDs nunca sao
-- pressupostos. Senhas em claro nao sao armazenadas.
begin;

select set_config('app.tenant_id', '00000000-0000-0000-0000-000000000001', true);

insert into agro360.platform_module_catalog(code,name,description,active) values
 ('agriculture','Operação Agro','Operação agrícola integrada.',true),
 ('producers','Produtores','Cadastro de produtores.',true),
 ('properties','Fazendas','Cadastro de fazendas.',true),
 ('fields','Talhões','Cadastro de talhões.',true),
 ('seasons','Safras','Planejamento de safras.',true),
 ('inventory','Estoque','Lotes, saldos e movimentações.',true),
 ('commercial','Comercial','Clientes e ciclo comercial.',true),
 ('contracts','Contratos','Contratos comerciais.',true),
 ('orders','Pedidos','Pedidos e aprovações.',true),
 ('logistics','Logística','Expedições e entregas.',true),
 ('finance','Financeiro','Faturamento e contas a receber.',true),
 ('traceability','Rastreabilidade','Cadeia de origem até a entrega.',true),
 ('environment-esg','Compliance','Documentos, evidências e validade.',true),
 ('analytics','Relatórios','Indicadores e relatórios.',true),
 ('platform','Administração','Usuários, perfis e configurações.',true)
on conflict(code) do update set name=excluded.name,description=excluded.description,
 active=true,updated_at=now();

do $seed$
declare
 platform_id uuid; santa_id uuid; vale_id uuid; blocked_id uuid;
 v_user_id uuid; role_id uuid; profile_id uuid;
 admin_hash constant text := 'pbkdf2-sha512$210000$QWdybzM2MFN1cGVyQWRtIQ==$Rw4HKe5g05CZwbA/Qiob2Q5i4oX/RfWIT6HzLtIduRo=';
 client_hash constant text := 'pbkdf2-sha512$210000$QWdybzM2MENsaWVudGUhIQ==$VckKAKONnkrSQLXHTkL6w6BLbLUEO7krSY2DRFYpHd0=';
 operator_hash constant text := 'pbkdf2-sha512$210000$QWdybzM2ME9wZXJhZG9yIQ==$/0AX57LMqF6X3yzEPavcE2vFRSM/PxRg3sXMTjbZjkQ=';
begin
 -- A tabela canonica nao possui uma coluna textual "code"; o slug e a chave
 -- natural equivalente, estavel e usada pelo login.
 select t.id into platform_id from agro360.tenancy_tenants t left join agro360.platform_tenants p on p.id=t.id
 where t.slug='agro360-platform' or p.normalized_document='00000000000000' order by (t.slug='agro360-platform') desc limit 1;
 if platform_id is null then platform_id:=gen_random_uuid(); insert into agro360.tenancy_tenants(id,name,slug,timezone_id,status,plan_code) values(platform_id,'Agro360 Plataforma','agro360-platform','America/Sao_Paulo',1,'ENTERPRISE');
 else update agro360.tenancy_tenants set name='Agro360 Plataforma',slug='agro360-platform',timezone_id='America/Sao_Paulo',plan_code='ENTERPRISE',updated_at=now() where id=platform_id; end if;

 -- A instalacao historica usava slug "santa-clara". O documento identifica e
 -- atualiza esse mesmo registro, em vez de criar um segundo cliente.
 select t.id into santa_id from agro360.tenancy_tenants t left join agro360.platform_tenants p on p.id=t.id
 where t.slug in ('fazenda-santa-clara','santa-clara') or p.normalized_document='11222333000181' order by (p.normalized_document='11222333000181') desc limit 1;
 if santa_id is null then santa_id:=gen_random_uuid(); insert into agro360.tenancy_tenants(id,name,slug,timezone_id,status,plan_code) values(santa_id,'Fazenda Santa Clara','fazenda-santa-clara','America/Sao_Paulo',1,'ENTERPRISE');
 else update agro360.tenancy_tenants set name='Fazenda Santa Clara',slug='fazenda-santa-clara',timezone_id='America/Sao_Paulo',plan_code='ENTERPRISE',updated_at=now() where id=santa_id; end if;

 select t.id into vale_id from agro360.tenancy_tenants t left join agro360.platform_tenants p on p.id=t.id where t.slug='cooperativa-vale-verde' or p.normalized_document='22333444000191' limit 1;
 if vale_id is null then vale_id:=gen_random_uuid(); insert into agro360.tenancy_tenants(id,name,slug,timezone_id,status,plan_code) values(vale_id,'Cooperativa Vale Verde','cooperativa-vale-verde','America/Sao_Paulo',1,'GROWTH');
 else update agro360.tenancy_tenants set name='Cooperativa Vale Verde',slug='cooperativa-vale-verde',timezone_id='America/Sao_Paulo',plan_code='GROWTH',updated_at=now() where id=vale_id; end if;

 select t.id into blocked_id from agro360.tenancy_tenants t left join agro360.platform_tenants p on p.id=t.id where t.slug='fazenda-bloqueada-teste' or p.normalized_document='33444555000172' limit 1;
 if blocked_id is null then blocked_id:=gen_random_uuid(); insert into agro360.tenancy_tenants(id,name,slug,timezone_id,status,plan_code) values(blocked_id,'Fazenda Bloqueada Teste','fazenda-bloqueada-teste','America/Sao_Paulo',3,'ENTERPRISE');
 else update agro360.tenancy_tenants set name='Fazenda Bloqueada Teste',slug='fazenda-bloqueada-teste',timezone_id='America/Sao_Paulo',plan_code='ENTERPRISE',updated_at=now() where id=blocked_id; end if;

 insert into agro360.platform_tenants
  (id,legal_name,trade_name,normalized_document,customer_type,primary_segment,primary_email,legal_contact,plan_id,status,block_reason)
 values
  (platform_id,'Agro360 Plataforma','Agro360 Plataforma','00000000000000','PLATFORM','SOFTWARE','superadmin@agro360.local','Super Admin Agro360',(select id from agro360.platform_saas_plans where code='ENTERPRISE'),'ACTIVE',null),
  (santa_id,'Fazenda Santa Clara','Fazenda Santa Clara','11222333000181','RURAL_PRODUCER','AGRICULTURE','admin.santaclara@agro360.local','Admin Fazenda Santa Clara',(select id from agro360.platform_saas_plans where code='ENTERPRISE'),'ACTIVE',null),
  (vale_id,'Cooperativa Vale Verde','Cooperativa Vale Verde','22333444000191','COOPERATIVE','COOPERATIVE','admin.valeverde@agro360.local','Admin Cooperativa Vale Verde',coalesce((select id from agro360.platform_saas_plans where code='GROWTH'),(select id from agro360.platform_saas_plans where code='ENTERPRISE')),'ACTIVE',null),
  (blocked_id,'Fazenda Bloqueada Teste','Fazenda Bloqueada Teste','33444555000172','RURAL_PRODUCER','AGRICULTURE','admin.bloqueado@agro360.local','Admin Fazenda Bloqueada',(select id from agro360.platform_saas_plans where code='ENTERPRISE'),'BLOCKED','Tenant de teste bloqueado para validação de acesso')
 on conflict(id) do update set legal_name=excluded.legal_name,trade_name=excluded.trade_name,
  normalized_document=excluded.normalized_document,customer_type=excluded.customer_type,
  primary_segment=excluded.primary_segment,primary_email=excluded.primary_email,
  legal_contact=excluded.legal_contact,plan_id=excluded.plan_id,updated_at=now();

 -- A reaplicacao acrescenta apenas direitos ausentes. Direitos suspensos ou
 -- removidos por um administrador nao sao reativados silenciosamente.
 insert into agro360.platform_tenant_module_entitlements(tenant_id,module_id,status,reason,activated_at)
 select santa_id,id,'ACTIVE','Fixture de homologação Santa Clara',now()
 from agro360.platform_module_catalog where code in
 ('agriculture','producers','properties','fields','seasons','inventory','commercial','contracts','orders','logistics','finance','traceability','environment-esg','analytics','platform')
 on conflict(tenant_id,module_id) do nothing;
 insert into agro360.platform_tenant_module_entitlements(tenant_id,module_id,status,reason,activated_at)
 select vale_id,id,'ACTIVE','Fixture de isolamento Vale Verde',now()
 from agro360.platform_module_catalog where code in ('agriculture','inventory','commercial','logistics','traceability','analytics')
 on conflict(tenant_id,module_id) do nothing;
 insert into agro360.platform_tenant_module_entitlements(tenant_id,module_id,status,reason,activated_at)
 select blocked_id,id,'ACTIVE','Módulos preservados; tenant bloqueado',now()
 from agro360.platform_module_catalog where code in ('agriculture','inventory')
 on conflict(tenant_id,module_id) do nothing;

 -- SUPERADMIN GLOBAL (identity_users e protegida por tenant, portanto a conta
 -- reside no tenant tecnico, e a elevacao global vem de platform_super_admins).
 perform set_config('app.tenant_id',platform_id::text,true);
 select u.id into v_user_id from agro360.identity_users u where u.tenant_id=platform_id and lower(u.email)='superadmin@agro360.local' and u.deleted_at is null;
 if v_user_id is null then v_user_id:=gen_random_uuid(); end if;
 insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status,normalized_document,document_type,must_change_password,mfa_enabled)
 values(v_user_id,platform_id,'Super Admin Agro360','superadmin@agro360.local',admin_hash,'ACTIVE','00000000000','CPF',false,false)
 on conflict(id) do update set name=excluded.name,email=excluded.email,normalized_document=excluded.normalized_document,document_type='CPF',updated_at=now();
 select id into role_id from agro360.identity_roles where tenant_id=platform_id and code='SUPER_ADMIN';
 if role_id is null then role_id:=gen_random_uuid(); end if;
 insert into agro360.identity_roles(id,tenant_id,code,name,is_system) values(role_id,platform_id,'SUPER_ADMIN','SuperAdmin',true)
 on conflict(tenant_id,code) do update set name='SuperAdmin',is_system=true;
 select id into strict role_id from agro360.identity_roles where tenant_id=platform_id and code='SUPER_ADMIN';
 insert into agro360.identity_user_roles values(platform_id,v_user_id,role_id) on conflict do nothing;
 insert into agro360.identity_role_permissions select platform_id,role_id,id from agro360.identity_permissions on conflict do nothing;
 insert into agro360.platform_super_admins(id,user_id,active) values(gen_random_uuid(), v_user_id, true)
 on conflict(user_id) do nothing;

 -- Administrador e operador Santa Clara.
 perform set_config('app.tenant_id',santa_id::text,true);
 select id into role_id from agro360.identity_roles where tenant_id=santa_id and code='tenant-administrator';
 if role_id is null then role_id:=gen_random_uuid(); end if;
 insert into agro360.identity_roles(id,tenant_id,code,name,is_system) values(role_id,santa_id,'tenant-administrator','TenantAdmin',true)
 on conflict(tenant_id,code) do update set name='TenantAdmin',is_system=true;
 select id into strict role_id from agro360.identity_roles where tenant_id=santa_id and code='tenant-administrator';
 insert into agro360.identity_role_permissions select santa_id,role_id,id from agro360.identity_permissions on conflict do nothing;
 select u.id into v_user_id from agro360.identity_users u where u.tenant_id=santa_id and lower(u.email) in ('admin.santaclara@agro360.local','admin.cliente@agro360.local') and u.deleted_at is null order by (lower(u.email)='admin.santaclara@agro360.local') desc limit 1;
 if v_user_id is null then v_user_id:=gen_random_uuid(); end if;
 update agro360.identity_users set name='Admin Fazenda Santa Clara',email='admin.santaclara@agro360.local',normalized_document='11222333000181',document_type='CNPJ',updated_at=now() where id=v_user_id;
 if not found then insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status,normalized_document,document_type,must_change_password,mfa_enabled) values(v_user_id,santa_id,'Admin Fazenda Santa Clara','admin.santaclara@agro360.local',client_hash,'ACTIVE','11222333000181','CNPJ',false,false); end if;
 insert into agro360.identity_user_roles values(santa_id,v_user_id,role_id) on conflict do nothing;
 select id into profile_id from agro360.platform_profiles where tenant_id=santa_id and name='TenantAdmin';
 if profile_id is null then profile_id:=gen_random_uuid(); insert into agro360.platform_profiles(id,tenant_id,name,is_template,active) values(profile_id,santa_id,'TenantAdmin',false,true); end if;
 insert into agro360.platform_user_profiles(tenant_id,user_id,profile_id,is_primary) values(santa_id,v_user_id,profile_id,true) on conflict do nothing;

 select id into role_id from agro360.identity_roles where tenant_id=santa_id and code='operator';
 if role_id is null then role_id:=gen_random_uuid(); end if;
 insert into agro360.identity_roles(id,tenant_id,code,name,is_system) values(role_id,santa_id,'operator','Operador',true) on conflict(tenant_id,code) do update set name='Operador',is_system=true;
 select id into strict role_id from agro360.identity_roles where tenant_id=santa_id and code='operator';
 insert into agro360.identity_role_permissions
 select santa_id,role_id,id from agro360.identity_permissions where code like any(array['properties.%','agriculture.%','inventory.read','dashboard.%','traceability.%']) on conflict do nothing;
 select u.id into v_user_id from agro360.identity_users u where u.tenant_id=santa_id and lower(u.email)='operador.santaclara@agro360.local' and u.deleted_at is null;
 if v_user_id is null then v_user_id:=gen_random_uuid(); end if;
 insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status,normalized_document,document_type,must_change_password,mfa_enabled) values(v_user_id,santa_id,'Operador Fazenda Santa Clara','operador.santaclara@agro360.local',operator_hash,'ACTIVE','11122233344','CPF',false,false)
 on conflict(id) do update set name=excluded.name,email=excluded.email,normalized_document=excluded.normalized_document,document_type='CPF',updated_at=now();
 insert into agro360.identity_user_roles values(santa_id,v_user_id,role_id) on conflict do nothing;
 select id into profile_id from agro360.platform_profiles where tenant_id=santa_id and name='Operador';
 if profile_id is null then profile_id:=gen_random_uuid(); insert into agro360.platform_profiles(id,tenant_id,name,is_template,active) values(profile_id,santa_id,'Operador',false,true); end if;
 insert into agro360.platform_user_profiles(tenant_id,user_id,profile_id,is_primary) values(santa_id,v_user_id,profile_id,true) on conflict do nothing;

 -- Administradores Vale Verde e tenant bloqueado.
 foreach vale_id in array array[vale_id,blocked_id] loop
  perform set_config('app.tenant_id',vale_id::text,true);
  select id into role_id from agro360.identity_roles where tenant_id=vale_id and code='tenant-administrator';
  if role_id is null then role_id:=gen_random_uuid(); end if;
  insert into agro360.identity_roles(id,tenant_id,code,name,is_system) values(role_id,vale_id,'tenant-administrator','TenantAdmin',true) on conflict(tenant_id,code) do update set name='TenantAdmin',is_system=true;
  select id into strict role_id from agro360.identity_roles where tenant_id=vale_id and code='tenant-administrator';
  insert into agro360.identity_role_permissions select vale_id,role_id,id from agro360.identity_permissions on conflict do nothing;
  select u.id into v_user_id from agro360.identity_users u where u.tenant_id=vale_id and lower(u.email)=case when vale_id=blocked_id then 'admin.bloqueado@agro360.local' else 'admin.valeverde@agro360.local' end and u.deleted_at is null;
  if v_user_id is null then v_user_id:=gen_random_uuid(); end if;
  insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status,normalized_document,document_type,must_change_password,mfa_enabled)
  values(v_user_id,vale_id,case when vale_id=blocked_id then 'Admin Fazenda Bloqueada' else 'Admin Cooperativa Vale Verde' end,case when vale_id=blocked_id then 'admin.bloqueado@agro360.local' else 'admin.valeverde@agro360.local' end,client_hash,'ACTIVE',case when vale_id=blocked_id then '33444555000172' else '22333444000191' end,'CNPJ',false,false)
  on conflict(id) do update set name=excluded.name,email=excluded.email,normalized_document=excluded.normalized_document,document_type='CNPJ',updated_at=now();
  insert into agro360.identity_user_roles values(vale_id,v_user_id,role_id) on conflict do nothing;
  select id into profile_id from agro360.platform_profiles where tenant_id=vale_id and name='TenantAdmin';
  if profile_id is null then profile_id:=gen_random_uuid(); insert into agro360.platform_profiles(id,tenant_id,name,is_template,active) values(profile_id,vale_id,'TenantAdmin',false,true); end if;
  insert into agro360.platform_user_profiles(tenant_id,user_id,profile_id,is_primary) values(vale_id,v_user_id,profile_id,true) on conflict do nothing;
 end loop;
end $seed$;

commit;
