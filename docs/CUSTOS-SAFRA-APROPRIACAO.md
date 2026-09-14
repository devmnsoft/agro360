# Custos por safra, apropriação e conferência gerencial

## Base real e classificação

Diagnóstico realizado na branch `work`, a partir do commit `f644ccc5b75d64337864edb7f3e98a9e913afe2d`. A solução é `MNSOFT.Agro360.sln` e o `global.json` exige .NET SDK 10.0.100. O ambiente não possui `dotnet` nem PostgreSQL; restore, build, testes, migration e E2E permanecem **não verificados em runtime**.

| Capacidade encontrada | Classificação anterior | Evidência |
|---|---|---|
| Fechamento versionado | Implementado sem verificação runtime | migration 077, `HarvestService`, `/Harvest` |
| Colheita e recebimentos | Implementado sem verificação runtime | migration 075, `HarvestController`, `HarvestService` |
| Estoque e consumo | Implementado sem verificação runtime | `InventoryService`, `StorageService`, migrations 068/073 |
| Produção industrial | Implementado sem verificação runtime | migration 076, `IndustrialProductionService`, `/Production` |
| Financeiro/centros de custo | Parcial | `FinanceService`, migration 036, `/Finance` |
| Frota e manutenção | Implementado sem verificação runtime | `FleetService`, `FleetOperationsService`, `/Fleet` |
| Usuários, permissões e módulos | Implementado sem verificação runtime | `PermissionAuthorization`, `TenantContextMiddleware`, catálogo de módulos |
| Componentes compartilhados | Verificado apenas estaticamente | `_Layout.cshtml`, `agro360.css`, `forms.js` |
| Apropriação/rateio reproduzível | Ausente | matriz de rastreabilidade e catálogo marcavam rateio como planejado |

## Semântica dos valores

* **Previsto:** orçamento/plano comparável; não é custo realizado.
* **Comprometido:** obrigação assumida; não implica consumo. Quando substituída pela realização, não integra novamente o reconhecido.
* **Reconhecido/realizado:** custo gerencial de evento operacional ou despesa manual justificada. Pedido, pagamento e entrada em estoque, isoladamente, não reconhecem consumo.
* **Pago:** saída financeira, exibida separadamente; não determina a safra.
* **Apropriado:** parcela reconhecida confirmada em uma safra/destino.
* **Pendente:** `reconhecido - apropriado`; nunca pode ser negativo.
* **Estornado:** confirmação revertida com referência, ator, data e motivo, preservando o original.

O painel é gerencial, não uma demonstração contábil oficial. Nesta versão somente BRL é aceito; não há conversão cambial implícita.

## Jornada e regras entregues

A página `/Costs` oferece visão geral, lançamentos/origem, pendências, apropriação direta, rateio em cinco passos, prévia sem persistência, confirmação, histórico/estorno, conferência por corte e CSV autorizado com os mesmos filtros. Seletores usam nomes; filtros e paginação são processados no backend.

Métodos: percentual informado (100% com tolerância 0,0001), área, quantidade produzida, horas, igualitário e direto. Bases ponderadas devem ser positivas. Destinos repetidos e unidades divergentes são rejeitados. Valores usam quatro casas e `MidpointRounding.AwayFromZero`; o resíduo determinístico vai para a última linha e a base fica no snapshot histórico.

A confirmação usa `FOR UPDATE` e revalida tenant, escopo, propriedade, safra aberta, talhão, centro de custo, saldo e `row_version`. Chave idempotente impede repetição. Estorno é transacional, exige motivo/permissão, não ocorre duas vezes e devolve o lote confirmado ao saldo. Autoria vem do contexto autenticado.

Origens de `cost_entries` são migradas sem segundo financeiro: cada evento recebe chave `legacy:<id>` e vínculo histórico direto quando já tinha safra inequívoca. Despesas manuais exigem justificativa e chave de origem. Não foram inventadas integrações sem vínculo real.

## Fechamento, limitações e continuidade

A conferência informa apropriado até o corte, pendências, apropriações e estornos posteriores. Não reescreve fechamento confirmado: indica nova apuração ou reabertura autorizada. Pagamento pendente não bloqueia fechamento operacional. A reconciliação estoque/produção permanece limitada aos vínculos inequívocos.

1. Homologar restore/build/test, API, Web, Swagger e migration 078 em PostgreSQL limpo e incremental.
2. Exercitar em desktop/mobile apropriação, arredondamento, base zero, saldo insuficiente, repetição, concorrência, estorno e isolamento entre tenants.
3. Reconciliar custos de produção e consumo por chaves de eventos parciais, aceitando quando totais não duplicarem entrada/consumo.
4. Completar lote → venda → expedição; evoluir margem somente quando custo e receita atribuíveis reconciliarem com as origens.
