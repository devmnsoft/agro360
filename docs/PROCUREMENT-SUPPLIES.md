# Compras e Suprimentos

A Sprint 37 entrega fluxo persistente e multiempresa de fornecedor, homologação, catálogo, requisição, cotação, alçada, pedido, recebimento, divergência e exportação. A central está em `/Procurement`; relacionamentos são pesquisados por nome/código, nunca digitados como GUID.

## Operação

1. Cadastre o fornecedor e solicite homologação. A decisão registra critérios, responsável, data, validade e motivo obrigatório na reprovação. Fornecedores bloqueados, inativos ou reprovados não são aceitos.
2. Cadastre materiais, serviços ou ativos e marque exigências de lote, validade, documento, inspeção e homologação.
3. Abra requisição com item ativo, quantidade positiva, prioridade e necessidade. Urgência exige justificativa.
4. Registre participantes e respostas reais na cotação. O mapa sugere menor preço, mas a decisão é humana; valor superior exige justificativa.
5. O pedido recalcula o total no backend e entra em aprovação. Alçada, segregação, orçamento e categoria crítica possuem tabelas próprias.
6. No recebimento parcial ou total, confira lote, validade, depósito, conta financeira, primeiro vencimento e parcelas. Excesso exige permissão e justificativa; divergências ficam sinalizadas para tratamento operacional.

## Integrações honestas

O recebimento de material agora conclui, na mesma transação PostgreSQL, a entrada no estoque pelo ledger canônico e a criação das previsões em `finance_payables`. A chave de idempotência, o bloqueio do pedido e os vínculos de integração impedem duplicação em reenvios ou concorrência. Serviços não geram estoque; itens materiais exigem produto relacionado e depósito. As parcelas nascem `OPEN`, nunca `PAID`, e o recebimento só informa integração concluída depois de toda a transação confirmar. Documento externo, pagamento e encerramento de inspeção continuam pendências reais, sem simulação.

## Instalação sem Docker e homologação

Configure `ConnectionStrings__Agro360` para PostgreSQL externo e forneça a autenticação por `PostgreSql__Password`, `PostgreSql__Passfile` ou mecanismo externo compatível. Execute `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql`. O arquivo é autocontido, sem `\i`, credenciais ou host fixo. Em instalações incrementais, aplique também `database/migrations/064_procurement_receipt_integrations.sql`. Valide RLS com dois tenants e percorra fornecedor → catálogo → requisição → pedido → aprovação → recebimento → estoque → previsão financeira. PDF e provedores externos permanecem pendências reais, sem simulação.
