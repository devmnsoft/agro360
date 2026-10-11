# Instruções de homologação visual autenticada — Compras (incremento 2026-10-10)

Procedimento executável para concluir a matriz de homologação visual que ficou bloqueada por degradação do ambiente dev no fecho do incremento 2026-10-10 (diagnóstico completo em `docs/EXECUTION-CHECKPOINT.md`). A camada de API/E2E da jornada já está verde (`artifacts/procurement-journey-e2e-4e570923d65a468fb12bb97f1ec0414a`); este documento cobre apenas a **confirmação visual autenticada**.

## Estado do ambiente em 2026-10-11 (continuação da rodada)

Descobertas que alteraram o plano de v5:

1. **Túnel do navegador**: o tráfego web da ferramenta de navegador passa por um proxy cujo `localhost` **não** é o loopback desta máquina (`https://localhost:7080` → `ERR_TUNNEL_CONNECTION_FAILED`; `https://example.com` ✓; `https://192.168.0.10:7080` alcançou a máquina — `ERR_SSL_PROTOCOL_ERROR` contra servidor plain-HTTP). Conclusão prática: navegar **sempre por `https://192.168.0.10:<porta>`** (ou IP da LAN), nunca `localhost`.
2. **Geração de hosts v6** (tarefas `Agro360HostApi6`/`Agro360HostWeb6`, bats `launch-api6.bat`/`launch-web6.bat`): API `https://0.0.0.0:7081;http://0.0.0.0:8081`, Web `https://0.0.0.0:7080;http://0.0.0.0:7083`, `ApiBaseUrl=https://192.168.0.10:7081`, CORS inclui `https://192.168.0.10:7080`. TLS terminado com certificado self-signed **SAN = localhost + 192.168.0.10** (`agro360-dev-lan.pfx`, thumbprint `C05DA1CAA96A04FDF9EB850D2281AB150531747F`, senha `agro360dev`) — se o desktop rejeitar (`ERR_CERT_*`), pedir ao usuário para confiar no certificado uma vez; não contornar.
3. **Dev DB divergente**: a dev DB do usuário está em `11.24.0` mas **falta a tabela `finance_commercial_receivables`** (ausente em todos os schemas) → `/health` responde **503 Unhealthy** (`DatabaseHealthCheck` valida 22 tabelas/colunas). Divergência pré-existente do snapshot original da dev DB, não regressão deste incremento. Decisão: homologação visual rodando contra **banco descartável `agro360_homolog`** criado limpo do `database/agro360-postgres-full.sql` atual (mesmo caminho do E2E verde); dev DB do usuário intacta.
4. Subida dos hosts v6 sob RAM degradada: ~30–40 min até "Now listening on"; logs em `Temp\opencode\hosts\api6.out.log`/`web6.out.log`.
5. **Ajuste de seed no banco descartável (documentado)**: no seed limpo do full.sql, `admin@santaclara.agro360.local` nasce com status `INVITED`; foi ativado (`status='ACTIVE'`, 1 linha) para viabilizar o login de aprovação do fluxo SoD. O outro admin seed (`admin.santaclara@...`) permanece `INVITED`/inalterado. Unidades: `geo_farms.code`=1 "Fazenda Santa Clara - Sede" / 2 "Retiro Boa Vista". Tabelas de Compras: catálogo é `procurement_item_catalog` (38 tabelas `procurement_*`).

## Pré-requisitos

1. Hosts no ar via **tarefas agendadas** (desacopladas do harness — jobs de background do agente são cancelados em restart do servidor e matam os hosts junto, o que matou as gerações v4): `schtasks /Create /TN "Agro360HostApi5" /TR "C:\Users\NCELL-DEV-020\AppData\Local\Temp\opencode\launch-api5.bat" /SC ONCE /ST 00:00 /F` + `schtasks /Run` (idem `Agro360HostWeb5` com `launch-web5.bat`; poller `Agro360HostPoll6` com `ready-poll6.bat`). API: `https://localhost:7081` + `http://0.0.0.0:8081`; Web: `https://localhost:7080` + `http://0.0.0.0:7080`; CORS do API ampliado por variáveis de ambiente para as origens de página (inclui `http://localhost:7080`). Confirmação: `GET http://localhost:8081/health` e portas em `netstat` (poller `READY6.txt` sai em BOTH-UP). O front resolve a API pelo `<meta name="api-base">` (default `http://localhost:8081`, configurável por `ApiBaseUrl` sem rebuild): navegador local usa o default (loopback); se o navegador estiver em outra máquina da LAN, relançar o Web com `ApiBaseUrl=http://192.168.0.10:8081` e navegar em `http://192.168.0.10:7080`. Sob RAM esgotada a subida leva 25–30 min+; reinício da máquina é o alívio recomendado. Dados: o tenant `santa-clara` começa **vazio** em Compras (0 fornecedores/catálogo/requisições) — criar tudo pela UI (os formulários preenchidos viram evidência).
2. Credenciais dev (tenant `santa-clara`; confirmar empiricamente no login):
   - admin: `admin@santaclara.agro360.local` / `Agro360Cliente!!`
   - operador: `operador.santaclara@agro360.local` / `Agro360Operador!`
3. Dev DB em schema `11.24.0` (migrada de 9.8.0 em 2026-10-10 com backup). Template de jornada em cluster próprio continua disponível para repetir a camada de API se necessário.
4. Artefatos em `artifacts/browser-homologacao-<data>/` (criar na data da execução).

## Matriz de execução

- **Idiomas (4):** pt-BR, es-ES, en-US, fr-FR — alterados pelo seletor de cultura do shell; persistência confirmada por reload (preferência deve voltar igual; feedback de erro se o servidor negar).
- **Larguras (3):** 360 px, 768 px, 1440 px + **zoom nativo 200%** (uma amostra representativa). A ferramenta de navegador não expõe parâmetro de viewport: tentar `window.resizeTo` via `evaluate` e **registrar o valor efetivamente alcançado** — anotar o limite honesto em vez de declarar a largura como simulada quando não for.
- **Fluxo SoD pela UI (obrigatório):** operador cria (requisição, recebimento/devolução); admin aprova/decide (alçada/separação de funções). A mesma conta não cria e aprova o que exige SoD.
- **Jornada completa pelo navegador:** login → navegação/menu (somente módulos contratados + permissões + escopo da unidade) → requisição → aprovação → cotação (≥2 participantes) → propostas → comparação (verificar os dois rótulos do critério nos 4 idiomas) → decisão (incluindo escolha acima do menor com justificativa ≥3 caracteres) → conversão (capturar o diálogo/contrato 409 sem `confirmLowestPrice`) → pedido → aprovação → recebimento parcial → decisão de qualidade → devolução ao fornecedor (registro + aprovação por outro usuário) → conferência documental → estado persistido após reload.
- **Palavra de comando:** mesmas regras de menu (módulo/permissão/escopo); testar uma recusa visível (item indisponível para o operador).

## Evidências obrigatórias (por idioma amostrado)

- Screenshots de **formulários preenchidos**, **mensagens de erro/validation**, **tela de comparação**, **diálogos de confirmação** (incluindo o 409 de menor preço) e **estado persistido após reload**.
- Console sem erros novos e rede sem falhas (4xx/5xx inesperados) nas páginas visitadas; capturar exceções.
- Registro dos valores reais de viewport/zoom alcançados.

## Critérios de resultado

| Resultado | Condição |
|---|---|
| **Aprovado** | Matriz completa com evidências acima nos 4 idiomas e sem defeito aberto de UI. |
| **Bloqueado** | Ambiente impede parte da matriz (documentar exatamente qual célula ficou sem evidência). |
| **Falhou** | Defeito reproduzido com screenshot + console/rede (abrir correção antes de aprovar). |
| **Não executado** | Item da matriz não coberto por limite da ferramenta (ex.: zoom nativo não alcançável) — registrar a limitação honesta. |

Não declarar Compras/SaaS visualmente homologados enquanto qualquer célula obrigatória ficar sem evidência; atualizar em seguida `EXECUTION-CHECKPOINT.md` (seção de resultados), `I18N-COVERAGE-REPORT.md`, `TRACEABILITY-MATRIX-v0.2.0.md` e `PROMPT-PROXIMA-EXECUCAO.md`.
