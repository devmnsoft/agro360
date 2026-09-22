# Consolidação funcional e Central Operacional — 2026-09-22

## Escopo auditado

Branch de trabalho: `work`. Base inicial: `5e434a4`. A classificação abaixo exige símbolo e persistência encontrados no código; um build aprovado não foi considerado homologação funcional.

| Jornada | Tela | endpoint / serviço | persistência e autorização | teste/evidência | antes → depois |
|---|---|---|---|---|---|
| Compra aprovada → recebimento parcial → inspeção → estoque | `/Procurement` | `ProcurementController`, `ProcurementService`; projeções `PURCHASE`, `RECEIPT_DIVERGENCE` e `QUALITY` em `WorkManagementService.OperationCenterAsync` | pedidos, itens, recebimentos, divergências e movimentos; `purchasing.read/approve`, `inventory.read` | migrations 064, 067, 080, 084; testes Sprint37 | funcional com evidência; não reexecutada ponta a ponta neste ciclo |
| Ordem de campo → apontamento → consumo → custo → conclusão/cancelamento | `/Agriculture` | `Agriculture360Controller`, `FieldOperationsService` | `agriculture_records`, logs, materiais/eventos e histórico; `agriculture.read/write` | migrations 085, 087, 101; testes Sprint43 | **parcial → integrada à central** para estados liberada, execução, pausa e conferência; conclusão continua no módulo de origem |
| Pedido → reserva → separação → expedição → entrega/retorno | `/Commercial`, `/Logistics` | serviços Commercial/Fulfillment/Logistics; projeção `SHIPMENT` | pedidos, reservas, remessas, entregas e retornos; permissões de logística | migrations 071, 088, 089, 094; testes de logística | funcional com evidência; não reexecutada ponta a ponta neste ciclo |
| Inspeção → bloqueio → investigação → liberação/destinação | `/Compliance`, `/Procurement` | `ComplianceService`, `InspectionService`; projeções `QUALITY` e nova `QUALITY_CASE` | não conformidades, ações, restrições e decisões; `compliance.read/write` | migrations 090–093; testes de compliance | **parcial → integrada à central** para casos abertos, prazo real, responsável e bloqueio ativo |
| Central operacional | `/Work` | `WorkManagementController`, `WorkManagementService.OperationCenterAsync` | ocorrências derivadas; somente leitura/atribuição são persistidas e auditadas; `work.read/write` mais permissão da origem | `Sprint24WorkManagementTests` | funcional ampliada; mutações agora recusam chave órfã ou condição já resolvida |

## Contratos e regras confirmados

- A ocorrência é derivada da condição de origem. Visualizar e atribuir não resolvem a obrigação.
- Atribuição não concede permissão: destinatários elegíveis precisam da permissão do módulo e pertencer ao tenant.
- Ausência de data é apresentada como “Sem prazo definido”; nenhuma política de SLA foi inventada.
- Estado de leitura é individual; atribuição é compartilhada, versionada e possui evento de auditoria.
- Antes de gravar leitura/atribuição, a condição operacional é revalidada dentro da mesma transação. Chaves arbitrárias, registros de outro tenant e condições já encerradas são tratados como inexistentes.

## Atualização e recuperação

Não há migration neste incremento. Publique API e Web a partir do mesmo commit após `dotnet restore`, build Release e testes. Em rollback, reverta o commit da aplicação; as tabelas e o histórico de interação existentes permanecem compatíveis e não requerem alteração de dados.

## Limites da validação e backlog priorizado

1. **P0 — não executado neste ambiente:** integração PostgreSQL descartável com role da aplicação, instalação limpa, reaplicação dos scripts e isolamento de dois tenants para anexos, exportações, jobs e conexão do pool.
2. **P0 — não verificado:** navegador autenticado cobrindo as quatro jornadas de ponta a ponta e persistência após recarga; a central foi validada por contratos/código, não homologada integralmente.
3. **P1:** criar consulta administrativa tenant-scoped para reservas órfãs, movimentos duplicados, quantidades não conciliadas e estados incompatíveis, com plano de reparo auditável — sem sobrescrever saldos.
4. **P1:** representar “em tratamento” a partir de evento de negócio explícito; hoje a central diferencia nova, visualizada e atribuída sem fingir que atribuição iniciou a operação.
5. **P2:** equipes como destinatárias. O modelo atual atribui somente usuários elegíveis; não foi criada uma estrutura paralela sem comprovação de equivalência.
