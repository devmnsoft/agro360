# Decisões de execução

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

## ADR-E0-06 — Baseline canônico e bridges imutáveis

Em 2026-09-09, o gate incremental foi reexecutado desde banco vazio. A ponte `006a` resolveu `due_on`, mas a sequência revelou contratos históricos ausentes antes das migrations 030, 037, 041, 043, 047 e 049. Foram acrescentadas bridges ordenadas antes de cada migration publicada; os bytes/checksums das migrations publicadas não foram alterados.

Quando `agro360.tenancy_tenants` não existe e a base histórica ainda está vazia, o Migrator materializa o instalador textual como baseline, registra nome e checksum em `platform_schema_migrations` e continua a cadeia. Se `tenancy.tenants` já contém qualquer cliente, o Migrator interrompe antes do baseline: criar tabelas canônicas vazias e declarar sucesso perderia a visibilidade dos dados legados. A conversão de base populada continua sendo um gate separado, em cópia descartável, com mapeamento e comparação de dados.

## ADR-E1-06 — Uma decisão de acesso, negação prevalece

`AccessDecision` concentra a resolução de módulos usada pela emissão de token e pela autorização de request. Assinatura vigente, concessões legadas e marketplace formam candidatos; bloqueio/suspensão/inadimplência, entitlement negado, módulo marketplace bloqueado e feature override desabilitado prevalecem. Dependências do catálogo também são exigidas. Claims continuam sendo o teto da sessão e a consulta atual ao banco permite que revogação, suspensão ou mudança de perfil retire acesso antes da expiração; novas concessões exigem renovar a sessão.

Rotas `account.*` permanecem acessíveis ao vínculo autorizado para regularização e suporte, mas não liberam módulos operacionais. Jobs ainda precisam adotar o mesmo componente antes de AG-E2-001 ser promovida.

## ADR-E5-01 — Recebimento autoritativo e efeitos compensatórios

Para a jornada entregue, `agro360.procurement_*` é o modelo autoritativo. `purchasing_*` é legado compatível e não deve receber novas escritas desta jornada até existir migração/leitura de transição explícita; nenhum dado foi apagado.

O recebimento possui idempotência por tenant/chave/payload, locks em ordem, validação acumulada e permissão específica para excesso. Material inspecionável fica em `procurement_receipt_quarantines` sem saldo disponível; a decisão de qualidade cria a movimentação somente ao liberar. Serviços não criam estoque. Previsões financeiras permanecem abertas e parcelas fecham o total em centavos. Cancelamento preserva históricos, cria movimentos compensatórios e é bloqueado se a obrigação já foi paga/alterada; toda decisão exige motivo e auditoria.
