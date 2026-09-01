# Super Administração MNSOFT

Há no máximo um super administrador global ativo, garantido por índice parcial. Ele não é criado por telas do tenant nem removido fisicamente. Mudanças de plano, tenant, módulo, cobrança, bloqueio e acesso de suporte exigem motivo e evento global. O acesso de suporte registra tenant, administrador, motivo, escopo, início e término; logs guardam apenas detalhes seguros e correlation ID.

### Sprint corretiva — schema PostgreSQL canônico
O banco consolidado usa somente `agro360`; nomes de tabela carregam o prefixo do módulo e SQL estático/Dapper deve ser qualificado. A homologação executa restore/build/test, instala o SQL com `ON_ERROR_STOP=1`, confirma a ausência dos schemas legados e testa isolamento: Super Admin enxerga todos os tenants; administradores e usuários permanecem limitados ao tenant ativo. Bloqueios de usuário/cliente, alterações de plano/módulo, cobrança e acesso global exigem confirmação, mensagem clara e auditoria. Login aceita e normaliza e-mail, CPF ou CNPJ; formulários usam seletores em vez de GUIDs e oferecem ajuda contextual em pt-BR, en-US e es-ES.

O bootstrap seguro recebe `AGRO360_SUPERADMIN_INITIAL_PASSWORD`, aplica o hash real da aplicação e marca troca obrigatória. Dados canônicos: Super Administrador MNSOFT; `superadmin@mnsoft.com.br`; CNPJ `18.160.057/0001-13`; perfil `SUPER_ADMIN`; status Ativo. Não existe senha padrão em produção.
