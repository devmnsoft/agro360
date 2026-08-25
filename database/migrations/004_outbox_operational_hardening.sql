alter table platform.outbox_messages
    add column if not exists correlation_id varchar(100) null,
    add column if not exists last_attempt_at timestamptz null,
    add column if not exists dead_lettered_at timestamptz null;

drop index if exists platform.ix_outbox_pending;
create index ix_outbox_pending
    on platform.outbox_messages (tenant_id, next_attempt_at, occurred_at)
    where processed_at is null and dead_lettered_at is null;

create index if not exists ix_outbox_dead_letter
    on platform.outbox_messages (tenant_id, dead_lettered_at desc)
    where dead_lettered_at is not null;

comment on column platform.outbox_messages.correlation_id is
    'Identificador técnico de correlação; o payload não deve ser escrito em logs.';
comment on column platform.outbox_messages.dead_lettered_at is
    'Marca falha permanente após o limite configurado de tentativas.';

update platform.modules
set status = 'FOUNDATION',
    description = case code
        when 'agriculture' then 'Backend transacional de safras, plantio e colheita; interface operacional completa permanece pendente.'
        when 'inventory' then 'Backend de produtos, depósitos, saldos e movimentos; interface operacional completa permanece pendente.'
        when 'livestock' then 'Backend de animais, pesagens e sanidade; interface operacional completa permanece pendente.'
        when 'cost' then 'Apropriação transacional inicial; consultas e interface operacional completas permanecem pendentes.'
        when 'commercial' then 'Backend de venda integrada; interface operacional e ciclo posterior permanecem pendentes.'
        when 'traceability' then 'Grafo e consulta pela API; navegação operacional completa permanece pendente.'
        else description
    end
where code in ('agriculture', 'inventory', 'livestock', 'cost', 'commercial', 'traceability');
