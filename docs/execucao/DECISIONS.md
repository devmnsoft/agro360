# Decisões de execução

## ADR-E9-01 — Fila local não é conclusão e replay é idempotente (2026-09-11)

A operação móvel grava primeiro em armazenamento local particionado pelo contexto autenticado. `PENDING`/“salvo neste dispositivo” não significa aplicado. Sincronização exige sessão online vigente, dispositivo ativo, ator e tenant do servidor, catálogo de comandos v1 e lote limitado. Chave igual com hash igual recupera o resultado; hash diferente gera conflito, sem “última escrita vence”. Trava consultiva serializa concorrentes e efeito interno + recibo persistido compartilham transação. O produto promete efeitos idempotentes diante de repetição, não entrega “exatamente uma vez”. Acesso local vence em 12 horas; revogação só pode ser conhecida quando o dispositivo volta a comunicar.


## ADR-E8-03 — Saída física não é entrega aceita (2026-09-10)

A reserva não altera saldo físico. A confirmação de expedição, dentro da mesma transação que consome a reserva, reduz o lote e grava exatamente um movimento referenciado; entrega não repete essa baixa. Aceites são eventos por tentativa e atualizam somente o saldo conciliado. Recusa permanece em trânsito/retorno pendente e nunca reentra automaticamente. Retorno exige recebimento e qualidade antes de disponibilidade. Idempotência compara hash do comando e controle de versão rejeita despacho baseado em leitura antiga. Fiscal, recebível e pagamento continuam integrações distintas e não são inferidos de mudança de status.


## ADR-E0-07 — Ponte aditiva para formato financeiro publicado (2026-09-10)

As migrations 001 e 007 permanecem imutáveis para preservar checksums. Uma migration ordenada antes da 007 renomeia somente a tabela `finance.receivables` com a assinatura legada; outra, imediatamente posterior, cria uma conta técnica por tenant e copia títulos positivos de forma idempotente para o formato Sprint 8. Registros antigos de valor zero ficam preservados na tabela legada, pois convertê-los criaria títulos inválidos. A ponte não declara upgrade homologado sem execução PostgreSQL descartável.

## ADR-E0-01 — Produto, precedência e controles

Em 2026-09-08 foi confirmado `C:\MNSOFT\agro360`, remoto `https://github.com/devmnsoft/agro360.git`, HEAD inicial `4b650fb74fc6d73050a5d84d0540f3962bf75f8c`. O plano anexado é requisito aprovado, não certificação do estado. A cópia integral está em `AGRO360-MASTER-PLAN.md`. O texto original não precisa ser reenviado.

Reutilizar `docs/EXECUTION-CHECKPOINT.md` e `docs/TRACEABILITY-MATRIX-v0.2.0.md`, encontrados durante trabalho concorrente, como checkpoint e matriz. Não criar outros arquivos com o mesmo papel em `docs/execucao`. Preservar seções históricas e distinguir evidência atual de resultado anterior.

## ADR-E0-02 — Stack e verificação isolada

Preservados .NET 10 (`global.json` 10.0.100, rollForward latestFeature), Razor, Dapper, PostgreSQL e camadas existentes. SDK efetivo nesta máquina: 10.0.400. `ConnectionStrings__Agro360` é o contrato do produto, não `DefaultConnection` do SIGOV.

Testes de E0 usam PostgreSQL 18 local em cluster novo, loopback, portas livres e senha aleatória transmitida ao initdb por named pipe. Nenhuma senha é escrita em arquivo/linha de comando. API usa variável de ambiente; fixture Santa Clara recebe hash aleatório apenas nessa instalação descartável. Artefatos são ignorados pelo Git. O script é Windows/PowerShell 7, não substitui CI Linux nem configuração de homologação persistente.

Não foram criadas migrations nesta entrega E0: mudanças de readiness e cache não alteram schema. Migrations e SQL alterados por execuções concorrentes não são atribuídos a esta entrega.

## ADR-E0-03 — Readiness não é liveness

`/health/live` prova processo vivo; `/health` verifica conexão e presença/acesso às colunas mínimas usadas por login e resumo inicial. Banco vazio/incompleto retorna 503. Não executa DDL, não revela nomes de contas e não certifica toda a estrutura nem segurança da base. Migration/integridade/autorizações continuam gates separados.

## ADR-E0-04 — Cache offline sem sucesso fictício

O antigo service worker interceptava GET fora de `/api/`, inclusive `/health` e Swagger de outra origem, e substituía falhas por `/field` HTTP 200. O erro foi reproduzido visualmente com API indisponível. Agora somente a allowlist de shell público da mesma origem é cacheável; dados autenticados, bootstrap mobile, health e OpenAPI não recebem fallback.

O cache antigo de lookups não possuía isolamento por identidade/tenant. Sua reutilização foi removida e as versões `agro360-shell-*`/`agro360-lookups-*` antigas são invalidadas na ativação. Não são removidos outros caches. O shell continua disponível offline; dados de trabalho offline dependem de AG-E9-001 e **não estão entregues por esta correção**.

O botão de diagnóstico valida `Healthy` e JSON OpenAPI, não só status HTTP, usa `no-store`, tempo limite e a URL configurada na mensagem de rede. Não altera autenticação ou autorização.

## ADR-E0-05 — Trabalho concorrente e propriedade das alterações

A branch `codex/agro360-e0-runtime` foi criada nesta execução a partir do HEAD local, preservando os três commits à frente de `origin/main`. Durante a inspeção, outras execuções alteraram propriedades, usuários/SaaS, SQL, layout e documentação. Erros de compilação transitórios decorrentes desses edits não foram corrigidos/revertidos por esta entrega. Os três ajustes MTP nos projetos de teste já existiam na baseline.

Somente health check, diagnóstico/cache Web, método de regressão na classe existente, gate `verify-e0.ps1` e seções documentais de consolidação são desta entrega. Não atribuir as classes novas de testes de propriedades a esta rodada; não removê-las. Antes de commit futuro, revisar arquivo e hunk individualmente. Nenhum commit/push/PR foi criado aqui.

## ADR-E5-01 — Integridade comercial e de apontamentos (2026-09-09)

- A alçada máxima de desconto é exclusivamente a configuração vigente do item da tabela de preços do segmento do cliente. O contrato do pedido não recebe esse limite; ausência de política para produto/unidade bloqueia a operação.
- Estados de pedido são normalizados uma vez e submetidos a matriz fechada. A ordem é lida com bloqueio na transação antes das precondições e da atualização; estados finais não reabrem implicitamente.
- Apontamento só é aceito em ordem `RELEASED`, `IN_PRODUCTION` ou `PAUSED`, com validação de linha, operador e etapa. A conformidade decorre dos critérios configurados; etapa crítica sem critério permanece pendente.
- Conclusão industrial exige evidência positiva: apontamento crítico concluído/conforme e inspeção obrigatória aprovada para cada lote. Ausência de reprovação não representa aprovação.

## ADR-E5/E7-02 — Merge PR #96 + commit local e preço/qualidade efetivos (2026-09-09)

O checkout real estava em `main`, HEAD `74a2d5b0d016606e9ee957081b0fa91bb21fa110`, com `origin/main` em `adc7cdd0f902a7fdb544f495e1907bf4825fffce`. O merge de `origin/main` foi resolvido preservando o avanço local de recebimento de compras integrado a estoque/financeiro e mantendo, nos conflitos SaaS, o lado remoto do PR #96 por conter permissões granulares, aceite de convites e remoção de credenciais universais. O instalador completo recebeu também o bloco incremental de compras para não divergir da migration local.

Preço comercial efetivo agora combina preço negociado e desconto explícito contra o `base_price` e o `maximum_discount` vigentes da tabela de preço do segmento. O total é soma de linhas arredondadas com `MidpointRounding.AwayFromZero`, alinhado ao `round(numeric,2)` do PostgreSQL, e cada item preserva `price_table_id`, `base_unit_price` e `pricing_snapshot`.

Na produção, a existência da etapa deixou de depender de tupla/default e passou a usar read model anulável. A conclusão industrial avalia a situação efetiva do lote em `production_batches.quality_status`; uma aprovação histórica não libera lote bloqueado/reprovado depois. Ordem sem etapa crítica registrada não cria obrigação fictícia no modelo atual, mas o snapshot versionado de roteiro receita → ordem continua pendente.

## ADR-E0/E8-02 — Exclusão lógica e merge reconciliado (2026-09-10)

Nenhum registro de negócio/histórico da aplicação deve ser apagado fisicamente pela operação comum. `deleted_at`/`deleted_by`/`deletion_reason` são a fonte de exclusão lógica; `status`/`active` permanecem significados operacionais distintos. Soft-delete não cancela/estorna efeitos de estoque, financeiro ou OS.

Autoria (`created_*`/`updated_*`) vem do contexto autenticado do servidor. Dados legados sem ator permanecem com `created_by` nulo; novas operações exigem ator. Auditoria reutiliza `agro360.audit_logs` (sem senhas/tokens) e não é apagável pela role `agro360_app` quando essa role existe.

No merge commitado incompleto: pecuária operacional (`INDIVIDUAL`/`QUANTITY`, facilities) prevalece sobre o modelo alternativo `COLLECTIVE`/`livestock_locations` no consolidado; colunas aditivas `origin`/`internal_identifier` foram preservadas.

## ADR-E8-01 — Frota: cadastral ≠ operacional ≠ agenda (2026-09-10)

Equipamentos usam um único cadastro (`fleet_assets`) compartilhado com produção/ordens de campo. Situação cadastral (`ACTIVE`/`INACTIVE`/baixa/venda), status operacional (`AVAILABLE`/`MAINTENANCE`/…) e ocupação na agenda (reservas) são conceitos distintos. Placa e medidores só se aplicam quando o tipo os possui.

Bloqueio operacional (`fleet_operational_blocks`) é separado do estado da OS: nem toda solicitação impede uso; OS pausada pode manter indisponibilidade; conclusão/cancelamento da OS libera só o bloqueio daquela OS; liberação do ativo exige ausência de impedimento não dispensável. Inspeção reprovada com falha impeditiva recria/mantém bloqueio.

Leituras preservam valor físico e acumulado operacional; reinicialização é evento explícito (`is_reset`), não redução silenciosa. Planos versionam a política aplicada; alterar plano não reescreve OS existentes. Abastecimento interno baixa estoque uma vez; externo não baixa. Peça removida não retorna como material novo. Custos carregam `origin_type`/`origin_id` para evitar duplicação compra/consumo/pagamento.

Continuidade: antes de offline móvel, garantir reservas, disponibilidade e movimentos confiáveis (logística).

## ADR-E6-01 — Controle do rebanho sem misturar entidades (2026-09-10)

Animal identificado, lote de manejo, localização (pasto/piquete/curral/instalação) e lote de produto/insumo são tabelas distintas. Grupos `livestock_herds.control_mode` são `INDIVIDUAL` (cabeças derivadas dos animais) ou `QUANTITY` (saldo por quantidade). Atribuir animal a grupo coletivo é recusado; a passagem coletiva→individual exige conciliação com a mesma quantidade, sem criar cadastros artificiais.

Saída comercial não usa transferência interna: reserva vigente → confirmação física → obrigação em `commercial_sales` + `finance_commercial_receivables`. Transferência só entre propriedades do mesmo tenant. Protocolos/doses não são inventados; a restrição demonstrativa da Santa Clara está marcada `demo_only` e o texto declara que não é orientação veterinária.

Pesagem coletiva não gera pesos individuais. Correções e estornos preservam a trilha. CSV de exportação prefixa valores que planilhas interpretariam como fórmula.

## ADR-E1-06 — Credenciais demo por comando explícito e MFA confirmado

Fixtures SQL não contêm credenciais utilizáveis. O provisionamento local de
SuperAdmin e Santa Clara é uma operação consciente do Migrator, restrita a
Development/Homologation, com segredos em variáveis locais e confirmação TOTP
antes da ativação. A redefinição revoga sessões e exige troca no primeiro
login; reexecução nunca é efeito colateral de seed ou inicialização.

## ADR-E6-01 — Separação do controle pecuário e detalhe histórico (2026-09-10)

O incremento mantém quatro identidades distintas: animal individual em `livestock_animals`, grupo operacional em `livestock_herds`, localização em `livestock_locations` e lote de insumo no estoque existente. Grupos declaram `COLLECTIVE` ou `INDIVIDUAL`; a passagem para indivíduos exige conciliação confirmada com quantidades iguais, em vez de criar cabeças implicitamente. Movimentos coletivos são eventos imutáveis com motivo, responsável, idempotência e eventual estorno referenciado.

A data de nascimento continua obrigatória no contrato atual, mas pode ser marcada como estimada. Cadastro valida propriedade, grupo e filiação no tenant; pesagem e tratamento anteriores ao nascimento são rejeitados. O detalhe individual agrega fontes históricas sem apagar ou recalcular eventos. Esta fundação não declara implementadas as jornadas de ordens, reserva comercial, alimentação completa ou rateio.

## ADR-E1-07 — Configuração concluída decorre de dados válidos (2026-09-11)

A Central de Implantação é a implementação canônica da configuração guiada do cliente. O checklist manual legado permanece para compatibilidade e observações, mas não é evidência de prontidão. O progresso operacional é calculado no servidor a partir de cadastros ativos e das dependências dos módulos efetivamente contratados. Etapas não pertinentes são opcionais; a revisão final só conclui quando todas as dependências obrigatórias estão satisfeitas. Assim, clicar em avançar não libera operação, e contratar um módulo não expõe automaticamente suas telas ou permissões.

## ADR-E10-01 — Pendência operacional é uma projeção, não um checklist paralelo (2026-09-11)

A Central de Operações deriva ocorrências das operações de origem e persiste separadamente apenas visualização e atribuição. Não existe transição manual para resolvido. A chave estável `TIPO:id` evita duplicação; quando o pré-requisito é atendido no módulo responsável, a projeção deixa de retorná-la. A consulta aplica tenant e permissões antes da paginação.

| Estado atual | Ação | Pré-requisitos | Permissão | Próximo estado | Efeito em outros módulos | Reversão |
|---|---|---|---|---|---|---|
| NEW | visualizar | ocorrência ainda derivável | `work.read` + leitura da origem | VIEWED | nenhum | não necessária; evento é histórico |
| NEW/VIEWED | atribuir | usuário ativo no mesmo tenant | `work.write` + leitura da origem | ASSIGNED | nenhum efeito na operação original | nova atribuição auditada |
| NEW/VIEWED/ASSIGNED | abrir origem | link interno conhecido | leitura da origem | inalterado | usuário executa a transição no serviço dono | conforme matriz do módulo |
| qualquer interação | resolver causa | pré-requisitos da operação original | permissão específica da ação | item deixa a projeção | somente o serviço dono grava efeitos | conforme eventos/ajustes do módulo |

A Central nunca aprova, recebe, inspeciona, expede, entrega ou baixa título por inferência. Essas transições continuam centralizadas nos respectivos serviços transacionais.
