# Atendimento, suporte e Customer Success — Sprint 33

A central `/support` reúne dashboard real, chamados, SLA, artigos, implantação, trilhas, feedback, backlog e comunicados. O acesso depende de `support.read`; alterações de atendimento usam `support.write`; políticas, publicação, backlog e implantação usam `support.manage`.

## Operação

1. Em **Chamados**, selecione **Abrir chamado**, informe assunto, descrição, categoria, módulo, prioridade e severidade.
2. Em **Detalhes**, a equipe consulta a timeline e move o chamado pela triagem. Cancelamento e reabertura exigem motivo; resolução exige resposta; atendimento exige responsável.
3. Comentários internos permanecem na tabela própria e não devem ser retornados pelo portal externo. Respostas ao cliente são eventos públicos autorizados.
4. Use **Exportar CSV** para extrair apenas chamados do tenant ativo.

SLA é congelado no momento da abertura pela política mais específica. Chamados críticos criam alerta operacional real. O job de varredura periódica de vencimentos ainda deve ser agendado no Worker; o dashboard já detecta violações em tempo real. Não há integração fictícia com GitHub nem mensageria externa. Canais sem provider devem seguir a outbox existente como `NOT_CONFIGURED`.

## Segurança e homologação

Teste usuário externo, administrador de tenant e suporte global separadamente. Confirme que códigos públicos não permitem acesso cruzado, comentários internos não chegam ao portal, lookups contêm somente pessoas ativas autorizadas e backlog nunca é exibido externamente. Queries usam parâmetros e o schema força RLS por `app.tenant_id`.
