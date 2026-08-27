# Feature flags e limites

A resolucao segue a precedencia: **bloqueio administrativo > override vigente > plano**. Origens aceitas: plano, override manual, trial, beta e bloqueio administrativo. Override exige motivo, validade e auditoria. Beta deve receber badge visual.

Menu e rota devem consultar o mesmo servico de entitlement; ocultar o menu nao substitui a autorizacao backend. As metricas sao tenant-scoped. Com 80% o produto alerta; com 100% bloqueia somente a criacao configurada. Overrides expirados deixam de compor o limite efetivo automaticamente.
