# Matriz de rastreabilidade v0.2.0

| Capacidade | Web | API/Application | Dapper/banco | Segurança/auditoria | Teste | Estado |
|---|---|---|---|---|---|---|
| Propriedade/talhão | atalho informativo | `PropertiesController`/contratos | `PropertyService`, `geo.*` | policy, RLS, audit | domínio/estrutura DB | FOUNDATION |
| Safra/plantio/colheita | atalho informativo | `AgricultureController` | `AgricultureService`, `agriculture.*`, estoque/custo/outbox | policy, RLS, audit | domínio | FOUNDATION |
| Estoque | KPI/atalho | `InventoryController` | `InventoryService`, `inventory.*` | policy, RLS, audit | domínio/constraint | FOUNDATION |
| Animal/pesagem/sanidade | KPI/atalho | `LivestockController` | `LivestockService`, `livestock.*`, custo/estoque | policy, RLS, audit | regras de GMD/carência | FOUNDATION |
| Venda/recebível | atalho informativo | `CommercialController` | `CommercialService`, `commercial.sales`, `finance.receivables` | policy, RLS, audit | regras de venda | FOUNDATION |
| Outbox | não aplicável | writer transacional/Worker | `platform.outbox_messages` | RLS, correlação, log sem payload | migration/integridade | FOUNDATION |
| Command Center | integrado | Discovery/dashboard | views e consultas Dapper | JWT/policy/RLS | arquitetura | CORE parcial |
| Alimentação/dieta | não existe | não existe | apenas fundações futuras | não aplicável | não existe | PLANNED |

`FOUNDATION` é deliberado enquanto não houver UI operacional conectada e E2E correspondente.
