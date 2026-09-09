# Plano de execução Agro360

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
| AG-E0-003 / P0 | Operação: caminho incremental canônico com dados anteriores | preservar checksums; bridges anteriores às migrations publicadas; baseline canônico registrado; bloquear base legada populada antes de qualquer materialização não homologada | **Parcial**: cadeia limpa e reexecução chegam à versão atual; `006a`, bridges 029/036a/040a/042a/046a/048a e baseline foram executados em banco descartável. Upgrade com dados legados permanece bloqueado deliberadamente até existir conversão e comparação de dados |
| AG-E0-004 / P1 | Consolidação do checkout com `origin/main` `2b831654` e trabalho local | preservar autoria/diffs; reaplicar gates após última alteração; remover somente controles documentais duplicados | **Implementado não validado até o commit final**: merge resolvido no ramo `codex/consolidar-avanco-compras`; gates finais e revisão de diff são o aceite |
| AG-E1-001 / P0 | Pessoa e administradores: identidade global, vínculo e seleção do cliente | identidade/vínculo/organização ativos; CNPJ resolve organização, não pessoa; e-mail/CPF; bloqueio do vínculo isolado; queries/FKs/contexto autorizado | E0 estabilizada. Dois clientes, operador restrito, URL/API/exports negados; código atual é tenant-first |
| AG-E1-002 / P0 | MNSOFT: SuperAdmin único, MFA e suporte assistido | unicidade concorrente, TOTP real, elevação, motivo/início/fim/ator real; último acesso protegido | **Parcial**: TOTP e provisionamento por segredo existem; acesso assistido completo e chaves persistentes de Data Protection ainda exigem homologação |
| AG-E1-003 / P0 | Administração cliente: usuários, perfis, convites e revogação | lock por tenant; limite central em criação/aceite/reativação; autoridade delegável revalidada no aceite; sessões revogadas; último administrador protegido | **Implementado não validado no recorte concorrente**: serviço e UI integrados; falta executar os cenários simultâneos e multi-tenant da matriz |
| AG-E1-004 / P0 | Operação: provisionamento seguro e demo opt-in | senha local aleatória/ambiente, hash, troca inicial, sem reset em reexecução; produção sem demo | Corrigir bootstrap fixo e documentação histórica; não reescrever migrations publicadas nem invalidar contas existentes |
| AG-E2-001 / P1 | Cliente/MNSOFT: catálogo → preço/pacote → aceite → contrato | decisão compartilhada considera assinatura/vigência, legado, entitlement, marketplace, override, bloqueios e dependências; conta suspensa mantém somente rotas `account.*` de regularização | **Parcial**: API e emissão de token usam `AccessDecision`; exports passam pelas policies. Worker/jobs e fluxo comercial de aceite/preço ainda precisam usar e homologar a mesma decisão |
| AG-E3-001 / P1 | MNSOFT/cliente: cobrança interna → conciliação → suspensão/regularização | pendente não é pago; retries/jobs idempotentes; exceções auditadas; separar financeiro rural | E2. Sem provedor não produzir PIX/boleto/split fictício; `ControlledPaymentSplitProvider` não é pagamento real |
| AG-E4-001 / P1 | Operador: cadastros estruturantes e estoque | fazenda/unidade/talhão/safra/parceiro/produto/depósito/lote; unidade coerente, saldo/reserva, compensação e concorrência | E1/E2; preservar trabalho concorrente de propriedades. Entrada → reserva → baixa → transferência → ajuste conciliados |
| AG-E5-001 / P1 | Comprador/qualidade/financeiro | `procurement_*` é o modelo autoritativo da jornada nova; pedido → recebimento parcial/total → quarentena ou estoque → previsão; excesso, inspeção e cancelamento possuem permissões próprias | **Implementado não homologado**: transação, locks, payload idempotente, soma duplicada, lote/unidade, parcelas, divergência, compensação e UI estão no código/SQL; faltam cenários E2E específicos de compras e plano de leitura/migração do legado `purchasing_*` |
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
