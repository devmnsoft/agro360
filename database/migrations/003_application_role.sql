do $$
declare
    schema_name text;
    application_schemas text[] := array[
        'platform', 'identity', 'tenancy', 'organization', 'geo', 'agriculture',
        'agronomy', 'precision_agriculture', 'livestock', 'pasture', 'dairy',
        'forestry', 'inventory', 'warehouse', 'fleet', 'purchasing', 'finance',
        'cost', 'commercial', 'logistics', 'traceability', 'documents',
        'environment', 'hr', 'workflow', 'notification', 'analytics', 'ai',
        'iot', 'integration', 'audit'
    ];
begin
    if not exists (select 1 from pg_roles where rolname = 'agro360_app') then
        raise notice 'Role agro360_app is not installed; grants skipped for this environment.';
        return;
    end if;

    execute format('grant connect on database %I to agro360_app', current_database());
    foreach schema_name in array application_schemas loop
        execute format('grant usage on schema %I to agro360_app', schema_name);
        execute format('grant select, insert, update, delete on all tables in schema %I to agro360_app', schema_name);
        execute format('grant usage, select on all sequences in schema %I to agro360_app', schema_name);
        execute format('alter default privileges in schema %I grant select, insert, update, delete on tables to agro360_app', schema_name);
        execute format('alter default privileges in schema %I grant usage, select on sequences to agro360_app', schema_name);
    end loop;

    revoke delete on agriculture.field_operations from agro360_app;
    revoke delete on inventory.stock_movements from agro360_app;
    revoke delete on livestock.animal_events from agro360_app;
    revoke update, delete on audit.logs from agro360_app;
    revoke update, delete on traceability.edges from agro360_app;
end;
$$;
