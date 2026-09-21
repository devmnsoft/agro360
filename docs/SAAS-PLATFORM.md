# Plataforma SaaS Agro360 Enterprise

A arquitetura SaaS do Agro360 é estruturada em múltiplos círculos concêntricos de governança e segurança:
1. **Super Administração da Plataforma**: Escopo reservado à operadora do sistema com autorização `platform.admin`.
2. **Administração Interna do Tenant**: Operadores, agrônomos, fiscais e gestores com permissões granulares por módulo.
3. **Portal Externo B2B / Autoatendimento (`AG-PORTAL-EXT-001`)**: Superfície externa isolada para produtores, cooperados, clientes B2B, compradores, parceiros técnicos e auditores.

## Defesa em Profundidade e Segregação

- **Token de Acesso Restrito**: Usuários do portal externo recebem apenas a claim `portal.access` e claims específicas de perfil (`agro360.portal_profile.*`). Não é concedida nenhuma claim administrativa.
- **Contexto Multi-Tenant Obrigatório**: Todas as requisições autenticadas vinculam a sessão ao `tenant_id` corporativo, com execução no banco aplicando `set local "app.tenant_id" = ...`.
- **Row-Level Security (RLS)**: Todas as tabelas `portal_*` (26 tabelas) possuem RLS ativo via `agro360.platform_enable_tenant_rls(...)`, garantindo que dados de um tenant jamais vazem para outro, mesmo em caso de erro na camada aplicativa.
- **Auditoria Externa Imutável**: Ações do portal são persistidas em `portal_external_audit_events` com trilha UTC, IP e identificadores funcionais.

## Governança de Esquema e Migrations

- O ciclo de evolução do banco é regido por migrações incrementais estritamente idempotentes e compatíveis com rollback seguro.
- O schema consolidado `database/agro360-postgres-full.sql` e a migration incremental `097_portal_external_hardening.sql` registram a versão de plataforma `'9.7.0'`.
