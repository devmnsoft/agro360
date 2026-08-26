# Sprint 16 — Integrações e interoperabilidade

A central `/Integrations` reúne Hub, chaves públicas, webhooks, CSV, fiscal, IoT, split manual, outbox e indicadores. Conectores sem referência segura permanecem `AWAITING_CONFIGURATION`. Segredos e tokens são armazenados somente como hash ou referência a cofre.

## Arquitetura
Controllers cuidam de HTTP e logging de fronteira; `IIntegrationService` define casos de uso; `IntegrationService` executa SQL Dapper parametrizado e transacional com contexto de tenant. PostgreSQL aplica RLS.

## Operação
Configure `ConnectionStrings__Agro360`, aplique o SQL único e inicie API/Web. Ativação, confirmação de importação, aprovação de split e reenvio exigem permissão `integrations.write`.
