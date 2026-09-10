-- Move usable records from the preserved 001 shape into the canonical Sprint 8
-- shape. Zero-value legacy records remain in the archive because the newer
-- invariant requires positive titles; no historical row is deleted.
do $$
declare
    tenant record;
    account_id uuid;
begin
    if to_regclass('finance.receivables_foundation_legacy') is null then
        return;
    end if;

    for tenant in select distinct tenant_id from finance.receivables_foundation_legacy loop
        select id into account_id
        from finance.chart_of_accounts
        where tenant_id = tenant.tenant_id and code = 'LEGACY-RECEIVABLES';

        if account_id is null then
            account_id := gen_random_uuid();
            insert into finance.chart_of_accounts
                (id, tenant_id, code, name, type, nature, category, active, created_by)
            values
                (account_id, tenant.tenant_id, 'LEGACY-RECEIVABLES',
                 'Recebíveis migrados da fundação', 'REVENUE', 'CREDIT',
                 'MIGRATION', true, '00000000-0000-0000-0000-000000000000');
        end if;

        insert into finance.receivables
            (id, tenant_id, customer_name, document, original_amount, discount,
             interest, fine, final_amount, balance, issued_on, due_on, settled_on,
             account_id, notes, source_id, status, created_at, created_by,
             updated_at, updated_by)
        select
            legacy.id, legacy.tenant_id, left(legacy.description, 160), null,
            legacy.amount, 0, 0, 0, legacy.amount,
            greatest(legacy.amount - legacy.paid_amount, 0),
            legacy.created_at::date, legacy.due_date,
            case when legacy.status = 'PAID' then coalesce(legacy.updated_at, legacy.created_at)::date end,
            account_id, 'Migrado de finance.receivables (001_foundation.sql)',
            legacy.sale_id,
            case legacy.status
                when 'PAID' then 'RECEIVED'
                when 'OVERDUE' then 'OPEN'
                else legacy.status
            end,
            legacy.created_at, legacy.created_by, legacy.updated_at, legacy.updated_by
        from finance.receivables_foundation_legacy legacy
        where legacy.tenant_id = tenant.tenant_id and legacy.amount > 0
        on conflict (id) do nothing;
    end loop;
end $$;
