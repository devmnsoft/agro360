# Manual do usuário — Agro360

## O que é

O MNSOFT Agro360 é uma plataforma SaaS B2B para integrar cadastros, operação rural, pecuária, agricultura, estoque, financeiro, documentos, conformidade e indicadores. Dados e permissões são isolados por **cliente (tenant)**; o backend é a fonte final das regras de negócio.

## Acesso e sessão

1. Abra a aplicação Web e informe o identificador da organização, o e-mail e a senha cadastrados.
2. O login usa um usuário real do banco. Em caso de sessão expirada, entre novamente; nunca envie senha ou token ao suporte.
3. O super administrador gerencia tenants, planos e governança global. O administrador do cliente gerencia apenas usuários, perfis e dados de seu tenant.

### Cliente interno de homologação

Ao executar `database/agro360-postgres-full.sql`, o ambiente local recebe as fixtures sem credencial utilizável: **Fazenda Santa Clara** (`santa-clara`, `admin@santaclara.agro360.local`) e plataforma (`agro360-platform`, `superadmin@mnsoft.com.br`). A operação técnica deve ativá-las com `scripts/provision-homologation.sh`; as senhas temporárias são entradas protegidas locais e devem ser trocadas no primeiro acesso.

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
# Centrais guiadas

## Central de Implantação

Acesse **Visão geral → Central de Implantação**. O percentual combina usuários, perfis, módulos contratados, fazendas e checklist obrigatório. Pendências e próximas ações são links funcionais para o cadastro correspondente; ao concluir um cadastro, use **Atualizar painel**.

## Central de Tarefas e Alertas

Acesse **Operação → Tarefas e Alertas**. Os cartões e listas vêm das tarefas, regras, alertas, estoque, aprovações e notificações do cliente autenticado. Use **Avaliar regras** para gerar alertas a partir das condições configuradas; não são criados alertas demonstrativos.

## Comercial Agro 360 (sprint atual)

Consulte `docs/COMMERCIAL-AGRO.md` para fluxo, regras implementadas, modelo persistente e pendências reais de integração.

## Acesso e navegação recuperados

1. Informe o slug do cliente, a identificação e a senha; o botão bloqueia durante a validação para impedir envio duplo.
2. Após o acesso, o menu exibe somente itens autorizados e destaca a tela atual.
3. Em qualquer módulo, abra **Como usar esta tela** para conferir finalidade, campos, ações e efeitos da gravação.
4. Use **Testar conexão com a API** no acesso para validar API, banco e Swagger no ambiente de desenvolvimento.

## Materiais da ordem e conferência operacional

**Finalidade.** Acompanhar o material desde a previsão da operação até a prestação de contas da equipe, sem confundir planejamento, reserva e movimentação física.

**Pré-requisitos.** Selecione o cliente e a unidade autorizados, mantenha produto, unidade, depósito e lotes cadastrados, e gere a ordem a partir de uma operação da safra. O usuário precisa das permissões do módulo para cada ação.

**Como usar.** Na ordem, consulte **Planejamento** e **Materiais**; crie a requisição; aprove quando exigido; reserve o saldo; confirme a entrega e seus lotes; depois registre consumo, devolução vinculada à entrega ou perda com motivo e responsável. Cada confirmação informa registro, quantidade e consequência. Em **Conferência**, resolva primeiro ocorrências classificadas como bloqueio, avalie advertências e informações e então conclua usando a versão mais recente da ordem.

**Leitura dos saldos.** Previsto não representa estoque reservado. Solicitado e aprovado documentam demanda/autorização. Reservado compromete disponibilidade, mas não baixa o físico. Entregue transfere a responsabilidade para a equipe e efetiva a saída. O saldo em custódia é `entregue - consumido - devolvido - perdido`; a necessidade não atendida é `máximo(0, previsto - entregue)`. Custos desconhecidos permanecem pendentes, nunca são mostrados como zero.

**Resultado esperado.** Materiais sem saldo em custódia e apontamentos obrigatórios registrados deixam de bloquear a conclusão. Reenvios com a mesma chave não duplicam movimentos, devoluções não excedem o saldo da entrega e ordens concluídas não podem ser concluídas novamente sem uma transição auditada.
