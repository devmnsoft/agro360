# Motor de alertas

O schema `intelligence` mantém regras por tenant, cooldown, severidade, evidência e fingerprint único para impedir duplicidade aberta. São cadastrados 18 tipos: estoque baixo; vencimento de produto; atividade e clima; lotação, carência e vacina; manutenção; contas vencidas; romaneio; conformidade; ledger; expedição; rota; split; e sincronização mobile.

Alertas aceitam `resolve`, `snooze` e `ignore`. Toda ação exige usuário autenticado, registra data, motivo e uma linha imutável em `alert_audit`. Adiamento exige data futura.
