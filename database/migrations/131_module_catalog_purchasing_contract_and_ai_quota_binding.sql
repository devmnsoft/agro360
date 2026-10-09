-- Migration 131: corrige a prova de contrato de módulos que travava Suprimentos em produção
-- (403 "módulo necessário não está contratado" mesmo para tenants adimplentes) e vincula as
-- execuções de IA à cota que realmente reservou o consumo.
--
-- Causa raiz comprovada no banco limpo: a prova de contrato usada por PermissionAuthorizationHandler
-- (EntitlementQueries.ModuleCodeSelect) lê platform_tenant_module_entitlements e plataforma marketplace,
-- e todo provisionamento/concessão retroativa (SaasService, migration 125) entra apenas por
-- platform_module_catalog. O catálogo nunca recebeu os códigos aceitos por Permissions.ModulesForPermission
-- para suprimentos, frota/manutenção, armazém, agroindústria, cooperativas, documentos, app de campo e
-- produtos verticais (incluindo 'purchasing'), então nenhuma dessas famílias podia ser contratada.
-- Este migration registra os códigos faltantes, estende os planos comerciais que vendem essas
-- capacidades, faz a concessão retroativa no padrão da migration 125 e alinha os fixtures de
-- homologação. Nenhum direito existente é removido: registrar catálogo não corta contratos vigentes.
--
-- Parte 2 (IA): ai_executions não guardava a cota da reserva, então reconcile/release/cleanup
-- debitavam a cota que casasse com CURRENT_DATE e mascaravam desvio com greatest(0,...). quota_id é
-- adicionado (NULL honesto para legado sem período correspondente) e result_payload permite replay do
-- resultado completo sem recobrar o provedor.
begin;

insert into agro360.platform_module_catalog(code,name,description,active) values
 ('purchasing','Suprimentos','Requisições, cotações, pedidos de compra, recebimento e glosas.',true),
 ('fleet','Frota e manutenção','Ativos, consumo, horas de máquina e ordens de manutenção.',true),
 ('warehousing','Armazenagem','Armazéns, posições e capacidade além do estoque central.',true),
 ('agroindustry','Agroindústria','Recebimento de matéria-prima, processamento e produção.',true),
 ('cooperatives','Cooperativas','Quadro de cooperados, rateios e logística cooperativa.',true),
 ('documents','Documentos','Documentos, evidências, dossiês e certidões anexadas.',true),
 ('mobile','App de campo','Instalações offline, checklists de campo e fila de sincronização.',true),
 ('rural-hr','Gestão de pessoas no campo','Quadros, jornada, remuneração e segurança rural.',true),
 ('verticals','Produtos verticais','Recursos de produtos verticais habilitados por contrato.',true)
on conflict(code) do update set name=excluded.name,description=excluded.description,active=true,updated_at=now();

-- Planos comerciais passam a carregar os módulos que efetivamente vendem; o guarda evita tocar em
-- planos ajustados manualmente (ex.: planos de teste dos scripts e2e) quando já contêm o módulo.
update agro360.saas_plans set modules=(select array(select distinct jsonb_array_elements_text(to_jsonb(modules||array['purchasing']::varchar[])))),updated_at=now()
where name in('Profissional','Cooperativa','Agroindústria','Enterprise') and not (to_jsonb(modules) ? 'purchasing');
update agro360.saas_plans set modules=(select array(select distinct jsonb_array_elements_text(to_jsonb(modules||array['fleet','warehousing']::varchar[])))),updated_at=now()
where name in('Cooperativa','Agroindústria','Enterprise') and not (to_jsonb(modules) ? 'fleet');
update agro360.saas_plans set modules=(select array(select distinct jsonb_array_elements_text(to_jsonb(modules||array['documents','mobile','cooperatives','rural-hr','verticals','agroindustry']::varchar[])))),updated_at=now()
where name='Enterprise' and not (to_jsonb(modules) ? 'documents');

-- Concessão retroativa do plano vigente para organizações ativas (mesmo padrão da migration 125):
-- somente direitos novos; bloqueios explícitos permanecem prevalecendo na leitura.
insert into agro360.platform_tenant_module_entitlements(tenant_id,module_id,status,reason,activated_at,origin)
select distinct o.tenant_id,m.id,'ACTIVE','Snapshot retroativo do plano vigente (migration 131)',now(),'PLAN'
from agro360.saas_organizations o
join agro360.saas_plans p on p.id=o.plan_id
cross join lateral unnest(p.modules) as planned(code)
join agro360.platform_module_catalog m on lower(m.code)=lower(planned.code)
where o.status='ACTIVE'
on conflict(tenant_id,module_id) do nothing;

-- Fixtures de homologação/DPD pré-existentes congelaram listas antigas de módulos; amplia apenas
-- tenants de teste ativos (bloqueados permanecem como prova de isolamento).
insert into agro360.platform_tenant_module_entitlements(tenant_id,module_id,status,reason,activated_at,origin)
select t.id,c.id,'ACTIVE','Módulos de suprimentos e operação habilitados para homologação (migration 131)',now(),'MANUAL'
from agro360.tenancy_tenants t
join agro360.platform_tenants pt on pt.id=t.id and pt.status='ACTIVE'
cross join lateral unnest(array['purchasing','fleet','warehousing','documents','mobile']) as wanted(code)
join agro360.platform_module_catalog c on lower(c.code)=wanted.code
where t.slug in('fazenda-santa-clara','cooperativa-vale-verde') or t.name like '%E2E%' or t.name like '%Homologa%' or t.name ilike '%plataforma%'
on conflict(tenant_id,module_id) do nothing;

-- Vínculo de execuções IA à cota que reservou + payload de resultado para replay fiel.
alter table agro360.ai_executions add column if not exists quota_id uuid references agro360.tenant_ai_quotas(id);
alter table agro360.ai_executions add column if not exists result_payload jsonb;
comment on column agro360.ai_executions.quota_id is 'Cota que realmente reservou esta execução; NULL em legado sem período correspondente, que não pode ser conciliada em outra competência.';
comment on column agro360.ai_executions.result_payload is 'Resultado completo persistido no COMPLETED (answer/data/draft) para replay idempotente sem novo consumo.';

-- Parte 3: mapeamento direto requisição→cotação. O ranking de vencedores dependia de casar
-- catalog_item_id+unidade entre itens, ambíguo quando o mesmo item/unidade aparecia em linhas
-- distintas da requisição. requisition_item_id torna o vínculo explícito desde a criação da cotação.
alter table agro360.procurement_quotation_items add column if not exists requisition_item_id uuid references agro360.procurement_requisition_items(id);
comment on column agro360.procurement_quotation_items.requisition_item_id is 'Linha aprovada da requisição que originou este item de cotação; base do pedido convertido e do saldo autorizado.';
update agro360.procurement_quotation_items qi set requisition_item_id=m.requisition_item_id,updated_at=now()
from (
    select qi2.id item_id,(array_agg(ri.id order by ri.id))[1] requisition_item_id
    from agro360.procurement_quotation_items qi2
    join agro360.procurement_quotations q on q.tenant_id=qi2.tenant_id and q.id=qi2.quotation_id
    join agro360.procurement_requisition_items ri on ri.tenant_id=q.tenant_id and ri.requisition_id=q.requisition_id
        and ri.catalog_item_id=qi2.catalog_item_id and upper(ri.unit)=upper(qi2.unit) and ri.deleted_at is null
    where qi2.requisition_item_id is null
    group by qi2.id having count(*)=1
) m where m.item_id=qi.id;
update agro360.ai_executions x set quota_id=q.id,updated_at=now()
from agro360.tenant_ai_quotas q
where x.quota_id is null and x.reserved_tokens>0 and q.tenant_id=x.tenant_id and q.use_case=x.use_case
  and (x.created_at at time zone 'UTC')::date between q.period_start and q.period_end;

insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.21.0', 'Catálogo de módulos contratuais de suprimentos/frota/armazém/agroindústria/cooperativas/documentos/app/verticais, planos e entitlements alinhados, vínculo de execuções IA à cota reservada e payload de replay', now())
on conflict (version) do update set description = excluded.description;
commit;
