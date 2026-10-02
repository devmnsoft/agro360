Você trabalha exclusivamente no repositório https://github.com/devmnsoft/agro360 (MNSOFT Agro360). Não use outro produto.

## Estado deixado pela execução anterior

HEAD de partida: `fcf3253907a5ba27ef7ce17094b44b02e02ed183`. As rodadas AG-OPS-MVP-002 e AG-OPS-MVP-003 permanecem **não commitadas** na árvore de trabalho: 16 arquivos modificados (duas das mudanças são as locais preservadas em `appsettings.Development.json` e `PostgreSqlConnectionConfiguration.cs`) + os novos `database/migrations/117_delivery_schedule_operation_identity.sql` e `scripts/verify-dispatch-confirmation.mjs`.

A rodada 002 alinhou tela, API e schema da programação de entrega, o saldo 100/30/50, a idempotência com hash e a migration 117. A rodada 003 fechou os bloqueios restantes de contrato/jornada: expedição só após confirmação positiva explícita (cancelar/silêncio não dispara POST, provado por teste executável), gating de `commercial.read` na aba Compromissos, selo ATRASADO derivado no critério do servidor, CSS escopado com tokens em `logistics.css` e ajuda contextual específica em Comercial (`forms.js`). Detalhe e matriz de aceite em `docs/EXECUTION-CHECKPOINT.md` (seção AG-OPS-MVP-003).

Não declare a jornada homologada. Build Release (0 avisos/0 erros), 264 testes unitários, 168/169 de arquitetura (a única falha é pré-existente e fora do diff: `appsettings.json` local com `DefaultConnection`), `node scripts/verify-dispatch-confirmation.mjs` (PASS), `node --check` e `git diff --check` passaram. PostgreSQL, HTTP e navegador não rodaram. Neste ambiente `dotnet test` sai com código 5; execute os testes via `dotnet exec tests/<Projeto>/bin/Release/net10.0/<Projeto>.dll`.

Preserve `appsettings*.json`, `PostgreSqlConnectionConfiguration.cs` e qualquer alteração local do usuário. Não publique, não faça merge e não apague histórico.

## Objetivo desta execução

Homologar a jornada já implementada e só então corrigir o que o banco e o navegador mostrarem:

proposta → pedido aprovado → programação → reserva por lote → separação → conferência → expedição parcial → entrega parcial ou recusa → pendência.

Critérios que ainda precisam de evidência real, não de leitura de código:

- Instalação limpa de `database/agro360-postgres-full.sql` e upgrade com a migration 117 aplicada pelo migrador (ele remove `BEGIN`/`COMMIT` e abre a transação).
- Criação, reprogramação e cancelamento por HTTP, com releitura de destino, responsável, data civil e versão.
- Pedido 100, programação atual 30, outras 50: quantidade 50 aceita e 60 rejeitada no PostgreSQL.
- Mesma chave e mesmo conteúdo não duplica; mesma chave e conteúdo diferente retorna conflito.
- Duas expedições concorrentes não deixam estoque negativo nem ultrapassam o compromisso.
- Falha no meio da transação não deixa saldo parcial.
- Tenant A não lê nem grava recurso do tenant B com o papel da aplicação.
- Atender dois produtos e mais de um lote persiste todos os vínculos.
- Saída parcial movimenta só o conferido e o remanescente continua atendível.
- Lote bloqueado depois da reserva impede a saída.
- Recusa não entra no estoque; retorno aguarda qualidade.
- Expedição no navegador com rede visível: cancelar ou fechar sem confirmar não envia POST; confirmar envia uma única vez `version` e `idempotencyKey`.
- Telas Comercial e Logística em 360, 768, 1280 e 1920 px, teclado e zoom 200%. Sem botão morto e sem erro de console.

## Fora desta rodada

Pacientes, minutas clínicas, PDF assistencial e assinatura eletrônica não existem no Agro360. Não criar esse módulo.

Também ficam de fora: expansão de IA, marketplace, novos provedores fiscais, novos módulos e estorno parcial de materiais. Não simular nota fiscal, crédito ou pagamento.

A central de compromissos já filtra no servidor e os indicadores seguem o mesmo filtro. A auditoria visual do shell e das telas que não são Comercial/Logística continua pendente; não marque essas telas como revisadas sem captura e console.
