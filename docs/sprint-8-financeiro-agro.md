# Sprint 8 — Financeiro Agro

A versão 0.5.0 entrega plano de contas e centros de custo multitenant, contas a pagar/receber com baixas parciais e totais, comercialização com reserva/baixa de estoque e faturamento parcelado, lançamentos manuais, fluxo de caixa, resultado econômico e dashboard.

## Regras principais

Códigos de contas e centros são únicos no tenant. Valores negativos, sobrebaixa e cancelamento sem motivo são rejeitados. A situação vencida é calculada na consulta. Confirmação de venda reserva estoque; faturamento baixa a reserva, atualiza animal quando a origem é `ANIMAL` e cria recebíveis. Toda mutação usa transação, tenant e auditoria.

## Uso

Os endpoints autenticados ficam em `/api/finance/*` e `/api/commercial/sales`. Use claims `finance.read`, `finance.write` e `commercial.write`. O dashboard é `GET /api/finance/dashboard`; fluxo e resultados aceitam `from`, `to` e dimensões como query string.

O banco completo é instalado com `psql "$ConnectionStrings__Agro360" -f database/agro360-postgres-full.sql`. Nenhum Docker é necessário.
