# LGPD e Segurança

Solicitações exigem tipo, titular, documento válido, base legal, finalidade e trilha de eventos. Estados aceitos: aberta, em análise, aguardando validação, atendida, recusada e cancelada; recusa exige motivo. Anonimização somente pode alcançar atributos permitidos, preservando chaves e documentos fiscais/contábeis/auditoria. Documentos são mascarados em logs e telas comuns.

Sessões persistem apenas hash do refresh token, expiração, revogação e metadados de acesso. A política por tenant define complexidade, tentativas e bloqueio. Tenant/usuário bloqueado, módulo inativo ou permissão ausente devem ser negados no middleware/policy, inclusive em acesso manual à rota. Nunca registrar senha, token, connection string ou payload sensível.
