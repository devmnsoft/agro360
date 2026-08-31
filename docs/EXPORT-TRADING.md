# Exportação e Trading — Sprint 39

O módulo `/Export` controla clientes internacionais, contratos e embarques por tenant. A API está em `/api/export-trading` e exige `export.read`, `export.write`, `export.approve` ou `export.reports` conforme a operação.

## Regras entregues

* clientes bloqueados, reprovados ou inativos não recebem contratos; reprovação exige motivo e aprovação registra ator/data;
* contrato tem itens positivos, moeda e Incoterm; o total é calculado com `decimal` no backend e somente cliente ativo/aprovado permite aprovação;
* cancelamento exige motivo; embarque exige contrato aprovado, lote não bloqueado e saldo real de `storage.lots`;
* documentos obrigatórios pendentes são persistidos e consultados para liberação; rejeição exige motivo;
* taxas manuais exigem valor positivo e data. Não há consulta de cotação externa sem provedor;
* categorias de custos são controladas, valores negativos são recusados e margem é calculada no backend;
* consultas, dropdowns e CSV sempre recebem o tenant autenticado no SQL.

## Integrações e pendências reais

O vínculo documental usa `documents.files`, sem duplicar binários. Estoque/qualidade usa o lote real em `storage.lots`. A migração reserva vínculos auditáveis para produção, recebimento, fornecedor e documento. Não existe provedor externo de câmbio configurado, portanto apenas taxa manual auditável é aceita. A liberação definitiva do embarque deve ser feita somente após todos os checks/documentos obrigatórios estarem aprovados; nesta sprint a criação começa em `AWAITING_DOCUMENTS`.

## Relatórios

`customers`, `contracts`, `contracts-country`, `contracts-customer`, `contracts-product`, `shipments` e `margin` estão disponíveis em CSV e preservam filtros e tenant. Documentos pendentes, custos e rastreabilidade permanecem acessíveis pelas tabelas auditáveis e serão expandidos no exportador quando houver dados operacionais completos.
