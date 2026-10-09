-- Migration 132: governa as concessões da migration 131. Nome de tenant nunca foi prova de direito
-- comercial e a atualização de planos da 131 podia deixar planos parcialmente atualizados. O bloco
-- separa quatro preocupações distintas: registro de catálogo (permanece na 131), direitos comerciais
-- dos planos (reaplicados aqui por módulo, independentemente uns dos outros), snapshot retroativo
-- das organizações ativas (completado após a correção dos planos) e fixtures de homologação
-- (mantidos apenas pela allowlist explícita de tenants; demais concessões automáticas por padrão de
-- nome são revogadas e passam a vir exclusivamente do seed-test-access.sql, com tuplas nominais).
begin;

-- 1) Direitos comerciais por módulo: a 131 adicionava dois ou seis módulos de uma vez, mas guardava
-- cada update apenas pela ausência de um deles ('fleet' e 'documents'). Um plano que tivesse o módulo
-- guarda e perdesse os demais nunca os receberia. Reaplica cada par (plano, módulo) de forma
-- independente e idempotente; a guarda por módulo ausente continua respeitando ajustes manuais.
update agro360.saas_plans p set modules=(select array(select distinct jsonb_array_elements_text(to_jsonb(p.modules||array[w.code]::varchar[])))),updated_at=now()
from (values
    ('Profissional','purchasing'),
    ('Cooperativa','purchasing'),('Cooperativa','fleet'),('Cooperativa','warehousing'),
    ('Agroindústria','purchasing'),('Agroindústria','fleet'),('Agroindústria','warehousing'),
    ('Enterprise','purchasing'),('Enterprise','fleet'),('Enterprise','warehousing'),
    ('Enterprise','documents'),('Enterprise','mobile'),('Enterprise','cooperatives'),
    ('Enterprise','rural-hr'),('Enterprise','verticals'),('Enterprise','agroindustry')
) w(plan_name,code)
where p.name=w.plan_name and not (to_jsonb(p.modules) ? w.code);

-- 2) Snapshot retroativo complementar (mesmo padrão das migrations 125/131): organizações ativas em
-- planos corrigidos acima ainda não tinham entitlement para os módulos que faltaram no snapshot
-- anterior. Somente direitos novos; bloqueios explícitos permanecem prevalecendo na leitura.
insert into agro360.platform_tenant_module_entitlements(tenant_id,module_id,status,reason,activated_at,origin)
select distinct o.tenant_id,m.id,'ACTIVE','Snapshot retroativo do plano vigente (migration 132)',now(),'PLAN'
from agro360.saas_organizations o
join agro360.saas_plans p on p.id=o.plan_id
cross join lateral unnest(p.modules) as planned(code)
join agro360.platform_module_catalog m on lower(m.code)=lower(planned.code)
where o.status='ACTIVE'
on conflict(tenant_id,module_id) do nothing;

-- 3) Fixtures de homologação: o bloqueio da 131 concedia módulos por slug OU padrão de nome
-- (%E2E%, %Homologa%, %plataforma%). Qualquer tenant criado com um desses trechos no nome ganhava
-- módulos reais sem contrato. Revoga apenas as linhas daquele bloqueio fixture fora da allowlist
-- nominal; os dois tenants de demonstração do instalador mantêm seus direitos e os tenants de teste
-- dos scripts e2e continuam cobertos pelas tuplas explícitas do seed-test-access.sql. Concessões
-- manuais legítimas (outros reasons/origens) não são tocadas.
delete from agro360.platform_tenant_module_entitlements e
using agro360.tenancy_tenants t
where e.tenant_id=t.id
  and e.reason='Módulos de suprimentos e operação habilitados para homologação (migration 131)'
  and t.slug not in('fazenda-santa-clara','cooperativa-vale-verde');

insert into agro360.platform_schema_versions(version, description, installed_at)
values('11.22.0', 'Governança das concessões da migration 131: direitos de plano reaplicados por módulo, snapshot retroativo complementar e revogação de fixtures de homologação concedidos por padrão de nome (allowlist explícita)', now())
on conflict (version) do update set description = excluded.description;
commit;
