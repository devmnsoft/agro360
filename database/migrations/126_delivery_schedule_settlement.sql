-- Migration 126: evolução integrada — liquidação administrativa do compromisso de entrega.
-- Schema 11.16.0. Não altera checksum de migration aplicada.
-- Entrega != liquidação: a liquidação é um passo administrativo explícito que NÃO altera
-- o status operacional do compromisso e NÃO escreve em finance_* / fiscal_* (sem NF/crédito/pagamento simulados).
-- Reaplicável: drop/add idempotentes de constraints com nome estável.
set local search_path to agro360, public;

alter table agro360.sales_delivery_schedules
  add column if not exists settled_at timestamptz,
  add column if not exists settled_by uuid,
  add column if not exists settlement_reason text;

comment on column agro360.sales_delivery_schedules.settled_at is 'Instante da liquidação administrativa; nulo indica não liquidado.';
comment on column agro360.sales_delivery_schedules.settled_by is 'Usuário que executou a liquidação.';
comment on column agro360.sales_delivery_schedules.settlement_reason is 'Motivo registrado na liquidação.';

alter table agro360.sales_delivery_schedules drop constraint if exists sales_delivery_schedules_settled_by_fk;
alter table agro360.sales_delivery_schedules
  add constraint sales_delivery_schedules_settled_by_fk
  foreign key (tenant_id, settled_by) references agro360.identity_users(tenant_id, id);

alter table agro360.sales_delivery_schedules drop constraint if exists ck_sales_delivery_schedules_settled_pair;
alter table agro360.sales_delivery_schedules
  add constraint ck_sales_delivery_schedules_settled_pair
  check ((settled_at is null) = (settled_by is null));

-- Extensão da identidade das operações: SETTLE vira operação de primeira classe do compromisso.
-- O constraint original é criado sem nome e o PostgreSQL deriva deterministicamente
-- "<tabela>_<coluna>_check" no caminho de instalação completa e no incremental.
alter table agro360.sales_delivery_schedule_operations
  drop constraint if exists sales_delivery_schedule_operations_operation_check;
alter table agro360.sales_delivery_schedule_operations drop constraint if exists ck_sales_delivery_schedule_operations_operation;
alter table agro360.sales_delivery_schedule_operations
  add constraint ck_sales_delivery_schedule_operations_operation
  check (operation in ('RESCHEDULE', 'CANCEL', 'SETTLE'));

create index if not exists ix_sales_delivery_schedules_settled
  on agro360.sales_delivery_schedules(tenant_id, planned_date)
  where settled_at is not null;

insert into agro360.platform_schema_versions(version, description, installed_at)
 values('11.16.0', 'Liquidação de compromissos de entrega: passo administrativo sem simulação financeira', now())
 on conflict (version) do update set description = excluded.description;
