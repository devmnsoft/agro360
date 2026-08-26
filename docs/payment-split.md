# Split de pagamentos

O provider `INTERNAL_MANUAL` calcula participantes e valores, exigindo percentuais totalizando 100%. A aprovação é explícita e auditada em `/api/payments/splits/{id}/approve`; baixa e conciliação são manuais. Nenhum pagamento externo ocorre sem provider e credencial reais.
