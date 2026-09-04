# Manual do usuário — Agro360

## O que é

O MNSOFT Agro360 é uma plataforma SaaS B2B para integrar cadastros, operação rural, pecuária, agricultura, estoque, financeiro, documentos, conformidade e indicadores. Dados e permissões são isolados por **cliente (tenant)**; o backend é a fonte final das regras de negócio.

## Acesso e sessão

1. Abra a aplicação Web e informe o identificador da organização, o e-mail e a senha cadastrados.
2. O login usa um usuário real do banco. Em caso de sessão expirada, entre novamente; nunca envie senha ou token ao suporte.
3. O super administrador gerencia tenants, planos e governança global. O administrador do cliente gerencia apenas usuários, perfis e dados de seu tenant.

### Cliente interno de homologação

Ao executar `database/agro360-postgres-full.sql`, o ambiente local recebe o tenant **Fazenda Santa Clara** (`santa-clara`), no plano Profissional, e o usuário `admin@santaclara.agro360.local`. A senha inicial de desenvolvimento é `SantaClara@2026!` e deve ser alterada no primeiro acesso. O super administrador usa o tenant `agro360-platform`, o e-mail `superadmin@mnsoft.com.br` e a senha inicial `MNSoft@Agro360#2026`. Essas contas são destinadas apenas à homologação local; nunca copie as credenciais ou documentos fictícios para produção.

## Usuários, perfis e permissões

O administrador cria o usuário, associa perfis e concede somente as permissões necessárias. Permissões de leitura e escrita são independentes. Uma opção ausente ou uma resposta `403` indica falta de autorização; solicite revisão ao administrador, sem compartilhar credenciais.

## Navegação e telas principais

O menu segue o fluxo: Dashboard; Administração SaaS; Clientes; Usuários e Perfis; Fazendas; Agricultura; Pecuária; Estoque; Compras; Produção; Comercial; Financeiro; Logística; Qualidade; Documentos; Relatórios; Configurações; Ajuda. A Administração SaaS é exclusiva do super administrador; os demais itens aparecem somente quando ao menos uma permissão de leitura compatível foi emitida no login. Plano e feature flag continuam sendo validados pela API ao abrir uma URL diretamente.

- **Command Center:** visão consolidada de indicadores, alertas e atividade recente.
- **Administração SaaS:** clientes, limites, planos e estado do tenant, exclusiva para perfis autorizados.
- **Usuários e perfis:** acesso, responsabilidades e permissões do cliente.
- **Agricultura:** propriedades, talhões, safras e operações de campo.
- **Pecuária 360:** animais, lotes, pastagens, manejo, sanidade, reprodução, nutrição e produção.
- **Estoque, compras e financeiro:** movimentações, custos, títulos e resultados ligados à operação.
- **Documentos e compliance:** evidências, alertas, aprovações e trilha auditável.

Cada tela possui a seção recolhível **Como usar esta tela**. Campos com `*` são obrigatórios; o botão `?` explica formato e regra quando há ajuda contextual. Use seletores pesquisáveis em vez de informar GUIDs.

## Dashboards e alertas

Dashboards mostram somente dados autorizados do tenant. “Sem dados” e valores zero são estados válidos, não dados simulados. Confirme filtros e período antes de interpretar um indicador. Alertas informativos orientam; alertas críticos exigem revisão. Atualizações podem ocorrer após a persistência e auditoria da operação.

## Salvar, aprovar e cancelar

Revise os campos antes de salvar. O backend valida, persiste, audita, registra o evento e então atualiza indicadores. Aprovação pode tornar o registro efetivo. Cancelar, excluir, bloquear, reprovar, encerrar ou revogar acesso exige confirmação e, quando aplicável, justificativa. Mensagens de sucesso, alerta e erro aparecem como toast ou modal.

## Suporte técnico

Use **Atendimento e Suporte** no menu. Informe tela, horário, ação realizada e o código de suporte (`traceId`) mostrado no erro. Não envie senha, token, hash, documento sensível ou captura com dados pessoais sem orientação do responsável por privacidade.
