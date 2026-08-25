# Validação de formulários e lookups

## Contrato

`GET /api/lookups/{resource}?search=texto&page=1&pageSize=20` retorna uma página com `id`, `label`, `description`, `status` e `metadata`. São suportados: `properties`, `production-areas`, `fields`, `crops`, `crop-seasons`, `suppliers`, `inventory-items`, `machines`, `people`, `cost-centers`, `livestock-batches`, `storage-lots` e `routes`.

A fonte é uma lista permitida no servidor, nunca SQL fornecido pelo cliente. As consultas usam parâmetros Dapper, filtro obrigatório pelo tenant, apenas ativos por padrão, ordenação amigável e limite máximo de 50. `includeInactive=true` é explícito.

## Formulários

O navegador valida obrigatoriedade, limites, números e datas antes do envio e associa mensagens ao campo. Relações usam autocomplete com texto amigável e um campo oculto para a chave selecionada. Digitar texto sem escolher um resultado é inválido. No backend, referências são revalidadas no tenant e regras cruzadas são aplicadas dentro da mesma transação que persiste efeitos de estoque e auditoria.
