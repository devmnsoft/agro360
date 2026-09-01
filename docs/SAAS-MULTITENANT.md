# SaaS multiempresa

O Agro360 isola os dados operacionais por `tenant_id`. A API abre transações com o contexto do tenant e as tabelas multiempresa usam RLS; apenas operações globais autorizadas do Super Administrador podem atravessar esse limite, sempre com auditoria.

## Administração

- O administrador do cliente cria usuários, perfis e permissões somente no próprio tenant e não pode conceder `SUPER_ADMIN`.
- Usuários e tenants bloqueados não acessam operações. Inadimplência pode bloquear funcionalidades conforme o plano sem apagar dados.
- Planos, módulos, limites, cobranças, convites, preferências e eventos de auditoria são persistidos; integrações externas não configuradas permanecem explicitamente pendentes.
- Identificadores de CPF/CNPJ são normalizados sem máscara. Mensagens de autenticação não confirmam a existência de uma conta.

## Idioma e experiência

`pt-BR` é o fallback, com `en-US` e `es-ES` cadastrados. A preferência pertence ao usuário. Telas principais expõem ajuda contextual curta e localizada.

## Homologação

Crie dois tenants e valide com usuários distintos que listagens, buscas, relatórios e mutações nunca retornam dados cruzados. Depois valide bloqueio e desbloqueio, troca obrigatória de senha, permissões por perfil e acesso global auditado do Super Administrador.
