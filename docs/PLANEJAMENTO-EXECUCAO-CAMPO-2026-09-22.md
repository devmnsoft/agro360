# Planejamento e execução de campo — auditoria incremental

**Base auditada:** branch `work`, commit inicial `2ea18b9`, em 22/09/2026.

## Reconciliação do escopo

O incremento preserva os agregados canônicos. O plano e a ordem continuam em
`agriculture_records`; operações, dependências e versões usam as estruturas da
migration 086; execução e custódia usam a migration 085; saldo permanece sob
`inventory_apply_stock_movement`; custos realizados continuam nas apropriações
de safra. Não foi criado outro calendário, cadastro de pessoas, saldo ou razão de
custos.

O modelo atual admite diversos planos/operações para um mesmo talhão e período;
isso foi mantido para não proibir consórcio. `planned_area_ha` é a base física e
os apontamentos acumulam `physical_area_ha`/quantidade tratada sem alterar essa
base. Estimativas informadas pelo usuário não são recomendações agronômicas.

## Matriz de capacidade e evidência

| Capacidade | Estado | Evidência/restrição observada |
|---|---|---|
| Fazendas, talhões e safras | Funcional | FKs compostas por tenant e lookups nominais; tela filtra talhões pela propriedade. |
| Plano, operações e reprogramação | Funcional | `agriculture_plan_operations`, revisões versionadas e impacto de dependentes. |
| Modelos reutilizáveis | Parcial | O plano aprovado guarda JSON/versionamento, mas não existe catálogo dedicado de modelos de operação. |
| Dependências | Funcional | Busca recursiva impede ciclo; dependências obrigatórias bloqueiam liberação. |
| Ordens e transições | Funcional | Estados abertos, liberação, execução, pausa, conferência, conclusão e cancelamento são validados no serviço. |
| Equipes e máquinas | Parcial | Pessoas/equipamentos e sobreposição exclusiva estão integrados; capacidade compartilhável ainda não possui cadastro canônico. |
| Apontamento parcial | Funcional | Período, operador, equipamento, área, parada, evidência e idempotência persistidos. |
| Horímetro | Funcional com pendência | Monotonicidade histórica e pares de leitura protegidos; correção formal versionada permanece no backlog. |
| Insumos, custódia e estoque | Funcional | Reserva, entrega, consumo, devolução, perda e estorno são fatos separados e movimentos usam o mecanismo canônico. |
| Compras | Funcional | Requisição/recebimento alimentam o estoque existente; não há compra automática inventada pela ordem. |
| Custos | Parcial | Material com custo conhecido é consolidado e custo ausente vira pendência; tarifa horária versionada de equipe/máquina ainda ausente. |
| Ocorrências | Parcial | Registros e dashboard existentes; fluxo específico de desbloqueio com SLA/evidência ainda não está completo. |
| Painel previsto x realizado | Funcional | Serviço de acompanhamento usa apontamentos, materiais, custos e colheita reais e expõe pendências. |
| Interface operacional/mobile | Funcional | Tela responsiva existente usa lookups nominais, detalhe, ações de material e conferência; não declara suporte offline. |
| RLS e autorização | Funcional | Transações recebem tenant do contexto autenticado, queries incluem tenant e tabelas novas usam RLS forçado. |

## Correções deste incremento

Reenvio de apontamento agora compara todo o payload: mesma chave e mesmo conteúdo
é idempotente; conteúdo diferente retorna conflito. Operador e equipamento são
revalidados no servidor. Parada não pode exceder a duração e exige motivo; leituras
devem vir em par e não podem regredir frente ao histórico confirmado. A unidade do
material deve ser exatamente a unidade-base do produto; conversão implícita de
massa, volume ou qualquer outra dimensão é rejeitada.

As novas checks usam `NOT VALID`: protegem inclusões e alterações sem impedir a
atualização de uma base que contenha legado a reconciliar. Depois de corrigir
eventuais registros antigos, a operação pode executar `VALIDATE CONSTRAINT`.

## Atualização, recuperação e backlog

1. Efetue backup conforme `docs/BACKUP-RESTORE.md`.
2. Execute `database/migrations/101_field_execution_integrity.sql` com
   `ON_ERROR_STOP=1`; a transação inteira reverte em caso de erro.
3. Publique API e Web da mesma revisão e execute a jornada operacional.

Backlog explícito: catálogo/versionamento de modelos; capacidade de recurso
compartilhável; tarifas horárias versionadas e apropriação de mão de obra/máquina;
correção auditada de horímetro; ocorrências impeditivas com liberação formal;
testes PostgreSQL/browser em infraestrutura descartável. Esses itens não devem ser
interpretados como concluídos apenas por build verde.
