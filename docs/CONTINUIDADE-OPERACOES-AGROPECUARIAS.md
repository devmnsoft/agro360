# Continuidade — jornada de operações agropecuárias

## Base verificada em 2026-09-16

A solução usa .NET SDK 10 (`net10.0`, C# `latest`), Razor Pages com JavaScript/CSS nativos, API ASP.NET Core, Application/Domain/Infrastructure, Dapper e PostgreSQL. O contexto de autorização é sempre o `tenant`; fazenda, talhão, safra, usuário, depósito e produto são referências existentes e não foram duplicados.

`MaterialConsumptionCommand` tem uma única definição canônica em `InventoryContracts.cs`. Ordens agrícolas usam o contrato específico `FieldMaterialEventCommand`; produção industrial mantém `ProductionMaterialConsumptionCommand`. `IFieldOperationsService`, `IAgriculture360Service` e `ISeasonTrackingService` possuem um registro de DI cada.

## Situação objetiva

| Funcionalidade | Situação encontrada | Lacuna corrigida nesta entrega | Validação |
|---|---|---|---|
| Planejamento | Ordem persistida em `agriculture_records`, seletores reais e materiais planejados separados do estoque | período planejado agora é validado; talhão e safra devem pertencer à fazenda; troca da fazenda limpa e filtra dependências | teste de arquitetura e inspeção tela → contrato → serviço → SQL |
| Execução | liberação, início, pausa e envio para revisão já centralizados no serviço | transições de ordem agora exigem versão, usam `UPDATE` otimista e gravam início real no servidor | teste de regressão estrutural |
| Materiais | reserva, entrega, consumo, devolução, perda e estorno persistidos em transação | depósito do material deve pertencer à fazenda da ordem e referência inválida não falha silenciosamente | teste de regressão estrutural |
| Conclusão | servidor recalcula bloqueios e persiste revisão única | conclusão grava término real no servidor; repetição ou versão antiga resulta em conflito sem duplicar revisão | índice existente + atualização otimista |
| Custos/histórico | custo realizado preserva custo unitário da linha; valor desconhecido permanece não apurado; eventos e estados são consultáveis | nenhuma metodologia contábil nova foi criada | inspeção dos agregados sem `join` multiplicador |

## Matriz de estados da ordem de campo

| Estado atual | Ação | Permissão/API | Pré-condições | Próximo estado |
|---|---|---|---|---|
| `OPEN`, `PLANNED`, `AWAITING_RESOURCES` | liberar | `agriculture.write` | fazenda, talhão, responsável, programação, dependências e versão atuais | `RELEASED` |
| `RELEASED` | iniciar | `agriculture.write` | versão atual | `IN_PROGRESS` |
| `IN_PROGRESS` | pausar | `agriculture.write` | motivo e versão atuais | `PAUSED` |
| `PAUSED` | retomar | `agriculture.write` | versão atual | `IN_PROGRESS` |
| `IN_PROGRESS`, `PAUSED` | revisar | `agriculture.write` | versão atual | `AWAITING_REVIEW` |
| `AWAITING_REVIEW` | concluir conferência | `agriculture.write` | versão atual, responsável, apontamento e custódia zerada | `COMPLETED` |
| qualquer não encerrado | cancelar | `agriculture.write` | justificativa e versão atuais | `CANCELLED` |

Cancelamento não estorna consumo. Movimentações permanecem no histórico e um estorno deve usar a ação explícita, autorizada e vinculada ao consumo original antes do encerramento.

## Limites e próximo incremento

O container desta execução não possui o SDK .NET nem PostgreSQL, portanto build, testes executáveis, aplicação das migrations e jornada em navegador não puderam ser homologados aqui. A experiência atual cobre ordens agrícolas; operações pecuárias continuam no agregado próprio e não devem ser forçadas a exigir talhão. O próximo incremento recomendado é criar a consulta tipada e paginada de “Minhas atividades/Todas”, com filtros e contadores no servidor, seguida de testes PostgreSQL e E2E da jornada completa.
