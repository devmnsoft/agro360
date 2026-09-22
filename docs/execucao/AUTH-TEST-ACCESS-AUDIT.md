# Auditoria de acesso e seed de teste — 2026-09-22

## Diagnóstico inicial

| Item auditado | Estado inicial | Evidência |
|---|---|---|
| Fluxo de login | Parcial | `IdentityService.LoginAsync` consultava PostgreSQL e hash real, mas aceitava somente CPF de 11 dígitos e exigia MFA até para a fixture solicitada. |
| Usuários, tenants, perfis, roles e permissões | Pronto com evidência | Tabelas `tenancy_tenants`, `identity_users`, `identity_roles`, vínculos e permissões existem no instalador consolidado e têm RLS. |
| Hash/validação de senha | Pronto com evidência | `PasswordHasher` usa PBKDF2-HMAC-SHA512, salt de 16 bytes, 210.000 iterações e comparação em tempo constante. |
| E-mail/CPF/CNPJ | Parcial | E-mail normalizado e CPF mascarado funcionavam; CNPJ era rejeitado explicitamente. |
| Cookies/sessão/claims | Parcial | A aplicação usa JWT + refresh token persistido; não usa cookie MVC. Claims de tenant, usuário, roles e permissões são emitidas pelo `TokenService`. |
| Autorização | Pronto com evidência | Policies por permissão e autoridade global consultam `platform_super_admins`; RLS mantém o escopo do tenant. |
| Seed de usuários | Quebrado para o aceite | Fixtures consolidadas eram `INVITED`/`unprovisioned`; o seed dev tinha outra credencial e troca obrigatória. |
| Scripts em `database` e consolidado | Parcial | Instalador idempotente e validadores já existiam, mas não havia o seed de acesso exigido. |
| Migrations | Pronto com evidência | Migrações incrementais e controle de versão existem; esta entrega não altera schema. |
| Services/controllers/views de autenticação | Parcial | API, serviço e modal real existiam; textos e validação frontend indicavam somente e-mail/CPF. |
| Layout/menu/telas principais | Parcial | Layout responsivo, breadcrumbs, filtros e ajuda global existiam; o título solicitado não era uniforme. |
| Testes | Parcial | Havia testes arquiteturais de autenticação, isolamento e experiência, mas nenhum comprovava que os hashes das credenciais exigidas eram aceitos pelo hasher real. |

## Correções desta etapa

- Seed idempotente persistente para SuperAdmin e Administrador Santa Clara, com atualização de status, vínculo, perfil e hash.
- Hashes estáticos compatíveis com o `PasswordHasher`, sem senha em texto puro no SQL.
- Login por e-mail, CPF ou CNPJ, com remoção de máscara e comparação de e-mail sem distinção de caixa.
- Bloqueio por estado operacional do tenant e pelo estado SaaS (`SUSPENDED`, `BLOCKED`, `DELINQUENT`, `CANCELLED` ou `CLOSED`).
- Módulos Enterprise explicitamente contratados para o tenant de demonstração.
- Título acessível e uniforme “Como funciona esta tela”, mantendo o conteúdo contextual específico ou o fallback global.

## Limites e riscos conhecidos

- O login continua exigindo o slug da organização (`agro360-platform` ou `santa-clara`) para evitar busca ambígua e preservar o isolamento entre tenants.
- A fixture global de teste não exige MFA; ela é destinada exclusivamente a Development/homologação e não deve ser aplicada em produção.
- Não havia SDK .NET nem PostgreSQL cliente/servidor disponíveis no ambiente desta execução; build, testes executáveis e login HTTP/banco limpo permanecem para validação em ambiente preparado.
- A aplicação já contém extensa evolução funcional, porém esta entrega não declara como concluídos todos os fluxos de negócio listados no pedido; o foco verificável foi acesso real, isolamento, módulos e ajuda contextual.
