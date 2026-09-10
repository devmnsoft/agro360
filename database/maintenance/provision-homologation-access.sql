select pg_advisory_xact_lock(hashtext('agro360-homologation-access-provisioning'));

do $$
begin
    if exists (
        select 1
        from agro360.platform_super_admins
        where active
          and deleted_at is null
          and user_id <> '00000000-0000-0000-0000-000000000002'::uuid
    ) then
        raise exception 'Já existe outro SuperAdmin ativo. Resolva a identidade canônica antes de redefinir acesso.';
    end if;

    if exists (
        select 1
        from agro360.identity_users
        where lower(email)='superadmin@mnsoft.com.br'
          and (tenant_id,id) <> ('00000000-0000-0000-0000-000000000001'::uuid,'00000000-0000-0000-0000-000000000002'::uuid)
          and deleted_at is null
    ) then
        raise exception 'E-mail do SuperAdmin encontrado em identidade divergente.';
    end if;

    if exists (
        select 1
        from agro360.identity_users
        where lower(email)='admin@santaclara.agro360.local'
          and (tenant_id,id) <> ('30000000-0000-0000-0000-000000000001'::uuid,'30000000-0000-0000-0000-000000000003'::uuid)
          and deleted_at is null
    ) then
        raise exception 'E-mail do administrador Santa Clara encontrado em identidade divergente.';
    end if;
end $$;

select set_config('app.tenant_id','00000000-0000-0000-0000-000000000001',true);

insert into agro360.tenancy_tenants(id,name,slug,timezone_id,status,plan_code)
values ('00000000-0000-0000-0000-000000000001','MNSOFT / Agro360 Platform','agro360-platform','America/Sao_Paulo',1,'ENTERPRISE')
on conflict(id) do update set
    name=excluded.name,
    slug=excluded.slug,
    timezone_id=excluded.timezone_id,
    status=1,
    plan_code='ENTERPRISE',
    deleted_at=null,
    updated_at=now();

insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status,mfa_enabled,mfa_secret_encrypted,must_change_password)
values ('00000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000001','Super Administrador MNSOFT','superadmin@mnsoft.com.br',@SuperHash,'ACTIVE',true,@MfaSecret,true)
on conflict(id) do update set
    name=excluded.name,
    email=excluded.email,
    password_hash=excluded.password_hash,
    status='ACTIVE',
    deleted_at=null,
    mfa_enabled=true,
    mfa_secret_encrypted=excluded.mfa_secret_encrypted,
    must_change_password=true,
    updated_at=now(),
    version=agro360.identity_users.version+1;

insert into agro360.identity_roles(id,tenant_id,code,name,is_system)
values ('00000000-0000-0000-0000-000000000003','00000000-0000-0000-0000-000000000001','SUPER_ADMIN','Super Administrador',true)
on conflict(id) do update set code='SUPER_ADMIN',name='Super Administrador',is_system=true;

insert into agro360.identity_user_roles(tenant_id,user_id,role_id)
values ('00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000002','00000000-0000-0000-0000-000000000003')
on conflict do nothing;

insert into agro360.identity_role_permissions(tenant_id,role_id,permission_id)
select '00000000-0000-0000-0000-000000000001','00000000-0000-0000-0000-000000000003',id
from agro360.identity_permissions
on conflict do nothing;

insert into agro360.platform_super_admins(id,user_id,active)
values ('00000000-0000-0000-0000-000000000004','00000000-0000-0000-0000-000000000002',true)
on conflict(user_id) do update set active=true,deleted_at=null,updated_at=now();

update agro360.identity_refresh_tokens
set revoked_at=coalesce(revoked_at,now())
where tenant_id='00000000-0000-0000-0000-000000000001'
  and user_id='00000000-0000-0000-0000-000000000002'
  and revoked_at is null;

insert into agro360.audit_logs(id,tenant_id,user_id,action,entity_type,entity_id,after_data)
values (
    gen_random_uuid(),
    '00000000-0000-0000-0000-000000000001',
    '00000000-0000-0000-0000-000000000002',
    'homologation_access_provisioned',
    'IdentityUser',
    '00000000-0000-0000-0000-000000000002',
    jsonb_build_object('environment',@Environment,'sessionsRevoked',true,'mustChangePassword',true,'mfaConfirmed',true)
);

select set_config('app.tenant_id','30000000-0000-0000-0000-000000000001',true);

insert into agro360.tenancy_tenants(id,name,slug,timezone_id,status,plan_code)
values ('30000000-0000-0000-0000-000000000001','Fazenda Santa Clara','santa-clara','America/Belem',1,'PROFESSIONAL')
on conflict(id) do update set
    name=excluded.name,
    slug=excluded.slug,
    timezone_id=excluded.timezone_id,
    status=1,
    plan_code='PROFESSIONAL',
    deleted_at=null,
    updated_at=now();

insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status,normalized_document,document_type,must_change_password)
values ('30000000-0000-0000-0000-000000000003','30000000-0000-0000-0000-000000000001','Administrador Santa Clara','admin@santaclara.agro360.local',@TenantHash,'ACTIVE','52998224725','CPF',true)
on conflict(id) do update set
    name=excluded.name,
    email=excluded.email,
    password_hash=excluded.password_hash,
    status='ACTIVE',
    deleted_at=null,
    normalized_document=excluded.normalized_document,
    document_type='CPF',
    must_change_password=true,
    updated_at=now(),
    version=agro360.identity_users.version+1;

insert into agro360.identity_roles(id,tenant_id,code,name,is_system)
values ('30000000-0000-0000-0000-000000000004','30000000-0000-0000-0000-000000000001','tenant-administrator','Administrador do Cliente',true)
on conflict(id) do update set code='tenant-administrator',name='Administrador do Cliente',is_system=true;

insert into agro360.identity_user_roles(tenant_id,user_id,role_id)
values ('30000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000003','30000000-0000-0000-0000-000000000004')
on conflict do nothing;

insert into agro360.identity_role_permissions(tenant_id,role_id,permission_id)
select '30000000-0000-0000-0000-000000000001','30000000-0000-0000-0000-000000000004',id
from agro360.identity_permissions
where code <> 'platform.admin'
on conflict do nothing;

insert into agro360.saas_usage_metrics(tenant_id)
values ('30000000-0000-0000-0000-000000000001')
on conflict(tenant_id) do nothing;

update agro360.identity_refresh_tokens
set revoked_at=coalesce(revoked_at,now())
where tenant_id='30000000-0000-0000-0000-000000000001'
  and user_id='30000000-0000-0000-0000-000000000003'
  and revoked_at is null;

insert into agro360.audit_logs(id,tenant_id,user_id,action,entity_type,entity_id,after_data)
values (
    gen_random_uuid(),
    '30000000-0000-0000-0000-000000000001',
    '30000000-0000-0000-0000-000000000003',
    'homologation_access_provisioned',
    'IdentityUser',
    '30000000-0000-0000-0000-000000000003',
    jsonb_build_object('environment',@Environment,'sessionsRevoked',true,'mustChangePassword',true)
);

do $$
begin
    if not exists (
        select 1
        from agro360.identity_users u
        join agro360.identity_user_roles ur on ur.tenant_id=u.tenant_id and ur.user_id=u.id
        join agro360.identity_roles r on r.tenant_id=ur.tenant_id and r.id=ur.role_id
        where u.tenant_id='00000000-0000-0000-0000-000000000001'
          and u.email='superadmin@mnsoft.com.br'
          and u.status='ACTIVE'
          and u.deleted_at is null
          and u.mfa_enabled
          and u.mfa_secret_encrypted is not null
          and r.code='SUPER_ADMIN'
    ) then
        raise exception 'Validação do SuperAdmin falhou.';
    end if;

    if not exists (
        select 1
        from agro360.platform_super_admins
        where user_id='00000000-0000-0000-0000-000000000002'
          and active
          and deleted_at is null
    ) then
        raise exception 'Autoridade global do SuperAdmin não foi gravada.';
    end if;

    if not exists (
        select 1
        from agro360.identity_users u
        join agro360.identity_user_roles ur on ur.tenant_id=u.tenant_id and ur.user_id=u.id
        join agro360.identity_roles r on r.tenant_id=ur.tenant_id and r.id=ur.role_id
        where u.tenant_id='30000000-0000-0000-0000-000000000001'
          and u.email='admin@santaclara.agro360.local'
          and u.status='ACTIVE'
          and u.deleted_at is null
          and r.code='tenant-administrator'
    ) then
        raise exception 'Validação do Administrador Santa Clara falhou.';
    end if;
end $$;
