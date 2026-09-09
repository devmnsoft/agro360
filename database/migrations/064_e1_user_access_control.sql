begin;

insert into agro360.identity_permissions(code,module,description) values
 ('account.users.read','Administracao da Conta','Consultar usuarios e seus perfis no proprio cliente.'),
 ('account.users.manage','Administracao da Conta','Gerenciar usuarios no proprio cliente.'),
 ('account.roles.read','Administracao da Conta','Consultar perfis no proprio cliente.'),
 ('account.roles.manage','Administracao da Conta','Gerenciar perfis dentro da propria autoridade.'),
 ('account.invitations.read','Administracao da Conta','Consultar convites do proprio cliente.'),
 ('account.invitations.manage','Administracao da Conta','Emitir, reenviar e cancelar convites.'),
 ('account.settings.read','Administracao da Conta','Consultar configuracoes da organizacao.'),
 ('account.settings.manage','Administracao da Conta','Alterar configuracoes da organizacao.'),
 ('account.security.read','Administracao da Conta','Consultar sessoes e dispositivos.'),
 ('account.security.manage','Administracao da Conta','Revogar sessoes e dispositivos.'),
 ('account.subscription.read','Administracao da Conta','Consultar plano, contrato e uso.'),
 ('account.subscription.manage','Administracao da Conta','Solicitar alteracao de contratacao.'),
 ('account.notifications.read','Administracao da Conta','Consultar notificacoes da conta.'),
 ('account.notifications.manage','Administracao da Conta','Tratar notificacoes da conta.'),
 ('platform.admin','Plataforma','Administrar a plataforma com autoridade global comprovada.')
on conflict(code) do update set module=excluded.module,description=excluded.description;

insert into agro360.identity_role_permissions(tenant_id,role_id,permission_id)
select r.tenant_id,r.id,p.id
from agro360.identity_roles r
cross join agro360.identity_permissions p
where lower(r.code) in ('tenant-administrator','super_admin')
  and p.code like 'account.%'
on conflict do nothing;

insert into agro360.identity_role_permissions(tenant_id,role_id,permission_id)
select r.tenant_id,r.id,p.id from agro360.identity_roles r cross join agro360.identity_permissions p
where lower(r.code)='super_admin' and p.code='platform.admin'
on conflict do nothing;

insert into agro360.saas_role_metadata(tenant_id,role_id,level)
select tenant_id,id,100 from agro360.identity_roles where lower(code)='tenant-administrator'
on conflict(tenant_id,role_id) do update set level=greatest(agro360.saas_role_metadata.level,excluded.level);

alter table agro360.identity_users add column if not exists must_change_password boolean not null default false;
alter table agro360.identity_users add column if not exists mfa_secret_encrypted text;
alter table agro360.saas_invitations add column if not exists delivery_status varchar(30) not null default 'PENDING_PROVIDER';
alter table agro360.saas_invitations add column if not exists accepted_at timestamptz;
alter table agro360.saas_invitations add column if not exists accepted_by_user_id uuid;

update agro360.identity_users
set password_hash='unprovisioned$'||encode(gen_random_bytes(32),'hex'),must_change_password=true,updated_at=now()
where password_hash in (
 'pbkdf2-sha512$210000$QWdybzM2ME1OU09GVDI2IQ==$XPdvwPxWZJO1J6BgBee2oNx3qEmuDipAFEqE+RRiaos=',
 'pbkdf2-sha512$210000$QWdybzM2MFNhbnRhMjYhIQ==$4UDbHTJM4k2raurPMDeniCv/McIHqZ1BxdqSD1I7GKc=',
 'pbkdf2-sha512$210000$QWdybzM2MERlbW9TZWVkIQ==$4VCMfY7wCNXW1YUuFkEKSgVnzQbUIYI0ThMD8anitDQ='
);

update agro360.identity_refresh_tokens rt set revoked_at=coalesce(rt.revoked_at,now())
where rt.revoked_at is null and exists(select 1 from agro360.identity_users u where u.id=rt.user_id and u.tenant_id=rt.tenant_id and u.password_hash like 'unprovisioned$%');

create table if not exists agro360.identity_user_status_events(
 id uuid primary key default gen_random_uuid(),
 tenant_id uuid not null references agro360.tenancy_tenants(id),
 user_id uuid not null,
 previous_status varchar(24) not null,
 new_status varchar(24) not null,
 reason varchar(1000) not null,
 created_at timestamptz not null default now(),
 created_by uuid not null,
 foreign key(tenant_id,user_id) references agro360.identity_users(tenant_id,id),
 foreign key(tenant_id,created_by) references agro360.identity_users(tenant_id,id),
 check(previous_status in('INVITED','ACTIVE','LOCKED','DISABLED')),
 check(new_status in('ACTIVE','DISABLED')),
 check(length(trim(reason)) between 5 and 1000)
);
create index if not exists ix_identity_user_status_events_target
 on agro360.identity_user_status_events(tenant_id,user_id,created_at desc);

alter table agro360.identity_user_status_events enable row level security;
alter table agro360.identity_user_status_events force row level security;
drop policy if exists tenant_isolation on agro360.identity_user_status_events;
drop policy if exists identity_user_status_events_tenant on agro360.identity_user_status_events;
drop policy if exists identity_user_status_events_tenant_isolation on agro360.identity_user_status_events;
create policy tenant_isolation on agro360.identity_user_status_events
 using (tenant_id=nullif(current_setting('app.tenant_id',true),'')::uuid)
 with check (tenant_id=nullif(current_setting('app.tenant_id',true),'')::uuid);

insert into agro360.platform_schema_versions(version,description,installed_at)
values('6.5.0','E1 - controle auditavel de acesso de usuarios do cliente',now())
on conflict(version) do nothing;

commit;
