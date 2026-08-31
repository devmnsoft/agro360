# Plataforma SaaS Agro360 Enterprise

A Sprint 45 consolida dois escopos: super administração MNSOFT e área isolada do tenant. O contexto JWT, as transações com `app.tenant_id` e RLS formam defesa em profundidade. Módulo inativo, tenant bloqueado ou permissão negada devem ser rejeitados também na rota.

O catálogo atende fazendas, produtores, cooperativas, agroindústrias, exportadoras, revendas, assistência técnica, consultorias, proteína animal, hortifruti, cadeia amazônica, logística e governo. A ativação depende do plano e das dependências persistidas.

## Ecossistema Sprint 46
Planos governam módulos e limites de usuários, propriedades, integrações, webhooks e idiomas. Upgrade/downgrade é solicitação sujeita a aprovação MNSOFT e histórico; a UI não representa pagamento processado.
