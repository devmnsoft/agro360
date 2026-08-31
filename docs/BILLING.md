# Cobrança gerencial

A cobrança é gerencial: não existe pagamento online sem provedor configurado. O backend calcula `base + adicional - desconto`; o banco repete a regra em coluna gerada e impede valores negativos, desconto excessivo e duplicidade por tenant/plano/competência. Baixa externa requer data, observação, operador e evidência de auditoria. Cancelamento, renegociação, bloqueio e desbloqueio exigem motivo.

Uma integração futura deverá implementar contrato de provedor, idempotência, webhook assinado e conciliação. Até lá, nenhuma tela representa baixa manual como pagamento processado.
