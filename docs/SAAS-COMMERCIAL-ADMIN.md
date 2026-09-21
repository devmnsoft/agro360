# Administração comercial SaaS — AG-SaaS-COM-001

## Escopo e autoridade

A área `/Saas` é a superfície canônica de administração. Rotas globais usam `api/platform` e exigem a policy `platform.admin`; ocultação de menu não é autorização. O tenant vem do contexto autenticado e ações globais executam pela conexão de sistema, sempre persistindo ator, tenant afetado, motivo e detalhes seguros. O catálogo de planos deixou de ser anônimo.

Ao atuar em cliente, o SuperAdmin deve selecionar explicitamente o contexto e manter uma faixa visual permanente. Esse fluxo de suporte elevado/MFA ainda não está concluído; não se deve simular seleção apenas no navegador.

## Operação comercial

* Tenant: `IMPLEMENTING`, `ACTIVE`, `SUSPENDED`, `BLOCKED`, `DELINQUENT` ou `CANCELLED`. Toda transição exige motivo; bloqueio preserva leitura e histórico e deve impedir comandos novos.
* Usuário: inativação/reativação exige motivo, protege o próprio usuário e o último administrador e revoga sessões.
* Módulo: solicitação, contratação, ativação, bloqueio, suspensão e cancelamento são fatos distintos. Dependências são validadas no backend e eventos são imutáveis.
* Cobrança: registro exclusivamente gerencial da MNSOFT; não cria contas no financeiro rural, documento fiscal, boleto, PIX ou cartão.
* Auditoria: consultar `saas_admin_audit_events`, eventos de status, módulo e cobrança. Payloads não podem conter segredo.

## Diagnóstico verificado estaticamente em 2026-09-21

| Capacidade | Classificação | Evidência/limite |
|---|---|---|
| Tenants e usuários | Implementado sem validação runtime | serviço Dapper, endpoints autorizados e UI existentes; SDK/DB indisponíveis |
| Perfis e permissões | Implementado sem validação runtime | policies no backend e administração tenant existentes |
| SuperAdmin global | Parcial | lista/dashboard existem; elevação assistida e faixa de contexto ainda pendentes |
| Catálogo de módulos | Parcial | catálogo comercial e dependências persistidos em 097; endpoints de manutenção pendentes |
| Planos comerciais | Parcial | planos/limites existentes; associação normalizada plano-módulo adicionada, sem editor completo |
| Solicitações | Parcial | upgrade e marketplace existem; consolidação em jornada única pendente |
| Bloqueios tenant/usuário | Implementado sem validação runtime | regras e auditoria existem; E2E não executado |
| Bloqueio de módulo | Parcial | modelo/RLS/eventos e regra de dependência existem; enforcement transversal ainda deve ser conectado a todos os comandos |
| Cobrança gerencial | Parcial | CRUD/status, decomposição monetária e evidência manual; UI de criação/baixa pendente |
| Auditoria | Implementado sem validação runtime | múltiplos eventos canônicos; runtime indisponível |
| Onboarding | Parcial | templates/progresso persistidos; UI atual ainda projeta parte pelo cliente |
| White label | Implementado sem validação runtime | schema e regras existentes |
| Marketplace/ecossistema | Implementado sem validação runtime | controller/service/UI/migrations existentes |

Esta classificação não é homologação. Instalação limpa, upgrade, isolamento real com dois tenants e navegação nos quatro breakpoints continuam obrigatórios.
