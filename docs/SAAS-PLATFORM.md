# Plataforma SaaS Agro360 Enterprise

A Sprint 45 consolida dois escopos: super administração MNSOFT e área isolada do tenant. O contexto JWT, as transações com `app.tenant_id` e RLS formam defesa em profundidade. Módulo inativo, tenant bloqueado ou permissão negada devem ser rejeitados também na rota.

O catálogo atende fazendas, produtores, cooperativas, agroindústrias, exportadoras, revendas, assistência técnica, consultorias, proteína animal, hortifruti, cadeia amazônica, logística e governo. A ativação depende do plano e das dependências persistidas.

## Ecossistema Sprint 46
Planos governam módulos e limites de usuários, propriedades, integrações, webhooks e idiomas. Upgrade/downgrade é solicitação sujeita a aprovação MNSOFT e histórico; a UI não representa pagamento processado.

## Sprint 47 — CRM e ciclo do cliente

A plataforma integra CRM, pipeline, propostas com total no backend, contratos SaaS, implantação assistida, suporte/SLA, saúde explicável, conhecimento e portal isolado. As novas rotas exigem permissões específicas, as tabelas usam auditoria/RLS por tenant e toda comunicação sem provedor permanece pendente na outbox. A experiência responsiva usa funil, timeline, badges e o componente recolhível **Como usar esta tela**. Consulte `docs/CRM-COMMERCIAL.md`, `docs/CUSTOMER-SUCCESS.md`, `docs/SUPPORT.md` e a migração `047_crm_customer_lifecycle.sql`.
