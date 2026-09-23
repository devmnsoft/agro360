# Evolução comercial — auditoria e entrega (2026-09-23)

## Baseline e escopo auditado

A execução iniciou na branch `work`, commit `775500d`, com árvore limpa. Foram inspecionados a solução .NET, migrations, instalador consolidado, contratos, serviços, API, Razor/JavaScript e testes. Nenhuma implementação foi presumida a partir de prompts anteriores.

| Fluxo | Estado encontrado | Evidência / decisão desta entrega |
|---|---|---|
| Clientes, vendedores e carteira | Funcional com evidência estática | `crm_customers`, representantes, portfólio, oportunidade e histórico já canônicos; perda exige motivo. Não foi criado cadastro paralelo. A autorização por carteira ainda não possui política granular por representante e segue como limitação. |
| Tabelas de preço | Funcional com evidência estática | Vigência/segmento e snapshot no item de pedido existentes. A proposta agora também preserva snapshot; não cria câmbio, tributo ou preço mínimo. |
| Propostas agro | Ausente | Implementadas como agregado versionado no schema canônico, com itens imutáveis por versão, estados, decisões, aceite evidenciado e conversão parcial. O módulo SaaS `commercial_proposals` permanece separado e não foi reutilizado indevidamente para vendas do tenant. |
| Contratos | Parcial | Agregado e transições existem; pedido controla saldo. Não foi declarada conversão de proposta para contrato nesta etapa: a conversão entregue cria pedido canônico. |
| Pedidos, reservas, entregas e devoluções | Parcial | Pedido, preço, fulfillment, reserva concorrente, entrega e pós-venda existem em fluxos canônicos separados. Aceite/conversão não reserva estoque. Visão unificada por item permanece backlog. |
| Recebíveis | Parcial | Financeiro canônico existe, mas a jornada proposta → pedido não cria recebível automaticamente. O vínculo completo faturamento/baixa à apuração de comissão requer integração transacional posterior. |
| Comissões | Parcial evoluído | Plano/regra/apuração existentes. Persistência ganhou snapshot de regra, identidade de evento e ajustes vinculados; o comando legado ainda precisa passar a apurar exclusivamente por eventos financeiros e preencher esses campos. Nenhum pagamento bancário é inferido. |

## Regras implementadas

* Versões anteriores nunca são atualizadas. Revisão material cria versão, invalida aprovação/aceite e retorna a raiz a `DRAFT`; após submissão exige motivo.
* Estados canônicos: `DRAFT`, `SUBMITTED`, `APPROVED`, `REJECTED`, `ACCEPTED`, `EXPIRED` e `CANCELLED`. Rejeição/cancelamento exigem motivo; versão substituída e validade vencida não podem ser aceitas.
* O aceite registra versão, instante, usuário autenticado e tipo/referência de evidência. É registro operacional, não assinatura digital.
* Quantidade e preço são positivos, desconto fica entre 0 e 100, valores usam `numeric`, cálculo por linha e arredondamento `MidpointRounding.AwayFromZero`; o banco reconcilia total dos itens + frete.
* Conversão aceita itens parciais, trava a proposta, soma conversões anteriores e rejeita excedente. Uma chave idempotente retorna o pedido já criado somente se o conteúdo for igual; conteúdo diferente gera conflito.
* A conversão copia moeda/condições e snapshots dos itens para o pedido, sem reservar, expedir, faturar ou criar recebível/comissão.
* Todas as novas tabelas têm tenant nas chaves/relacionamentos, RLS forçada, políticas, índices e SQL parametrizado.

## Atualização e recuperação

1. Faça backup conforme `docs/BACKUP-RESTORE.md`.
2. Aplique `database/migrations/107_commercial_proposals_commissions.sql` com a role de migração, na ordem, uma única vez. O script é transacional e registra a versão `10.7.0`.
3. Alternativamente, instalações limpas devem usar `database/agro360-postgres-full.sql`, que contém o mesmo bloco ao final.
4. Em falha, a transação inteira é revertida. Para recuperação posterior à aplicação, restaure o backup; não edite migration aplicada nem checksum.

## Limitações e backlog honesto

Não foram inventadas alçadas monetárias: a proposta reutiliza `commercial.orders.approve`; configuração explícita de alçadas condicionais permanece pendente. Também permanecem: extensão autorizada de validade como evento próprio, comparação/preview imprimível de versões, editor completo de proposta na Razor, margem estimada/realizada com fonte de custo, conversão para contrato, indicadores unificados contratado/reservado/expedido/entregue/devolvido/cancelado e processador de comissão por aprovação/faturamento/recebimento parcial/estorno. A migration prepara rastreabilidade de regra/evento/ajuste, mas o apurador legado não foi apresentado como concluído.

Build/runtime, PostgreSQL descartável, atualização representativa, seeds, login, dois tenants, concorrência real e jornada em navegador somente podem ser declarados após execução dos comandos descritos no relatório final; falhas ou indisponibilidade do ambiente devem permanecer explícitas.

## Continuidade — fechamento proposta → pedido

**Prioridade vigente:** o pedido deste ciclo e a base `0cd08ee` prevalecem sobre os recortes históricos dos prompts e checkpoints, sem apagar as decisões anteriores. A árvore foi reencontrada limpa exatamente nesse commit. Não houve conflito com instrução local (`AGENTS.md` não existe no escopo do repositório).

| Recorte | Estado nesta continuação | Evidência / limite |
|---|---|---|
| Rateio parcial | Corrigido | O valor de cada parcela é calculado sobre quantidade e valor acumulados; a última parcela recebe o residual exato. O cabeçalho soma os valores realmente gravados mais o frete, aplicado somente na primeira conversão. |
| Idempotência concorrente | Corrigido | A identidade normalizada inclui tenant (pela consulta/RLS), proposta da rota, versão e itens ordenados. Após o lock da proposta ocorre nova leitura da chave; colisão divergente permanece conflito. |
| Moeda | Compatibilidade incremental | A migration 108 adiciona moeda anulável ao pedido para não atribuir moeda a legado sem fonte. Conversões novas copiam a moeda da versão aceita. |
| PostgreSQL no CI | Gate obrigatório | O workflow cria PostgreSQL descartável, instala o consolidado e define a connection string. Localmente a suíte continua opcional; em `CI=true`, ausência da variável falha com diagnóstico. |
| Jornada Razor completa | Pendente | A correção desta continuação fecha invariantes de servidor e o gate de banco. Listagem/editor/comparação e validação responsiva no navegador ainda não foram implementados e não são declarados homologados. |
| Comissões | Pendente preservada | `CalculateCommissionAsync` continua legado e não passa a alegar origem/snapshot completos da migration 107. |

### Atualização e recuperação desta continuação

1. Faça backup e aplique `database/migrations/108_proposal_conversion_currency.sql` depois da 107.
2. A migration não preenche moeda histórica: valores nulos são diagnóstico de origem ainda não comprovada.
3. Instalações limpas usam o consolidado atualizado. Falha durante a migration reverte a transação; recuperação posterior segue restauração de backup, sem editar migrations aplicadas.
