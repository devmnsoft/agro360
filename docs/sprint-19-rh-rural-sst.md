# Sprint 19 — RH Rural e SST

O módulo operacional conecta pessoas e equipes a propriedades, recursos e atividades, sem pretender substituir a folha legal. Inclui pessoas, funções, equipes, jornada (inclusive chave offline idempotente), alocações, custos, treinamentos, EPIs, riscos, incidentes, ações corretivas, alojamento, transporte, exportação CSV e dashboard.

A API fica em `/api/rural-hr`, exige tenant no token e permissões `rural-hr.read`, `rural-hr.write` e `rural-hr.safety`. Toda persistência passa por `IRuralHrService`, Dapper parametrizado e transação com contexto do tenant. Vencimentos, incidentes abertos e ações atrasadas alimentam alertas do dashboard.
