# Transferências, inventário físico e reconciliação

## Diagnóstico e modelo canônico

A implementação reutiliza `inventory_stock_balances`, `inventory_stock_movements`, produtos, depósitos e lotes; não existe um saldo paralelo. `available` é o saldo físico liberado no depósito, `reserved` é o compromisso ainda contido no físico e o **disponível para nova operação** é `available - reserved`. Quantidades em qualidade não liberada não integram `available`. Em trânsito é calculado por item como `expedido - recebido` e não pertence ao saldo de nenhum depósito. Quantidade bloqueada é preservada como custódia/ocorrência e somente material recebido em condição `GOOD`, que não saiu bloqueado, entra no saldo de destino. Custo de saída usa custo médio; ajuste usa o custo médio existente e fica com `valuation_pending` quando essa base não existe — preço de venda e custo zero fabricado não são usados.

A expedição é o instante de saída da origem e cria `TRANSFER_OUT`; cada conferência elegível cria `TRANSFER_IN` no destino. Locks de saldo, versão otimista, chaves idempotentes e índices únicos protegem concorrência e repetição. Correção de projeção deve ser feita na consulta; movimentos `ADJUSTMENT_*` existem somente após uma diferença física aceita.

## Jornadas e critérios de aceite

* Transferência: criar com depósitos distintos no mesmo tenant/unidade → expedir sob nova validação de `available - reserved` → receber total/parcial → manter restante em trânsito → registrar condição e ocorrência → encerrar somente quando tudo for recebido. Antes da expedição pode ser cancelada; depois dela a API exige retorno em vez de apagar movimentos.
* Inventário: planejar escopo parcial → abrir e fotografar saldo/reservas → bloquear movimentações do depósito/produto → registrar contagem e recontagens imutáveis → escolher a contagem aceita → aprovar diferenças uma vez → concluir. Inventários incompatíveis não se sobrepõem.
* A estratégia adotada é **bloqueio de movimentação**, não reconciliação temporal. A API genérica de estoque e a jornada de transferência verificam o bloqueio. Integrações/workers que usam essas APIs recebem conflito. Uma escrita direta no banco é fora do contrato suportado e deve ser impedida pelas permissões de conexão.
* `null` significa não contado e bloqueia a conclusão; `0` é uma contagem física válida. Contagem cega omite referência enquanto o trabalho está em `COUNTING`. Recontagem cria nova rodada e não sobrescreve a primeira.
* Ajuste revalida o saldo fotografado sob lock. Saldo aceito abaixo das reservas interrompe a aprovação e exige tratamento explícito. Cada movimento referencia o inventário e tem unicidade por item.

## Classificação da continuidade

| Área inspecionada | Situação nesta entrega |
|---|---|
| Depósitos, produtos, saldo, reservas e movimentos | Reutilizados; verificados por inspeção estática |
| Requisições, consumo, recebimento e qualidade | Existentes; integração de bloqueio completa apenas na API canônica de estoque; runtime não verificado |
| Transferência e trânsito | Implementados; runtime PostgreSQL pendente |
| Inventário, contagem, recontagem e ajuste | Implementados com bloqueio; runtime PostgreSQL pendente |
| Custos | Custo médio reaproveitado; valorização pendente explícita; relatórios contábeis parciais |
| Central de Trabalho | Ausente nesta fatia: projeções específicas ainda não incluídas |
| Permissões, tenant e auditoria | Políticas HTTP/RLS e autoria presentes; escopo granular por depósito parcial |
| Interface compartilhada e acessibilidade | Reutilizados; inspeção de navegador/mobile pendente |

## Matriz de auditoria dos formulários

| Formulário | Inclusão/releitura | Transições e reversão | Permissão/concorrência | Situação |
|---|---|---|---|---|
| Nova transferência | POST e GET/lista | aguarda expedição; cancelamento pré-efeito | `inventory.move`, tenant, versão | Implementado; runtime pendente |
| Expedição | movimentos e detalhe | idempotente por estado/índice; não conclui | lock de saldo e reserva | Implementado; corrida real pendente |
| Recebimento | parcial e releitura | ocorrência para bloqueado/avariado | chave idempotente, versão | Implementado; runtime pendente |
| Planejamento/abertura | escopo parcial e snapshot | bloqueio; sem exclusão física | `inventory.adjust`, lock e sobreposição | Implementado; integrações legadas a auditar |
| Contagem/recontagem | rodadas imutáveis | zero distinto de vazio | `inventory.move`, versão | Implementado; navegador pendente |
| Reconciliação/aprovação | decisão aceita e movimento | ajuste único, sem edição de movimento | `inventory.adjust`, saldo/reservas sob lock | Implementado; PostgreSQL pendente |

## Verificação em homologação

Aplicar a migration 082 em banco incremental e em banco vazio pelo consolidado. Executar restore/build/test com SDK 10.0.100 e, com dois tenants e perfis de leitura/operação/ajuste, validar transferência válida, depósitos iguais, insuficiência, expedição repetida, recebimento parcial, bloqueio/avaria, zero versus não contado, bloqueio durante contagem, histórico de recontagem, ajuste repetido e insuficiência perante reservas. Percorrer `/Inventory?section=transfers` e `/Inventory?section=counts` em desktop, viewport móvel, teclado e leitor de tela.
