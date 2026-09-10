# Plano de execução Agro360

> Atualização 2026-09-10: o reparo de código de AG-E0-003 foi implementado por migrations aditivas `006z`/`007z`, preservando 001/007. O gate PostgreSQL de instalação incremental continua obrigatório. Também foi alinhado o ID canônico `...003` da conta Santa Clara no provisionador. Após o gate, o próximo bloco é AG-E1-002 (MFA e assistência auditada), sem promover jornadas parciais por presença de camadas.

> Atualização de 2026-09-10: o próximo gate é identidade/SaaS em PostgreSQL real (migration 067, provisionamento PowerShell/Bash, MFA após reinício, login/refresh e isolamento). Depois vêm quarentena/liberação/reversão de compras; somente então comercial e produção. Estados e bloqueios ficam nos documentos canônicos `../EXECUTION-CHECKPOINT.md` e `../TRACEABILITY-MATRIX-v0.2.0.md`.

Fonte aprovada: [plano mestre integral](AGRO360-MASTER-PLAN.md), incorporado em 2026-09-08. Requisitos não são evidências de implementação. Não reutilizar regras, namespaces ou diagnósticos do SIGOV-PLUS.

## Controles canônicos (sem duplicação)

- Matriz de funcionalidades: [TRACEABILITY-MATRIX-v0.2.0.md](../TRACEABILITY-MATRIX-v0.2.0.md), seção de consolidação atual; a tabela original é histórica.
- Checkpoint: [EXECUTION-CHECKPOINT.md](../EXECUTION-CHECKPOINT.md).
- Decisões: [DECISIONS.md](DECISIONS.md).
- Histórico de produto: [ROADMAP](../ROADMAP.md) e [SPRINTS](../SPRINTS.md), sem equivalência automática entre sprint e etapa E0–E10.

## Ordem e escopo desta versão

Concluir os fluxos existentes e os requisitos E0–E10 do mestre. Não acrescentar novos setores/provedores à versão sem decisão explícita. As linhas abaixo são entregas verificáveis; um módulo não é vendável porque possui controller ou migration. Cada ID mantém sua identidade nas próximas execuções.

| ID / prioridade | Entrega, atores e pré-condições | Estados, validações e efeitos | Dependência / aceite |
|---|---|---|---|
| AG-E0-001 / P0 | Operação: readiness real, API/Web, login no PostgreSQL | vazio/incompleto → 503; instalado → 200; leitura de metadados, sem DDL no health; liveness separado | `verify-e0.ps1`: instalação/reexecução, OpenAPI, login, dashboard, refresh/replay/logout e testes. Implementado e exercitado nesta rodada |
| AG-E0-002 / P0 | Usuário na tela de acesso: diagnóstico sem falso sucesso | rede falha → erro; health 200 exige `Healthy`; Swagger exige JSON OpenAPI; cache só shell público local; remover cache lookup legado | API indisponível nunca pode virar HTML 200. Regressão observada e corrigida no navegador |
| AG-E0-003 / P0 | Operação: caminho incremental canônico com dados anteriores | preservar checksums; mapear schemas legados → `agro360`; definir baseline sem registrar migration não executada | **Próxima entrega / defeito executado**: migration 007 falha com 42703 (`due_on` ausente; 001 define `due_date`). `-CheckMigrations` reproduz em banco independente. Não aplicar migrations em banco real por suposição |
| AG-E0-004 / P1 | Consolidação das alterações concorrentes | preservar autoria/diffs; reaplicar gates após última alteração; nenhum `git add .` | Não incluir automaticamente propriedades, SaaS, novas migrations/classes de teste produzidas por outra execução |
| AG-E1-001 / P0 | Pessoa e administradores: identidade global, vínculo e seleção do cliente | identidade/vínculo/organização ativos; CNPJ resolve organização, não pessoa; e-mail/CPF; bloqueio do vínculo isolado; queries/FKs/contexto autorizado | E0 estabilizada. Dois clientes, operador restrito, URL/API/exports negados; código atual é tenant-first |
| AG-E1-002 / P0 | MNSOFT: SuperAdmin único, MFA e suporte assistido | unicidade concorrente, MFA real, elevação, motivo/início/fim/ator real; último acesso protegido | AG-E1-001. Nenhum desafio MFA foi localizado; não considerar a coluna `mfa_enabled` implementação |
| AG-E1-003 / P0 | Administração cliente: usuários, perfis, convites e revogação | sem autoelevação/papel global/perfil estrangeiro; efeitos imediatos nos acessos; limites e último administrador | AG-E1-001. Há implementação concorrente de usuários: revisar, não duplicar; autorização de perfis/convites ainda exige auditoria |
| AG-E1-004 / P0 | Operação: provisionamento seguro e demo opt-in | senha local/entrada protegida, hash real, MFA confirmado, troca inicial, revogação e auditoria; produção sem demo | **Implementado, aguardando homologação PostgreSQL:** comando explícito valida fixtures e não roda em startup/seed. Executar login real antes de promover a validado |
| AG-E2-001 / P1 | Cliente/MNSOFT: catálogo → preço/pacote → aceite → contrato | decimal, snapshot, dependências, limites, vigência; contrato não equivale a permissão | E1. Unificar os três caminhos de entitlement hoje lidos no login; contrato antigo preserva preço |
| AG-E3-001 / P1 | MNSOFT/cliente: cobrança interna → conciliação → suspensão/regularização | pendente não é pago; retries/jobs idempotentes; exceções auditadas; separar financeiro rural | E2. Sem provedor não produzir PIX/boleto/split fictício; `ControlledPaymentSplitProvider` não é pagamento real |
| AG-E4-001 / P1 | Operador: cadastros estruturantes e estoque | fazenda/unidade/talhão/safra/parceiro/produto/depósito/lote; unidade coerente, saldo/reserva, compensação e concorrência | E1/E2; preservar trabalho concorrente de propriedades. Entrada → reserva → baixa → transferência → ajuste conciliados |
| AG-E5-001 / P1 | Comprador/vendedor/financeiro | cotação/alçada/pedido/recebimento parcial → estoque/título; proposta/reserva/entrega/recebível; baixa/estorno sem duplicação | E4. Unificar fornecedor `purchasing` vs `procurement`; validar rollback e segregação, não apenas CRUD |
| AG-E6-001 / P1 | Agrônomo/operador pecuário | planejamento → apontamento → consumo/custo/resultado; manejo/sanidade/reprodução com histórico; protocolos regulados não inventados | E4/E5. Agricultura, pecuária, leite/pastagens e especialidades já presentes; telas e regras devem ser exercitadas |
| AG-E7-001 / P1 | Produção/qualidade | receita versionada/aprovada → ordem → consumo → inspeção → lote; reprovação bloqueia expedição | E4/E5. Rastrear origem/destino e falha transacional sem divergência de estoque |
| AG-E8-001 / P1 | Logística/frota/fiscal | expedição parcial/entrega/prova; peças/manutenção/custos; documentos autorizados; fiscal solicitado ≠ autorizado | E4/E5/E7. Provedor fiscal não configurado permanece bloqueado; não emitir documento real na homologação |
| AG-E9-001 / P1 | Campo/analista/integrações | fila por tenant/dispositivo, reautorização, conflito/replay; CSV seguro, pt-BR/en/es, BI com fonte | E1 e fluxos de origem. Cache público E0 não entrega bootstrap offline seguro; projetar cache por identidade antes de reativar dados offline |
| AG-E10-001 / P1 | Operação e donos de módulos | suporte, RH/SST, portal, workflow/notificações, cooperativas, ESG, trading, IoT e BI; carga, acessibilidade e restore | Matriz inteira com critérios demonstrados ou retirada formal do escopo; jamais declarar sistema todo revisado por busca de arquivos |

## Como verificar e avançar

1. Registrar `git status --short`, branch/HEAD e diferenças desde o checkpoint; confirmar `origin` Agro360.
2. Executar `pwsh -File scripts/verify-e0.ps1 -PostgresBin 'C:\Program Files\PostgreSQL\18\bin'`. O script não usa o banco local configurado; cria cluster protegido e banco novos em `artifacts/` e encerra somente seus processos.
3. Para AG-E0-003, executar o migrator contra **outro banco descartável** com `ConnectionStrings__Agro360` e `--migrations database/migrations`; comparar objetos e dados com o instalador. Nunca usar a base do usuário para descobrir se a consolidação funciona.
4. Implementar uma entrega por vez, com Dapper/SQL, regras, autorização, endpoint, UI e ajuda onde aplicável; repetir cenários positivos, negativos e concorrentes.
5. Atualizar matriz e checkpoint citando ambiente/comandos, sem publicar segredo, hash, token ou capturas/binários.

Não houve autorização de push nesta rodada. A inclusão literal do mestre não autoriza infraestrutura de produção, comunicações ou cobranças externas.

## Próximo recorte após merge do PR #96 — 2026-09-09

- AG-E5-001/AG-E5-002: homologar em PostgreSQL descartável o recebimento integrado preservado do commit local e o pedido comercial com preço-base, desconto efetivo, snapshot e arredondamento por linha. Em seguida concluir reserva → entrega → recebível sem mudança fictícia de status.
- AG-E7-001: modelar o snapshot versionado do roteiro receita → etapas esperadas → ordem, gerar lote acabado operacional e validar consumo/reserva/qualidade com bloqueios efetivos.
- UI: completar jornada comercial de tabela de preços → cliente → pedido → análise/aprovação e exercitar a interação no navegador.
