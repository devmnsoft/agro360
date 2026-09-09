-- Compatibility bridge between the foundation receivable model and Sprint 8.
--
-- 001_foundation.sql created finance.receivables with the original operational
-- columns (amount, paid_amount and due_date). 007_sprint8_finance.sql reuses the
-- same table name and expects the richer financial-title columns. CREATE TABLE
-- IF NOT EXISTS does not evolve an existing relation, so a clean incremental
-- installation stopped when the Sprint 8 indexes/functions referenced due_on.
--
-- Keep both shapes during the legacy-schema transition. This migration is
-- additive and preserves existing data and the checksums of published files.

alter table finance.receivables
    add column if not exists customer_name varchar(160),
    add column if not exists document varchar(100),
    add column if not exists original_amount numeric(18,4),
    add column if not exists discount numeric(18,4) not null default 0,
    add column if not exists interest numeric(18,4) not null default 0,
    add column if not exists fine numeric(18,4) not null default 0,
    add column if not exists final_amount numeric(18,4),
    add column if not exists balance numeric(18,4),
    add column if not exists issued_on date,
    add column if not exists due_on date,
    add column if not exists settled_on date,
    add column if not exists account_id uuid,
    add column if not exists cost_center_id uuid,
    add column if not exists notes text,
    add column if not exists source_id uuid,
    add column if not exists cancel_reason text;

update finance.receivables
set customer_name = coalesce(customer_name, description),
    original_amount = coalesce(original_amount, amount),
    final_amount = coalesce(final_amount, amount),
    balance = coalesce(balance, greatest(amount - paid_amount, 0)),
    issued_on = coalesce(issued_on, created_at::date),
    due_on = coalesce(due_on, due_date),
    settled_on = coalesce(settled_on, case when status = 'PAID' then updated_at::date end)
where customer_name is null
   or original_amount is null
   or final_amount is null
   or balance is null
   or issued_on is null
   or due_on is null;

insert into platform.schema_versions(version, description, installed_at)
values ('0.4.1', 'Compatibility bridge for finance receivables', now())
on conflict (version) do nothing;
