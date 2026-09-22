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

## Evolução 102 — integridade do grafo (2026-09-22)

A auditoria encontrou que `RecordGenealogyLinkAsync` reutilizava a restrição natural
com `ON CONFLICT DO UPDATE`. Uma segunda requisição, com chave de idempotência
diferente, podia portanto alterar quantidade, unidade e metadados de um elo já
persistido. Isso contrariava a regra de histórico e foi corrigido: reenvio da mesma
chave retorna o mesmo identificador; uma chave diferente para o mesmo elo resulta
em conflito explícito e exige retificação, sem sobrescrever o fato anterior.

A escrita agora é serializada por tenant com advisory lock transacional, reconsulta
a chave depois da aquisição do lock e percorre o grafo por `(tipo, id)` antes do
insert. A migration `102_genealogy_integrity.sql` adiciona a defesa final no banco:
trigger tenant-scoped contra ciclos diretos e indiretos, índice do caminho ativo e
imutabilidade de elos que já saíram de `PENDING_REVIEW`. A mesma migration foi
acrescentada ao instalador consolidado sem reescrever migrations anteriores.

### Estado auditado e limites

| Jornada | Estado | Evidência / limite |
|---|---|---|
| Colheita → recebimento → lote | Parcial | Há chaves persistidas e consultas autenticadas; execução em PostgreSQL não foi possível nesta imagem. |
| Genealogia consultável | Parcial | Safra e lote possuem API/tela; elos ausentes continuam exibidos como lacuna. |
| Idempotência e ciclos de elos manuais | Implementada, não homologada dinamicamente | Serviço e trigger defendem concorrência/ciclos; teste estático de regressão foi incluído. |
| Fracionamento, mistura e transformação quantitativa | Parcial | Existem elos para produção/estoque, mas ainda falta um agregado transacional que reconcilie entradas, saídas, perdas e conversões. |
| Inspeções versionadas | Parcial | Modelos, runs e efeitos existem; a jornada completa não foi reexecutada nesta imagem. |
| Restrições e CAPA | Parcial | Restrições independentes, ações e eficácia existem; concorrência real com reserva/expedição não foi reexecutada. |
| Recolhimento | Não verificado | Vínculos de devolução/pós-venda existem; não foi encontrada homologação atual de prévia e confirmação de recall ponta a ponta. |
| Consulta pública | Parcial | Publicação explícita, token opaco e revogação existem; teste dinâmico entre tenants permanece obrigatório. |

O SDK .NET 10 e PostgreSQL/`psql` continuam ausentes do ambiente. Por isso esta
entrega não declara build, restore, RLS, concorrência ou jornada de navegador como
aprovados. O próximo incremento só deve criar eventos quantitativos de
fracionamento/mistura depois de validar estoque e movimentações com a role real da
aplicação; duplicar o saldo em um agregado paralelo não é uma solução aceitável.
