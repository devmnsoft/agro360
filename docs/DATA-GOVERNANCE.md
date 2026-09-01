# Governança de Dados — Sprint 48

A camada `governance` centraliza lotes de importação, achados de qualidade, exportações gerenciais, LGPD, sessões, auditoria e telemetria de consultas. Todas as operações da API usam o `ITenantContext`, transação Dapper parametrizada e RLS; a super administração deve trocar o contexto explicitamente e registrar finalidade.

## Qualidade e auditoria

Regras produzem achados, nunca correções silenciosas. Um achado só muda para analisado, corrigido ou ignorado com justificativa. Eventos críticos preservam usuário, tenant, módulo, entidade, correlação, origem e antes/depois sanitizados. Auditoria é append-only para usuários comuns e nunca recebe senha, token, segredo ou documento completo.

## Operação

A página `/governance` oferece dashboard, importação, qualidade, backup, LGPD, auditoria, segurança, performance, governança global e área “Meus dados”. Cada área tem ajuda contextual e conteúdo adequado ao perfil.
