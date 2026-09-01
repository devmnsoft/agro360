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

### Sprint corretiva — schema PostgreSQL canônico
O banco consolidado usa somente `agro360`; nomes de tabela carregam o prefixo do módulo e SQL estático/Dapper deve ser qualificado. A homologação executa restore/build/test, instala o SQL com `ON_ERROR_STOP=1`, confirma a ausência dos schemas legados e testa isolamento: Super Admin enxerga todos os tenants; administradores e usuários permanecem limitados ao tenant ativo. Bloqueios de usuário/cliente, alterações de plano/módulo, cobrança e acesso global exigem confirmação, mensagem clara e auditoria. Login aceita e normaliza e-mail, CPF ou CNPJ; formulários usam seletores em vez de GUIDs e oferecem ajuda contextual em pt-BR, en-US e es-ES.

O bootstrap seguro recebe `AGRO360_SUPERADMIN_INITIAL_PASSWORD`, aplica o hash real da aplicação e marca troca obrigatória. Dados canônicos: Super Administrador MNSOFT; `superadmin@mnsoft.com.br`; CNPJ `18.160.057/0001-13`; perfil `SUPER_ADMIN`; status Ativo. Não existe senha padrão em produção.
