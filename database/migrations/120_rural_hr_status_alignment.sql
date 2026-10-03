-- Migration 120: Alinhamento de estados legados de RH Rural e preservacao de integridade de transicoes
-- Schema version 11.10.0
set local search_path to agro360, public;

-- 1. Alinha incidentes legados que foram gravados como 'ACTIVE' para 'OPEN'
update agro360.rural_hr_records
set status = 'OPEN', updated_at = now()
where kind = 'INCIDENT' and status = 'ACTIVE';

-- 2. Alinha acoes corretivas legadas que foram gravadas como 'ACTIVE' para 'OPEN'
update agro360.rural_hr_records
set status = 'OPEN', updated_at = now()
where kind = 'CORRECTIVE_ACTION' and status = 'ACTIVE';

-- 3. Alinha EPIs legados gravados como 'ACTIVE' para 'AVAILABLE'
update agro360.rural_hr_records
set status = 'AVAILABLE', updated_at = now()
where kind = 'PPE' and status = 'ACTIVE';

-- 4. Alinha transportes legados gravados como 'ACTIVE' para 'SCHEDULED'
update agro360.rural_hr_records
set status = 'SCHEDULED', updated_at = now()
where kind = 'TRANSPORT' and status = 'ACTIVE';

-- 5. Atualiza versao do schema
insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.10.0', 'Alinhamento de estados legados de RH Rural e integridade de transicoes (AG-HR-STATUS-001)', now())
on conflict (version) do update set description = excluded.description;
