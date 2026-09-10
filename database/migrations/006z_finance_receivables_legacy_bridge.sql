-- Compatibility bridge between the finance.receivables shape published in 001
-- and the Sprint 8 finance model. Keep 001/007 immutable so their checksums
-- remain valid in databases that already recorded either migration.
do $$
begin
    if to_regclass('finance.receivables') is not null
       and exists (
           select 1 from information_schema.columns
           where table_schema = 'finance' and table_name = 'receivables' and column_name = 'due_date')
       and not exists (
           select 1 from information_schema.columns
           where table_schema = 'finance' and table_name = 'receivables' and column_name = 'due_on') then
        alter table finance.receivables rename to receivables_foundation_legacy;
        alter index if exists finance.ix_receivables_due rename to ix_receivables_foundation_legacy_due;
    end if;
end $$;
