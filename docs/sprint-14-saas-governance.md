# Sprint 14 — governança SaaS

A Sprint 14 entrega governança multiempresa de ponta a ponta: organizações e seus estados, onboarding, catálogo de planos, medição de uso, RBAC, convites, sessões, dispositivos, notificações, configurações, portal do cliente e dashboard da plataforma. A API mantém controllers finos; `ISaasService` é o contrato da Application e a implementação Dapper parametrizada está na Infrastructure.

## Regras operacionais

Organizações percorrem `IMPLEMENTING`, `ACTIVE`, `SUSPENDED`, `BLOCKED` e `CANCELLED`. Bloqueio exige motivo; suspensão e bloqueio impedem transações de escrita pela fronteira tenant. Documento é normalizado e único. Alterações de plano, estado, perfil, convite, sessão, dispositivo e configuração produzem eventos em `audit.saas_events`.

O super administrador usa `/api/platform`; contas comuns nunca informam `tenantId`, obtido do token e do `TenantContext`. A interface `/saas` reúne os quinze contextos exigidos, com loading, erro/tentativa, vazio, responsividade e seleções relacionais.
