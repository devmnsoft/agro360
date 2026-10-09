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

## Evolução 10.0 — ciclo de requisição e saldo autorizado

A auditoria desta entrega classificou fornecedores, catálogo, recebimento idempotente, quarentena, conferência de cobrança e reposição como **funcionais por evidência estática**; estoque e previsão financeira já são integrados pelo recebimento. Cotações persistiam no banco, mas permanecem **parciais**, pois ainda não há API/tela completa para propostas e versionamento comercial. Devolução de compra permanece **ausente**. A obrigação financeira automática no recebimento é comportamento legado vigente; a conferência documental não cria nem paga título.

Requisições agora nascem em rascunho (ou são submetidas explicitamente), têm versão e histórico imutável, e suportam submissão, aprovação, rejeição e cancelamento com concorrência otimista. Rejeição/cancelamento exigem justificativa e o solicitante não pode autoaprovar. Unidade operacional, centro de custo, item ativo e unidade canônica são validados dentro do tenant. A fila de aprovação usa a permissão `purchasing.approve`.

Pedidos vinculados exigem requisição aprovada e vínculo de cada linha ao item autorizado. O servidor bloqueia unidade incompatível e quantidade superior ao saldo, inclusive sob concorrência. Compras parciais e múltiplos fornecedores consomem o saldo; cancelar um pedido libera apenas suas linhas ainda contabilizadas, sem apagar recebimentos ou eventos. Esta migration não inventa alçadas monetárias: as políticas existentes continuam explícitas por tenant e nenhuma autoaprovação é concedida.

### Atualização

1. Faça backup e aplique `database/migrations/100_procurement_requisition_lifecycle.sql` com uma role de migração.
2. Confirme a versão `10.0.0` em `agro360.platform_schema_versions`.
3. Com a role da aplicação, valide dois tenants e a jornada rascunho → submissão → decisão → pedidos parciais → recebimento.
4. Para instalação limpa, use somente `database/agro360-postgres-full.sql`, que contém a mesma origem incremental.

### Pendências reais

* completar captura/comparação/versionamento de propostas e sua interface;
* implementar devolução ao fornecedor vinculada ao saldo aceito/consumido;
* separar por configuração o gatilho legado de previsão financeira no recebimento;
* executar homologação dinâmica PostgreSQL e navegador onde esses runtimes estejam disponíveis.

## Evolução 11.22 — conversão de cotação com confirmação explícita e atomicidade multi-fornecedor

A recomendação de menor preço continua sendo apenas recomendação: quando algum item seria decidido
silenciosamente pelo menor preço, a conversão recusa com `agro360.quotation.confirmation_required`
(409) antes de gravar qualquer linha, e só prossegue quando o usuário confirma explicitamente
(`?confirmLowestPrice=true` na API; a tela captura o 409 e pede confirmação). Decisões confirmadas
são persistidas como registro comercial auditável com justificativa padrão e `lowest_total` corrigido.

A conversão agora executa em uma única transação do kernel canônico de pedidos
(`IPurchaseOrderKernel`, mesma regra usada pela criação direta): bloqueio da cotação com `for update`,
validação de fornecedores (bloqueio de fornecedor derruba todos os pedidos, sem sucesso parcial),
um pedido por fornecedor vencedor, consumo do saldo autorizado da requisição, virada de status e
auditoria (`CONVERSION_STARTED`, `CONVERSION_ORDER_CREATED`, `CONVERTED`; falhas registram
`CONVERSION_FAILED` fora da transação apenas com código higienizado). Replay devolve exatamente os
mesmos pedidos — inclusive cancelados — sem recriação automática; reabastecimento vem de novo comando
explícito na requisição, nunca da conversão. Cancelamento de pedido reabre o saldo correspondente e
retorna a requisição para aprovação.

O registro das consultas de IA ao plano passa a vincular a execução à cota ativa do período corrente
(`quota_id`), mantendo linhas antigas `NULL` como histórico honesto. A governança das concessões da
migration 131 (migration 132, versão `11.22.0`) separa registro de catálogo, direitos comerciais por
plano (reparação por módulo, completando planos parcialmente atualizados), snapshot retroativo das
organizações ativas e fixtures de homologação: concessões automáticas por padrão de nome foram
revogadas e os tenants de teste passam exclusivamente do `seed-test-access.sql` com tuplas nominais.

### Atualização

1. Faça backup e aplique `database/migrations/132_module_grant_governance_and_partial_plan_repair.sql`
   com uma role de migração; é idempotente e reaplicável.
2. Confirme a versão `11.22.0` em `agro360.platform_schema_versions`.
3. Instalação limpa usa somente `database/agro360-postgres-full.sql` (espelho byte a byte).

### Validação

`tests/Agro360.IntegrationTests/ProcurementConversionJourneyTests.cs` executa a jornada real sobre
PostgreSQL com os serviços em produção (não stubs): recusa sem decisão (zero escritas), decisão parcial
não silencia a guarda, conversão confirmada multiprovecedor com auditoria e saldo integral, rollback
total com fornecedor bloqueado mais recuperação sem duplicação, concorrência serializada com replay
idêntico e cancelamento que reabre saldo sem recriação automática.
