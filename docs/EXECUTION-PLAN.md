# Plano de execução

> Índice legado preservado. O plano canônico é [execucao/EXECUTION-PLAN.md](execucao/EXECUTION-PLAN.md), as evidências ficam em [EXECUTION-CHECKPOINT.md](EXECUTION-CHECKPOINT.md) e os estados em [TRACEABILITY-MATRIX-v0.2.0.md](TRACEABILITY-MATRIX-v0.2.0.md). Não atualize este arquivo como fonte concorrente.

## Concluído neste checkpoint

1. Estabilizar configuração PostgreSQL, tratamento de conexão e respostas 503.
2. Estabilizar login, refresh concorrente e experiência de erro.
3. Fechar governança SaaS de usuários, perfis, convites e planos.
4. Integrar pedido aprovado, recebimento, estoque físico e previsão financeira.

## Próximas entregas

1. Comercial → Reserva → Expedição → Recebível.
2. Produção → Consumo → Lote → Qualidade.
3. Agricultura/Pecuária → Custos.
4. Offline, idiomas e indicadores.

Cada etapa deve manter idempotência, concorrência, auditoria, tenant/RLS, permissões e atualização do instalador completo.
