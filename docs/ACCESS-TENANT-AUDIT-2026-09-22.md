# Auditoria de acesso e tenants — 2026-09-22

## Diagnóstico anterior à alteração

| Item obrigatório | Estado inicial | Evidência e diagnóstico |
|---|---|---|
| Tabelas de tenants | **pronto com evidência estática** | `tenancy_tenants` é o registro canônico e `platform_tenants` agrega documento, plano e bloqueio. |
| Usuários | **parcial** | `identity_users` era persistente e tenant-scoped, mas o seed de acesso continha somente SuperAdmin e um administrador Santa Clara com e-mail diferente do contrato desta entrega. |
| Perfis/roles | **parcial** | Roles, vínculos e permissões existiam; faltavam operador, Vale Verde e usuário do tenant bloqueado. |
| Permissões/ACL | **pronto com evidência estática** | As permissões são carregadas por role e filtradas pelos módulos contratados durante a emissão do token. |
| Módulos contratados | **parcial** | Entitlements e bloqueio no backend existiam, mas apenas Santa Clara era provisionada pelo seed de teste. |
| Seed atual | **quebrado para o aceite** | Não criava todos os tenants/usuários solicitados e usava `admin.cliente@agro360.local`. |
| Migrations | **parcial** | Há 69 migrations validadas estaticamente; instalação/upgrade não foi executado porque este ambiente não possui PostgreSQL. |
| SQL consolidado | **parcial** | Era autocontido, mas repetia o seed incompleto ao final. |
| Fluxo de login | **parcial** | Consultava banco por slug + e-mail/CPF/CNPJ, validava hash e tenant, mas não recusava explicitamente identidade sem role e escondia bloqueios sob erro genérico. |
| Hash de senha | **pronto com evidência** | `PasswordHasher` usa PBKDF2-HMAC-SHA512, 210.000 iterações, salt de 16 bytes e comparação em tempo constante. |
| Claims/cookies/sessão | **pronto com evidência estática** | O login emite access/refresh tokens com tenant, usuário, roles e permissões; refresh token é persistido somente como hash. |
| Layout/menu por tenant | **pronto com evidência estática** | O layout central oferece ajuda contextual e o token recebe apenas permissões de módulos ativos. |
| SuperAdmin | **parcial** | Elevação global persistente existia, porém a fixture dependia do tenant técnico legado e não havia o tenant natural solicitado. |
| Tenant comum | **parcial** | RLS e transações tenant-aware existiam; faltavam fixtures suficientes para demonstrar isolamento entre dois clientes. |
| RLS/policies | **pronto com evidência estática; não verificado dinamicamente** | O consolidado habilita/força RLS e o validador impede regressões, mas não há servidor PostgreSQL neste runner. |
| Testes existentes | **parcial** | Havia regressões de login e hash para duas senhas; não cobriam o inventário completo solicitado. |

## Causa raiz e correção

O problema não era um segundo mecanismo de autenticação: o login real já consumia
`identity_users`. A causa era **provisionamento incompleto e divergente**. O seed
publicava apenas duas identidades, com login Santa Clara diferente do solicitado,
não criava o tenant secundário/bloqueado nem o operador e não reconciliava módulos.

O seed agora localiza tenants por slug e documento, reaproveita a Santa Clara
legada (`santa-clara`) pelo CNPJ, reconcilia status/plano/módulos e cria cinco
identidades persistentes com roles. Os três hashes conhecidos são compatíveis com
o mesmo `PasswordHasher` da aplicação. O consolidado contém exatamente a mesma
rotina. Reaplicar não duplica tenants, usuários, roles, vínculos ou módulos.

## Contrato das fixtures

| Organização (slug) | Usuário | Perfil | Resultado esperado |
|---|---|---|---|
| `agro360-platform` | `superadmin@agro360.local` ou CPF `00000000000` | `SUPER_ADMIN` | acesso global |
| `fazenda-santa-clara` | `admin.santaclara@agro360.local` ou CNPJ `11222333000181` | `tenant-administrator` | somente Santa Clara |
| `fazenda-santa-clara` | `operador.santaclara@agro360.local` ou CPF `11122233344` | `operator` | permissões operacionais contratadas |
| `cooperativa-vale-verde` | `admin.valeverde@agro360.local` ou CNPJ `22333444000191` | `tenant-administrator` | somente Vale Verde |
| `fazenda-bloqueada-teste` | `admin.bloqueado@agro360.local` ou CNPJ `33444555000172` | `tenant-administrator` | recusado antes de validar sessão |

As senhas conhecidas continuam somente no contrato de homologação; o SQL contém
exclusivamente hashes. A interface distingue senha inválida, usuário bloqueado e
tenant bloqueado. Identidades sem role são recusadas antes da emissão de tokens.

## Limites da homologação neste runner

Os validadores estáticos do banco e `git diff --check` passaram. O SDK .NET,
`psql`, PostgreSQL e navegador não estão instalados, portanto build, testes,
instalação limpa/reaplicação e os sete logins pela tela real permanecem **não
verificados dinamicamente**. Não se declara homologação manual sem essas provas.

Próximo passo: executar o consolidado duas vezes em PostgreSQL descartável, iniciar
API e Web e automatizar os sete cenários pela interface, incluindo inspeção das
claims e consultas cruzadas entre Santa Clara e Vale Verde.
