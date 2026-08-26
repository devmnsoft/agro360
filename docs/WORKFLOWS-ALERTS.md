# Workflows, alertas e trabalho operacional — Sprint 24

A central **Operação 360** une tarefas persistentes, alertas determinísticos, aprovações segregadas, notificações por usuário, agenda e monitoramento da outbox. Todas as consultas recebem o tenant do token e as tabelas usam RLS; vínculos de usuário são selecionados pela busca de usuários ativos.

## Uso

1. Abra `/Work`, filtre a lista ou crie uma tarefa escolhendo uma pessoa ativa. Conclusão exige descrição e cancelamento exige motivo.
2. Em **Alertas**, leia ou resolva ocorrências. `POST /api/rules/evaluate` avalia estoque mínimo, contas a pagar próximas, recebíveis vencidos e tarefas críticas vencidas com chave de deduplicação.
3. Em **Regras**, mantenha nome, módulo, tipo, condição JSON, severidade, ação e estado. Regras inativas não são registradas como executadas.
4. Em **Aprovações**, o aprovador atribuído decide. Reprovação/cancelamento exige motivo e uma definição segregada bloqueia autoaprovação.
5. Em **Notificações**, itens são pessoais; é possível ler um ou todos. Links são rotas internas controladas.
6. **Outbox** nunca representa entrega inexistente: sem provider, a mensagem permanece `PENDING`/`NOT_CONFIGURED`, com tentativas e erro preservados.
7. **Agenda** agrega eventos persistidos em uma janela diária, semanal ou mensal pelos parâmetros `from` e `to`.

## Regras integradas e extensibilidade

A função `operations.evaluate_operational_rules` executa integrações existentes de estoque e financeiro, além do SLA crítico de tarefas. Os demais módulos publicam eventos por `operational_tasks`, `operational_alerts`, `workflow_instances`, `notifications` e `calendar_events`; novos avaliadores devem usar `dedup_key`, registrar `operational_rule_executions` e nunca engolir falhas. Canais externos só podem passar a `SENT` após confirmação real de um provider.

## Homologação

Execute o SQL completo em PostgreSQL externo, inicie API e Web sem Docker, autentique-se, crie/conclua/cancele tarefas, avalie regras duas vezes (a segunda não duplica alertas), teste segregação de aprovação, leitura de notificações, filtros da agenda e confirme que uma outbox sem provider não fica `SENT`.
