# Compras e Suprimentos

A Sprint 37 entrega fluxo persistente e multiempresa de fornecedor, homologação, catálogo, requisição, cotação, alçada, pedido, recebimento, divergência e exportação. A central está em `/Procurement`; relacionamentos são pesquisados por nome/código, nunca digitados como GUID.

## Operação

1. Cadastre o fornecedor e solicite homologação. A decisão registra critérios, responsável, data, validade e motivo obrigatório na reprovação. Fornecedores bloqueados, inativos ou reprovados não são aceitos.
2. Cadastre materiais, serviços ou ativos e marque exigências de lote, validade, documento, inspeção e homologação.
3. Abra requisição com item ativo, quantidade positiva, prioridade e necessidade. Urgência exige justificativa.
4. Registre participantes e respostas reais na cotação. O mapa sugere menor preço, mas a decisão é humana; valor superior exige justificativa.
5. O pedido recalcula o total no backend e entra em aprovação. Alçada, segregação, orçamento e categoria crítica possuem tabelas próprias.
6. No recebimento parcial ou total, confira lote e validade. Excesso exige permissão e justificativa; divergências permanecem abertas até resolução.

## Integrações honestas

Os contratos `IProcurementStockGateway`, `IProcurementFinanceGateway`, `IProcurementDocumentGateway` e `IProcurementQualityGateway` definem a fronteira transacional. O recebimento nasce com `stock_integration_status=PENDING`: nunca declara entrada concluída sem integrador real. Pedido aprovado não é marcado como pago e não fabrica documento, inspeção ou workflow.

## Instalação sem Docker e homologação

Configure `ConnectionStrings__Agro360` para PostgreSQL externo e execute `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql`. O arquivo é autocontido, sem `\i`, credenciais ou host fixo. Pode-se aplicar `database/migrations/037_sprint37_procurement.sql` sobre 3.6.0. Valide RLS com dois tenants e percorra fornecedor → catálogo → requisição → pedido → aprovação → recebimento. PDF e provedores externos permanecem pendências reais, sem simulação.
