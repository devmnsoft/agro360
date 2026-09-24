# Incremento de atendimento de pedidos — 2026-09-24

## Base auditada

Branch `work`, commit inicial `83129979e6ef4fafc9ee2082f11b14f968668c63`. A solução fixa .NET 10, Razor Pages, Dapper 2.1.66, Npgsql 10 e PostgreSQL. O executor transacional possui sobrecargas tipadas; as quatro expressões que retornam resultado em `Commercial360Service` usam explicitamente `InTenantTransactionAsync<T>`. A compilação não pôde ser executada porque o SDK .NET não está instalado neste ambiente.

| Área | Diagnóstico inicial | Resultado deste incremento |
|---|---|---|
| Proposta → pedido | implementado; build não verificado | moeda, arredondamento, conversão parcial e idempotência preservados |
| Reserva e saldo | parcial | mecanismo canônico reutilizado; disponibilidade e lock mantidos |
| Lote e qualidade | implementado | aprovação/validade revalidadas antes da saída |
| Separação/expedição | parcial | visão operacional e idempotência estrita da saída completadas |
| Entrega/devolução | implementado; não homologado neste ambiente | sem alteração funcional |
| Permissões/tenant/auditoria | implementado | endpoints continuam autorizados e todas as consultas tenant-scoped |
| Interface | parcial | fila paginada, busca comercial e detalhe em cinco seções |

## Responsabilidades e invariantes

Comercial registra o compromisso aceito; Estoque registra disponibilidade, reserva e movimento; Qualidade decide a elegibilidade do lote; Logística separa, expede, transporta e registra entrega; Financeiro reage apenas a seus próprios eventos. Pedido confirmado não é entrega, reserva não é saída e expedição não é pagamento ou autorização fiscal.

A visão calcula quantidades das tabelas de pedido, reservas e expedições, sem contadores paralelos. `pendente = pedida - cancelada - expedida`; reserva ativa e separação são subconjuntos do pendente. A migration ainda não adiciona cancelamento quantitativo: portanto, nesta versão `cancelada = 0`. Quantidade já expedida continua imutável e devolução mantém evento próprio.

## Homologação requerida

1. Aplicar migrations até `109` em cópia descartável e também criar uma base limpa pelo SQL consolidado.
2. Executar restore, build e todas as suítes.
3. Com PostgreSQL real, validar duas reservas concorrentes, rollback induzido e isolamento entre tenants.
4. Pela tela Logística, filtrar e paginar pedidos, abrir o detalhe, criar atendimento existente, confirmar saída duas vezes com a mesma chave e repetir com chave/conteúdo conflitante.
5. Validar desktop, viewport móvel, teclado e leitor de tela.

## Fechamento operacional (incremento 110)

O fechamento foi implementado sobre as tabelas canônicas de pedido, estoque e fulfillment. O `HEAD` de entrada era `ab43b2864f35077f8c2f62c5755d6009ab2be5cf`; nenhuma mudança posterior existia no branch de trabalho.

* **Cumulativos comerciais:** `sales_order_items.quantity` é a quantidade original imutável; `cancelled_quantity` acumula cancelamentos; `sum(fulfillment_reservations.consumed_quantity)` acumula expedições. Assim, `pendente = pedida - cancelada - expedida`.
* **Reserva:** `quantity` é original; `consumed_quantity` e `released_quantity` são cumulativos; o saldo ativo derivado é `quantity - consumed_quantity - released_quantity`. `ACTIVE` identifica saldo comprometido. Liberação reduz `inventory_stock_balances.reserved` e não cria movimento/entrada física.
* **Preparação:** `picked_quantity` e `checked_quantity` representam o estado corrente do documento, não consumo comercial. A criação compatível aceita os valores antigos, mas zero cria `PREPARING`; conferência positiva de todos os itens promove a `CHECKED`.
* **Cancelamento:** é um evento auditável em `fulfillment_order_item_cancellations`, preserva quantidade e valores originais, libera reserva ativa não preparada na mesma transação e não altera quantidade expedida. Documento separado/conferido bloqueia a ação até desfazimento explícito. Nenhum estorno financeiro é simulado.
* **Concorrência:** criação e cancelamento usam advisory locks determinísticos por item comercial e depois por lote; atualizações condicionais de versão/saldo detectam disputa. O protocolo impede que reservas em depósitos/lotes diferentes ultrapassem o mesmo item comercial.
* **Replay:** cada preparação, liberação e cancelamento grava identidade em `fulfillment_operation_requests`. A identidade de despacho é verificada antes do estado atual, portanto o mesmo conteúdo retorna sucesso mesmo após entrega/reconciliação; chave com conteúdo diferente conflita.
* **Interface:** o detalhe oferece reserva por depósito/lote elegível (sem GUID), separação/conferência, liberação, cancelamento e despacho, sempre relendo o estado canônico. As abas Separação e Expedições conduzem à fila real e não exibem sucesso fictício.

### Ordem de bloqueios

Para operações com mais de um agregado, a ordem obrigatória é: **item comercial (ordenado por UUID) → lote (ordenado por UUID) → saldo de estoque → reserva → item/documento de expedição**. Chamadas externas não ocorrem dentro dessas transações.

### Comandos e evidências desta execução

* `./scripts/validate-database-assets.sh`: consolidado e migrations válidos.
* `python3 tools/check-api-routes.py`: rotas HTTP sem colisões.
* `node --check src/Hosts/Agro360.Web/wwwroot/js/logistics.js`: JavaScript válido.
* `dotnet build MNSOFT.Agro360.sln --no-restore`: não executado com sucesso, pois o SDK fixado em `global.json` não está instalado no contêiner (`dotnet: command not found`).

### Riscos e homologação pendente

O código não deve ser declarado homologado sem .NET 10, PostgreSQL e navegador. Em ambiente de CI, executar instalação limpa e upgrade até 110, todas as suítes e os dez cenários concorrentes/tenant descritos na solicitação. Validar também foco/teclado e viewport móvel. O próximo pacote, somente depois desta homologação, permanece sendo editor de propostas e acabamento visual de moeda; financeiro e comissões não fazem parte deste incremento.
