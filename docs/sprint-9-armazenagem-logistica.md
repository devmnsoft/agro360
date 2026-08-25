# Sprint 9 — Armazenagem, pós-colheita e logística

## Fluxo operacional

1. Cadastre a estrutura (`/api/storage/structures`) com capacidade, unidade, produto permitido e situação operacional.
2. Abra o romaneio, registre bruto/tara em `weigh`, classifique e aprove. A classificação consulta parâmetros do produto, emite alerta/reprovação e recalcula o desconto.
3. `unload` executa uma transação PostgreSQL: valida estado/capacidade, forma ou alimenta o lote, grava origem e movimento e reduz a capacidade disponível.
4. Lotes preservam romaneio, safra, talhão e composições. Transferir movimenta capacidade nas duas estruturas; bloquear impede saídas.
5. Uma ordem de processamento baixa a entrada, registra perda/custo e forma o lote de saída. `DRYING` representa secagem.
6. A expedição é pesada e despachada. O despacho bloqueia saldo insuficiente/lote bloqueado, baixa lote, libera capacidade e atualiza o contrato.
7. A viagem guarda transportador, distância, frete, custos por tonelada/km, ocorrências e entrega. O dashboard consolida capacidades, pendências, perdas, contratos, viagens, frete e alertas.

Todas as escritas usam Dapper parametrizado dentro de transação, `tenant_id`, autorização por policy e auditoria. Erros de domínio chegam ao cliente no formato Problem Details pelo middleware global.

## Rastreabilidade

`storage.product_traceability` liga lote e seus registros de origem (romaneio, talhão ou lote de composição) às expedições. Parta do número da expedição, consulte o lote e percorra `lot_origins` até romaneio, safra e talhão.

## Interface

O Command Center traz indicadores reais e atalhos responsivos para estruturas, romaneios/pesagem, qualidade/descontos, lotes, processamento/secagem, expedição, fretes e contratos. Estados de carregamento e erro são anunciados na própria seção.
