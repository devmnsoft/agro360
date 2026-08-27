# Planos, assinaturas e cobrancas internas

Planos definem features e limites em `saas.plan_features` e `saas.plan_limits`. Plano inativo nao pode receber assinatura. Upgrade libera o novo conjunto; downgrade bloqueia novos registros excedentes, sem apagar dados.

A assinatura registra ciclo, vigencia, valor, desconto, responsavel e historico. Desconto e cancelamento exigem motivo; vigencia final deve ser posterior a inicial. Trial, vencimento, suspensao e cortesia sao estados explicitos.

Cobranças sao administrativas, por competencia, e nao representam gateway. O Agro360 **nao baixa automaticamente pagamentos**. A baixa manual requer permissao e data; cancelamento requer motivo; valor deve ser positivo; uma competencia nao cancelada por assinatura e unica. CSV deve refletir os filtros e nunca expor outro tenant. Integracao futura devera usar provider homologado, webhook autenticado e idempotencia.
