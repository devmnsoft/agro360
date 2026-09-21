# Genealogia comercial e rastreabilidade pública

## Escopo AG-E6-GEN-002

A genealogia canônica permanece no agregado de colheita. A consulta por safra ou número comercial de lote percorre somente chaves persistidas entre plano, apontamento, recebimento, inspeção, destinação, estoque, reserva/beneficiamento, lote industrial, remessa, entrega e retorno. Nome, proximidade de datas e texto livre nunca criam um elo; ausência de chave aparece como lacuna.

`GET /api/v1/harvest/operational-pendings` projeta pendências reais e direciona para o módulo dono. A Central não conclui qualidade, entrega, retorno ou estoque. Cada item informa impacto, ação, URL e permissão requerida.

## Publicação segura

`POST /api/public/trace` exige `traceability.publish`, lote aprovado, origem agrícola comprovada e chave idempotente. O servidor gera código opaco aleatório e grava um snapshot exclusivamente público. A fazenda só entra no snapshot quando `publishFarm=true`. Qualidade pendente, quarentena ou bloqueio impedem a publicação como liberado.

`POST /api/public/trace/{publicCode}/revoke` exige a mesma permissão e motivo com ao menos três caracteres. Revogação é auditada, não apaga o snapshot e faz a consulta anônima passar a responder `404`.

`GET /api/public/trace/{publicCode}` é anônimo e lê apenas `safe_payload`. O contrato não contém tenant/user IDs, GUIDs internos, preço, custo, finanças, pessoas, documentos ou auditoria. A tabela mantém `tenant_id` para gestão autenticada, RLS, índices globais do código opaco e auditoria de publicação/revogação.

## Validação obrigatória antes de homologar

Aplicar a migration `096_public_traceability.sql` em PostgreSQL descartável, testar publicação e revogação autenticadas em dois tenants, confirmar isolamento cruzado e inspecionar a resposta pública. O instalador integral deve ser exercitado separadamente. Sem esses gates, o recorte está implementado, não homologado.
