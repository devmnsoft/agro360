-- AG-SaaS-ADM-006 - Hardening de RLS para contexto de plataforma SaaS
begin;
set local search_path to agro360, public;

drop policy if exists saas_organizations_tenant_isolation on agro360.saas_organizations;
create policy saas_organizations_tenant_isolation on agro360.saas_organizations
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

drop policy if exists identity_users_tenant_isolation on agro360.identity_users;
create policy identity_users_tenant_isolation on agro360.identity_users
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

drop policy if exists saas_tenant_restrictions_isolation on agro360.saas_tenant_restrictions;
create policy saas_tenant_restrictions_isolation on agro360.saas_tenant_restrictions
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

drop policy if exists saas_billing_payments_isolation on agro360.saas_billing_payments;
create policy saas_billing_payments_isolation on agro360.saas_billing_payments
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

drop policy if exists saas_billing_credits_isolation on agro360.saas_billing_credits;
create policy saas_billing_credits_isolation on agro360.saas_billing_credits
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

drop policy if exists saas_tenant_modules_tenant on agro360.saas_tenant_modules;
create policy saas_tenant_modules_tenant on agro360.saas_tenant_modules
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

drop policy if exists tenant_isolation on agro360.platform_tenant_module_entitlements;
create policy tenant_isolation on agro360.platform_tenant_module_entitlements
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

drop policy if exists saas_admin_audit_events_tenant_isolation on agro360.saas_admin_audit_events;
create policy saas_admin_audit_events_tenant_isolation on agro360.saas_admin_audit_events
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

drop policy if exists audit_saas_events_tenant_isolation on agro360.audit_saas_events;
create policy audit_saas_events_tenant_isolation on agro360.audit_saas_events
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

drop policy if exists tenant_isolation on agro360.portal_profiles;
create policy tenant_isolation on agro360.portal_profiles
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

drop policy if exists tenant_isolation on agro360.portal_permissions;
create policy tenant_isolation on agro360.portal_permissions
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

drop policy if exists tenant_isolation on agro360.portal_external_users;
create policy tenant_isolation on agro360.portal_external_users
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

drop policy if exists tenant_isolation on agro360.portal_external_user_links;
create policy tenant_isolation on agro360.portal_external_user_links
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

drop policy if exists tenant_isolation on agro360.portal_invitations;
create policy tenant_isolation on agro360.portal_invitations
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

drop policy if exists tenant_isolation on agro360.portal_terms_acceptances;
create policy tenant_isolation on agro360.portal_terms_acceptances
    using (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    )
    with check (
        (nullif(current_setting('app.tenant_id', true), '')::uuid is not null
         and tenant_id = nullif(current_setting('app.tenant_id', true), '')::uuid)
        or nullif(current_setting('app.platform_context', true), '') = 'true'
    );

insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.11.0', 'Hardening de RLS para contexto de plataforma SaaS (AG-SaaS-ADM-006)', now())
on conflict(version) do update set description = excluded.description;

commit;
