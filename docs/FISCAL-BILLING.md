# Fiscal e Faturamento — Sprint 40

## Escopo real

O módulo registra operações e regras fiscais gerenciais, faturamentos nacionais e de exportação, documentos NF-e, NFS-e, NFC-e, CT-e, MDF-e, nota de produtor, entradas e documentos de exportação. Toda consulta e escrita ocorre dentro da transação que configura o tenant no PostgreSQL; as tabelas também usam RLS.

## Limite tributário e emissão

Alíquotas, CST/CSOSN, CFOP, bases, reduções, retenções e impostos são **informados e parametrizados**. O Agro360 não oferece consultoria tributária nem determina o imposto oficial. Não existe autorização SEFAZ simulada: sem certificado e provedor, o documento fica `AWAITING_EXTERNAL_ISSUANCE`, `PROVIDER_PENDING` ou `NOT_CONFIGURED`. `IFiscalIssuanceProvider` é o ponto de extensão para um provedor homologado. Referências XML/PDF somente apontam para documentos existentes; nenhum arquivo fictício é criado.

## Fluxos e invariantes

- Operação inativa é recusada; exportação exige contrato internacional; serviço não gera movimento físico.
- Regras vencidas não são selecionadas automaticamente e sobreposição de operação/produto/UF/vigência é recusada.
- Quantidade e valor unitário são positivos, percentuais usam `decimal`, desconto não torna item negativo e o total é recalculado no backend.
- Confirmação grava usuário/data. Estoque bloqueado, rejeitado ou insuficiente impede a confirmação; movimentos e integrações são rastreados.
- Parcelas devem somar exatamente o documento. A integração cria uma pendência real, nunca uma liquidação fictícia.
- Rejeição e cancelamento exigem motivo e geram evento de auditoria.
- Conferência de compra divergente exige justificativa e preserva a divergência aberta.

## Integrações e pendências reais

`IFiscalStockIntegration` e `IFiscalFinancialIntegration` isolam estoque e financeiro. O adaptador atual valida lotes e registra movimentos/previsões pendentes para processamento pelos módulos internos. A emissão governamental só poderá ser habilitada após seleção de provedor, certificado A1/A3 protegido, homologação por UF/município e política operacional de contingência. CT-e e MDF-e são controles documentais, não emissão.

## API e relatórios

A API autenticada está em `/api/fiscal`; permissões: `fiscal.read`, `fiscal.write`, `fiscal.approve` e `fiscal.reports`. CSVs disponíveis: operações, regras, faturamentos, documentos/status, faturamento por cliente/produto/operação, divergências de compra, integrações pendentes e movimentos fiscais. Todos recebem filtros e tenant da sessão.

## Execução sem Docker

Defina `ConnectionStrings__Agro360` apontando para PostgreSQL externo, aplique `database/migrations/040_sprint40_fiscal_billing.sql` (ou a instalação completa) e execute os hosts com `dotnet run`. Docker não é requisito.
