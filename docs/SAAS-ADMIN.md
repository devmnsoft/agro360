# Administracao SaaS — Sprint 29

## Estado frente ao plano mestre

Este é o equivalente local de SAAS-ADMINISTRATION.md; não criar manual duplicado. [Plano mestre](execucao/AGRO360-MASTER-PLAN.md), [matriz](TRACEABILITY-MATRIX-v0.2.0.md) e [checkpoint](EXECUTION-CHECKPOINT.md) prevalecem sobre alegações históricas de conclusão.

Login tenant-first e administração SaaS existem; identidade global/vínculos, MFA real, contexto assistido auditado e ciclo contratual completo permanecem requisitos E1/E2. Alterações concorrentes em usuários/perfis precisam de validação própria. SuperAdmin do instalador não foi autenticado no gate E0 desta entrega; não confundir o sucesso do administrador Santa Clara no banco descartável com homologação global. Provisionamento seguro e demo opt-in são AG-E1-004.

A area `/saas` e a API `/api/platform` sao exclusivas do perfil **Super Admin Plataforma**. A operacao cobre tenants, planos, uso e auditoria; usuarios de tenant usam somente `/api/account`. Toda consulta operacional permanece vinculada ao `ITenantContext`, transacao tenant-scoped e RLS.

## Operacao

1. Cadastre um plano ativo com modulos e limites positivos.
2. Cadastre a organizacao escolhendo o plano — IDs nunca sao digitados pelo operador.
3. Ative, suspenda ou reative informando motivo. O evento e persistido em `saas.tenant_status_events` e na auditoria.
4. Acompanhe consumo. A partir de 80% ha alerta; em 100% apenas novos registros sao bloqueados, sem exclusao.
5. Conceda override somente com motivo e validade futura.

Tenant suspenso/inativo nao possui acesso operacional. A identidade de plataforma nao concede edicao silenciosa de dados do cliente. As permissoes criticas do catalogo `saas.permission_catalog` devem ser homologadas no backend e na visibilidade dos comandos.
