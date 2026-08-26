# Sprint 13 — Inteligência Agro

A Sprint 13 transforma os registros transacionais em indicadores gerenciais, 23 relatórios, alertas auditáveis, previsões determinísticas, assistente orientado a dados e painéis personalizados. A API fica em `/api/intelligence`, exige JWT, permissão `intelligence.read` e, para mutações, `intelligence.write`.

## Arquitetura

Os contratos residem em Application; `IntelligenceService` implementa consultas Dapper parametrizadas e isoladas pelo tenant; o controller apenas traduz HTTP. Dashboard, previsão e assistente consultam o PostgreSQL no momento da requisição: não há resposta fixa nem dados de demonstração.

## UI

Acesse `/intelligence`. Período e propriedade são filtros validados; relações são selects alimentados por lookup, nunca caixas de UUID. A página contém Painel executivo, Relatórios, Central de alertas, Previsões, Assistente e Meus painéis, com loading, vazio e erro responsivos.
