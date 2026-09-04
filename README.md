# MNSOFT Agro 360

> **Sprint 31:** **Inteligência Agro360** entrega recomendações rastreáveis, scores, anomalias, Prioridades do Dia e assistente interno sem exigir IA externa. Veja [a documentação operacional](docs/INTELIGENCIA-AGRO360.md).

Plataforma modular e multi-tenant para gestão do agronegócio, em .NET 10 e PostgreSQL/PostGIS. **A execução principal é nativa e não requer Docker.** Docker Compose permanece somente como conveniência opcional.

## Início rápido sem Docker

### Pré-requisitos

- .NET SDK 10 (a versão é fixada em `global.json`);
- PostgreSQL 14 ou superior, local, remoto ou gerenciado;
- PostGIS e as extensões `pgcrypto`, `pg_trgm` e `unaccent` disponíveis;
- PostgreSQL client tools (`psql`, `pg_dump`, `pg_restore`) para instalação manual e manutenção.

Prepare um servidor externo (os nomes são exemplos; a senha deve ser digitada com segurança pelo administrador):

```bash
createuser --host localhost --port 5432 --pwprompt agro360_app
createdb --host localhost --port 5432 --owner agro360_app agro360
psql --host localhost --port 5432 --dbname agro360 --file database/bootstrap/003-enable-extensions.sql
export ConnectionStrings__Agro360='Host=localhost;Port=5432;Database=agro360;Username=agro360_app;Password=ALTERAR;Pooling=true;Timeout=15;Command Timeout=30'
```

Não versione essa variável. Em desenvolvimento também é possível usar User Secrets:

```bash
dotnet user-secrets --project src/Hosts/Agro360.Api set ConnectionStrings:Agro360 'SUA_CONEXAO'
dotnet user-secrets --project src/Hosts/Agro360.Migrator set ConnectionStrings:Agro360 'SUA_CONEXAO'
```

Um `appsettings.Development.json` local ignorado pelo Git ou o secret manager da plataforma de produção são igualmente suportados. API, Worker e Migrator usam a chave única `ConnectionStrings:Agro360`; nenhum host de container é assumido.

Execute a preparação e, depois, os hosts:

```bash
./scripts/setup-local.sh
./scripts/migrate.sh migrate
./scripts/run-api.sh       # terminais separados
./scripts/run-worker.sh
./scripts/run-web.sh
# PowerShell: use os scripts .ps1 equivalentes
```

O script de execução inicia API, Worker e Web e encerra todos ao receber `Ctrl+C`. A sequência equivalente, sem scripts, é:

```bash
dotnet restore MNSOFT.Agro360.sln
dotnet build MNSOFT.Agro360.sln --configuration Release
dotnet run --project src/Hosts/Agro360.Migrator -- migrate
dotnet run --project src/Hosts/Agro360.Api
dotnet run --project src/Hosts/Agro360.Worker
dotnet run --project src/Hosts/Agro360.Web
```

### Portas da API em desenvolvimento

O único perfil de inicialização da API, `Agro360.Api`, usa
`http://localhost:5046` e `https://localhost:7046`. O Visual Studio e o comando
`dotnet run --project src/Hosts/Agro360.Api/Agro360.Api.csproj` usam esse perfil
por padrão. O documento OpenAPI fica disponível em
`https://localhost:7046/openapi/v1.json` durante o desenvolvimento.

Para trocar a porta apenas na execução atual, sem alterar arquivos versionados,
informe `--urls` (esse argumento prevalece sobre o perfil):

```bash
dotnet run --project src/Hosts/Agro360.Api/Agro360.Api.csproj --urls "http://localhost:5046"
```

No Windows, se uma porta estiver ocupada, identifique o processo antes de
encerrá-lo:

```powershell
Get-NetTCPConnection -LocalPort 5046 | Select-Object LocalAddress,LocalPort,State,OwningProcess
Get-Process -Id <PID>
# Somente se for uma instância antiga ou presa do Agro360:
Stop-Process -Id <PID> -Force
```

Em CMD, use `netstat -ano | findstr :5046` e depois, somente após conferir o
processo, `taskkill /PID <PID> /F`. Evite iniciar simultaneamente a API pelo
Visual Studio e pelo terminal; pare a instância anterior antes de iniciar outra.

## Instalação do banco

O instalador canônico da release candidate pode ser aplicado diretamente pela mesma connection string usada pela aplicação:

```bash
export ConnectionStrings__Agro360="Host=localhost;Port=5432;Database=agro360;Username=postgres;Password=postgres"
psql "$ConnectionStrings__Agro360" -f database/agro360-postgres-full.sql
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Hosts/Agro360.Api
```

Os valores são apenas exemplos locais. Em pgAdmin/DBeaver, abra e execute o mesmo arquivo inteiro; ele não seleciona banco, usuário ou host e não inclui outros arquivos.

### Homologação guiada local

O instalador completo é idempotente e inclui o cliente interno **Fazenda Santa Clara** no plano Profissional. Para validar autenticação real no banco, use o tenant `santa-clara`, o usuário `admin@santaclara.agro360.local` e a senha inicial `SantaClara@2026!`; a troca é obrigatória no primeiro acesso. O acesso global usa o tenant `agro360-platform`, o usuário `superadmin@mnsoft.com.br` e a senha inicial `MNSoft@Agro360#2026`. Essas credenciais e os documentos matematicamente válidos do seed são exclusivamente locais e não devem existir em produção.

Depois de aplicar o SQL, inicie API e Web, abra `/swagger` somente em Development e execute o login. A navegação é derivada das permissões devolvidas pela API; ocultar um item no cliente não substitui a autorização do endpoint.

### A — Migrator (recomendado)

```bash
dotnet run --project src/Hosts/Agro360.Migrator -- status
dotnet run --project src/Hosts/Agro360.Migrator -- validate
dotnet run --project src/Hosts/Agro360.Migrator -- migrate
dotnet run --project src/Hosts/Agro360.Migrator -- seed minimal
dotnet run --project src/Hosts/Agro360.Migrator -- seed demo # nunca em produção
# migrations externas: --migrations /caminho/fornecido
```

O Migrator usa lock consultivo, checksum, histórico e uma transação por migration. Um checksum alterado ou PostGIS indisponível gera erro específico e exit code não zero.

### B — SQL consolidado

Em um **banco vazio**:

```bash
psql --host localhost --port 5432 --username agro360_app --dbname agro360 --set=ON_ERROR_STOP=1 --file database/releases/v0.2.0/agro360-v0.2.0-full-install.sql
```

### C — pgAdmin, DBeaver ou equivalente

Conecte ao banco de destino, abra o editor SQL, carregue `database/releases/v0.2.0/agro360-v0.2.0-full-install.sql` e execute o script completo. Ele não usa `\i`, proprietário, senha, banco fixo ou caminhos externos. Consulte `database/README.md` para a organização do pacote.

## Testes nativos com PostgreSQL externo

Use somente banco descartável cujo nome contenha `test` ou `teste`; o script recusa outro destino:

```bash
export AGRO360_TEST_CONNECTION_STRING='Host=localhost;Port=5432;Database=agro360_test;Username=agro360_app;Password=ALTERAR'
./scripts/test-local.sh
# PowerShell: $env:AGRO360_TEST_CONNECTION_STRING='...'; ./scripts/test-local.ps1
```

As migrations reais são aplicadas antes da suíte. O banco informado é responsabilidade do operador e nunca deve ser o de produção.

## Backup e restauração sem containers

A autenticação deve usar prompt, `PGPASSWORD` temporário ou, preferencialmente, `.pgpass`/`pgpass.conf` protegido; os scripts não recebem nem imprimem senha:

```bash
export PGHOST=localhost PGPORT=5432 PGUSER=agro360_app PGDATABASE=agro360
./database/maintenance/backup.sh agro360
createdb --host "$PGHOST" --port "$PGPORT" --username "$PGUSER" agro360_restore
PGDATABASE=agro360_restore ./database/maintenance/restore.sh agro360.backup
PGDATABASE=agro360_restore ./database/maintenance/restore.sh agro360.sql
```

## Publicação nativa

Os artefatos incluem documentação, scripts, migrations e instalador consolidado:

```bash
for host in Agro360.Migrator Agro360.Api Agro360.Worker Agro360.Web; do
  dotnet publish "src/Hosts/$host" --configuration Release --output "artifacts/$host"
done
```

## Docker Compose (alternativa opcional)

Somente se Docker estiver disponível:

```bash
cp .env.example .env       # substitua ALTERAR localmente; não versione .env
docker compose up --build
```

O Compose usa `postgres` apenas dentro de sua configuração opcional; aplicações nativas nunca dependem desse hostname. Web: `http://localhost:8080`; API: `http://localhost:8081`.

## Estrutura e segurança

Os hosts ficam em `src/Hosts`, módulos em `src/Modules`, testes em `tests` e SQL físico em `database`. RLS protege dados tenant; o usuário da aplicação não deve ser proprietário do banco. Nunca mantenha credenciais em JSON versionado, logs, scripts ou linha de comando compartilhada.

## Guias operacionais

- [Primeiros passos sem Docker](docs/getting-started-without-docker.md)
- [Instalação, backup e restauração do banco](docs/database-installation.md)
- [Arquitetura](docs/architecture.md)
- [Testes](docs/testing.md)
- [Release v0.2.0](docs/release-v0.2.0.md)

## Sprint 6 — operação completa

Fornecedores, compras/recebimentos, estoque/insumos, frota, manutenção, abastecimento e dashboard operacional estão disponíveis na API e no Command Center. A instalação PostgreSQL portátil recomendada é:

```bash
psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql
```

Veja [execução nativa](docs/getting-started-without-docker.md), [instalação do banco](docs/database-installation.md) e [guia dos módulos](docs/sprint-6-operational-modules.md).

## Sprint 7 — Pecuária 360 (v0.4.0)

Rebanho, lotes, pastagens/piquetes, manejo, sanidade, reprodução, nutrição, leite, ganho de peso e dashboard zootécnico estão disponíveis ponta a ponta. Instale todo o banco, sem Docker, com:

```bash
psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql
```

Consulte o [guia da Sprint 7](docs/sprint-7-pecuaria-360.md), a [instalação](docs/database-installation.md) e a [execução nativa](docs/getting-started-without-docker.md).

## Sprint 8 — Financeiro Agro

Plano de contas, centros de custo, pagar/receber, comercialização, baixas, fluxo de caixa, resultados dimensionais e dashboard econômico estão disponíveis na v0.5.0. Consulte [o guia da Sprint 8](docs/sprint-8-financeiro-agro.md) e instale todo o banco com `psql "$ConnectionStrings__Agro360" -f database/agro360-postgres-full.sql`.

## Sprint 9 — Armazenagem e logística

A v0.6.0 entrega estruturas/silos, romaneio e pesagem, qualidade e descontos, lotes rastreáveis, secagem/beneficiamento, expedição, contratos operacionais, viagens/fretes e dashboard consolidado. Consulte [o guia operacional](docs/sprint-9-armazenagem-logistica.md).

## Sprint 10 — Rastreabilidade Amazônica (v0.7.0)

Rastreabilidade ponta a ponta, beneficiamento configurável, ledger imutável, certificados públicos, logística fluvial/vicinal e força de vendas com split auditável estão integrados à API e ao Command Center. Instale o banco completo com `psql "$ConnectionStrings__Agro360" -f database/agro360-postgres-full.sql` e consulte o [guia da Sprint 10](docs/sprint-10-rastreabilidade-amazonica.md).

## Sprint 11 — Agricultura 360

O sistema inclui caderno de campo, planejamento, fitossanidade, recomendações/aplicações, irrigação, clima, ordens de serviço, dashboard e lookups pesquisáveis multi-tenant. Consulte [a documentação da sprint](docs/sprint-11-agricultura-360.md) e [o padrão de formulários](docs/form-validation-and-lookups.md). A instalação standalone usa `database/agro360-postgres-full.sql` e não requer Docker.

## Sprint 12 — Campo Mobile/Offline

Execute sem Docker configurando `ConnectionStrings__Agro360`, aplique `database/agro360-postgres-full.sql`, inicie API e Web com `dotnet run --project`. Abra `/field` para dashboard, registros rápidos, evidências, QR, checklists e fila offline. Consulte [guia da sprint](docs/sprint-12-mobile-offline.md), [PWA](docs/pwa-mobile.md) e [sincronização](docs/offline-sync.md).

## Inteligência Agro — Sprint 13

A rota `/intelligence` oferece BI, 23 relatórios com CSV, alertas, painel executivo, previsões determinísticas, assistente baseado no banco e dashboards personalizados. A instalação continua independente de Docker: configure `ConnectionStrings__Agro360`, execute `database/agro360-postgres-full.sql` com `psql` e use `scripts/run-local.sh`. Consulte [a documentação da Sprint 13](docs/sprint-13-inteligencia-agro.md).

## Sprint 14 — operação SaaS

A governança comercial está disponível em `/saas`, com administração de organizações, onboarding, planos e limites, RBAC, convites, segurança, notificações, configurações, conta do cliente e dashboard. A API lê PostgreSQL de `ConnectionStrings__Agro360` e usa Dapper. A instalação completa, sem Docker, é `psql "$ConnectionStrings__Agro360" -f database/agro360-postgres-full.sql`; consulte [governança SaaS](docs/sprint-14-saas-governance.md) e [instalação](docs/database-installation.md).

## Sprint 15 — Compliance Agro e ESG

A aplicação inclui gestão multitenant de documentos regulatórios, regras por produto/mercado, certificações, auditorias de cadeia, não conformidades, indicadores ESG, inventário de carbono e dossiê público de exportação. A interface está em `/compliance` e os endpoints em `/api/compliance` e `/api/esg`. Consulte [a documentação da Sprint 15](docs/sprint-15-compliance-esg.md).

A instalação continua sem Docker: configure `ConnectionStrings__Agro360`, aplique `psql "$ConnectionStrings__Agro360" -f database/agro360-postgres-full.sql` e execute os hosts com `dotnet run`.

## Sprint 16 — Integrações

A central em `/Integrations` oferece API pública, webhooks assinados, importação/exportação CSV, documentos fiscais como metadados, IoT, split manual, mensageria e monitoramento. Consulte [`docs/sprint-16-integrations.md`](docs/sprint-16-integrations.md). Para rodar sem Docker, defina uma connection string PostgreSQL e execute:

```bash
export ConnectionStrings__Agro360='Host=localhost;Port=5432;Database=agro360;Username=agro360;Password=troque-me'
psql "$ConnectionStrings__Agro360" -f database/agro360-postgres-full.sql
dotnet run --project src/Hosts/Agro360.Api
dotnet run --project src/Hosts/Agro360.Web
```

## Sprint 17 — Mapa Agro

O módulo `/Maps` reúne mapas operacionais, desenho de propriedades/talhões/pastagens/piquetes/zonas, ocorrências, rotas, monitoramento territorial e dashboard. GeoJSON/JSONB e latitude/longitude garantem execução em qualquer PostgreSQL, sem PostGIS e sem token de mapas obrigatórios. Consulte [visão da sprint](docs/sprint-17-mapa-geoespacial.md), [modelo](docs/geospatial-model.md), [importação/exportação](docs/geojson-import-export.md) e [UI](docs/map-ui.md).

Para rodar sem Docker, configure `ConnectionStrings__Agro360`, aplique `database/agro360-postgres-full.sql` com `psql` e execute `dotnet run --project src/Hosts/Agro360.Api` e `dotnet run --project src/Hosts/Agro360.Web`.

## Sprint 18 — Cooperativas e B2B
A central `/Cooperatives` reúne cooperados, assistência técnica, programas integrados, marketplace, compras coletivas, contratos, bonificações, repasses, crédito rural interno e portal do produtor. Para executar sem Docker, exporte `ConnectionStrings__Agro360`, aplique `database/agro360-postgres-full.sql` com `psql`, e inicie API e Web com `dotnet run --project src/Hosts/Agro360.Api` e `dotnet run --project src/Hosts/Agro360.Web`.

## Sprint 19 — RH Rural e SST

A área `/RuralHr` entrega pessoas, equipes, jornada, alocações, produtividade/custos, treinamentos, EPIs, segurança, incidentes, ações corretivas, alojamento, transporte e dashboard. A API está em `/api/rural-hr/*`. Consulte [visão funcional](docs/sprint-19-rh-rural-sst.md), [custos](docs/labor-costs.md), [treinamentos e EPIs](docs/trainings-and-ppe.md), [segurança](docs/safety-incidents.md) e [validação/lookups](docs/form-validation-and-lookups.md).

Execução local sem Docker:

```bash
export ConnectionStrings__Agro360='Host=localhost;Port=5432;Database=agro360;Username=agro360;Password=senha'
psql "$ConnectionStrings__Agro360" -f database/agro360-postgres-full.sql
dotnet run --project src/Hosts/Agro360.Api
dotnet run --project src/Hosts/Agro360.Web
```

## Sprint 21 — implantação comercial

Acesse `/Deployment` para executar onboarding por segmento, aplicar templates, acompanhar o checklist e validar importações CSV antes da gravação. A referência operacional está em [docs/ONBOARDING.md](docs/ONBOARDING.md).

Sem Docker: instale o SDK definido em `global.json` e PostgreSQL comum, defina `ConnectionStrings__Agro360`, execute `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql` e inicie API/Web com `dotnet run --project`. Dados de demonstração são opcionais: `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/seed-demo.sql`.

## Sprint 22 — CRM Agro e Comercial B2B
O Agro360 inclui CRM, força de vendas, funil, atividades, preços, pedidos, contratos, metas, comissões e split interno em `/Commercial`. Use PostgreSQL externo em `ConnectionStrings__Agro360` e instale sem Docker:
```bash
export ConnectionStrings__Agro360='Host=localhost;Port=5432;Database=agro360;Username=agro360;Password=...'
psql "$ConnectionStrings__Agro360" -f database/agro360-postgres-full.sql
dotnet run --project src/Hosts/Agro360.Api
dotnet run --project src/Hosts/Agro360.Web
```
Detalhes operacionais e homologação: [docs/COMMERCIAL-MODULE.md](docs/COMMERCIAL-MODULE.md).

## Sprint 23 — Documentos e Evidências

A biblioteca documental operacional está disponível em `/Documents`. Configure arquivos com `Storage__RootPath` (padrão local seguro `App_Data/documents`) e PostgreSQL externo com `ConnectionStrings__Agro360`. Consulte [o guia do módulo](docs/DOCUMENTS-EVIDENCES.md) para instalação sem Docker, permissões, homologação e consulta pública.

## Sprint 24 — Operação 360

A rota `/Work` oferece tarefas, alertas determinísticos, regras configuráveis, aprovações, notificações internas, agenda e outbox com persistência PostgreSQL real. Configure `ConnectionStrings__Agro360` com a connection string do PostgreSQL externo, execute `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql` e use `dotnet run` nos hosts API e Web; Docker é opcional. Consulte [o guia de workflows e alertas](docs/WORKFLOWS-ALERTS.md).

## Inteligência Agro360 e Relatórios

Após configurar o PostgreSQL externo em `ConnectionStrings__Agro360`, execute a aplicação sem Docker com `scripts/run-local.sh`. Acesse `/Intelligence` para indicadores e gráficos reais, `/Maps` para geovisualização operacional e `/Reports` para relatórios filtrados e CSV. Consulte `docs/BI-REPORTS-MAPS.md` e `docs/DESIGN-SYSTEM.md` para uso e homologação responsiva.

## Sprint 26 — Agro360 Campo

Acesse `/field` para a operação mobile/PWA. Manejos, ocorrências, check-ins e evidências podem entrar na fila IndexedDB e são materializados no PostgreSQL por sincronização idempotente. Consulte [operações mobile](docs/MOBILE-FIELD-OPERATIONS.md) e [sincronização offline](docs/OFFLINE-SYNC.md).

## Portal Agro360 (Sprint 27)

O portal B2B externo está em `/Portal/Login`, separado da administração, com convite seguro, primeiro acesso, dashboard por perfil, comunicados, solicitações e Marketplace com cotações persistidas. Consulte [Portal Externo](docs/PORTAL-EXTERNO.md) e [Marketplace B2B](docs/MARKETPLACE-B2B.md).

Para rodar sem Docker, instale o SDK definido em `global.json`, configure `ConnectionStrings__Agro360` com uma connection string PostgreSQL externa, execute `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql` e use `./scripts/run-local.sh`. Nenhum PostgreSQL embutido é iniciado.

## Sprint 28 — Qualidade e Compliance

A Central de Qualidade adiciona requisitos configuráveis, especificações versionadas, inspeções, status/holds de lote, não conformidades, CAPA, auditorias, compliance de beneficiamento e prontidão de exportação. Consulte `docs/QUALITY-COMPLIANCE.md`. O PostgreSQL continua externo e configurado por `ConnectionStrings__Agro360`; aplique `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql` sem Docker.

## Sprint 29 — Produto SaaS B2B

A governanca comercial passa a incluir tenants auditados, catalogo de planos/features/limites, assinaturas e cobrancas internas sem gateway ficticio, overrides temporarios, onboarding e white label. Consulte [Administracao SaaS](docs/SAAS-ADMIN.md), [Planos e assinaturas](docs/PLANS-SUBSCRIPTIONS.md), [Feature flags](docs/FEATURE-FLAGS.md) e [Onboarding/white label](docs/ONBOARDING-WHITELABEL.md). Para executar sem Docker, configure PostgreSQL externo em `ConnectionStrings__Agro360`, aplique `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql` e execute `./scripts/run-local.sh`.

## Sprint 30 — Integrações e Fiscal

A central `/Integrations` reúne conectores, aplicações externas, chaves hasheadas, webhooks, importações CSV, exportações e documentos fiscais por tenant. Configure PostgreSQL externo em `ConnectionStrings__Agro360`, aplique `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql` e execute `./scripts/run-local.sh`; Docker não é necessário. Emissão fiscal permanece bloqueada até provider oficial, credencial e certificado serem homologados. Consulte `docs/INTEGRATIONS.md`, `docs/API-EXTERNA.md`, `docs/WEBHOOKS.md`, `docs/IMPORT-EXPORT.md` e `docs/FISCAL.md`.

## Sprint 33 — Atendimento e Suporte

A rota `/support` oferece chamados persistentes, SLA, Central de Ajuda, implantação assistida, treinamento, feedback, backlog interno e release notes. Execute sem Docker com PostgreSQL externo definindo `ConnectionStrings__Agro360`, aplique `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql` e inicie API/Web conforme [guia sem Docker](docs/getting-started-without-docker.md). Detalhes operacionais estão em [Suporte e Customer Success](docs/SUPPORT-CUSTOMER-SUCCESS.md).

A Sprint 33 não admite artefatos binários novos; documentação, exports e evidências desta entrega são texto/Markdown/CSV.

## Sprint 34 — SST Rural

Acesse `/Sst` para a central de segurança operacional. Para rodar sem Docker, configure `ConnectionStrings__Agro360` com um PostgreSQL externo, execute `psql "$ConnectionStrings__Agro360" -f database/agro360-postgres-full.sql` e inicie os hosts com `dotnet run`. Consulte [SST Rural](docs/SST-RURAL.md) e [privacidade ocupacional](docs/SST-PRIVACIDADE.md). Esta sprint não gera nem aceita novos arquivos binários.

## Sprint 35 — Frota e Máquinas

A central `/Fleet` entrega cadastro de ativos e operadores, manutenção preventiva/corretiva, OS, abastecimento, lubrificação, pneus, paradas, disponibilidade, custos e CSV. Consulte [Frota e Máquinas](docs/FROTA-MAQUINAS.md), [Manutenção e OS](docs/MANUTENCAO-OS.md) e [Abastecimento e custos](docs/ABASTECIMENTO-CUSTOS.md).

O sistema roda sem Docker: configure `ConnectionStrings__Agro360` com a connection string do PostgreSQL externo, execute `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql` e use `dotnet run --project src/Hosts/Agro360.Api` / `dotnet run --project src/Hosts/Agro360.Web`. Não gere ou versione arquivos binários nesta sprint.

## Sprint 36 — Financeiro e Controladoria

A central `/Finance` entrega caixa, DRE, orçamento e rentabilidade sobre dados reais, com isolamento por tenant e permissões segregadas. Consulte [o guia financeiro](docs/FINANCEIRO-CONTROLADORIA.md). Rode sem Docker configurando `ConnectionStrings__Agro360` para PostgreSQL externo, aplique `database/agro360-postgres-full.sql` com `psql` e execute `dotnet run --project src/Hosts/Agro360.Api` e `dotnet run --project src/Hosts/Agro360.Web`. Não gere arquivos binários para esta sprint.

## Sprint 37 — Compras e Suprimentos
A central `/Procurement` cobre fornecedores, homologação, catálogo, requisições, pedidos, recebimento e CSV com PostgreSQL multi-tenant. Consulte [o guia operacional](docs/PROCUREMENT-SUPPLIES.md). O sistema roda sem Docker por `ConnectionStrings__Agro360`.

## Sprint 38 — Produção Agroindustrial
O Agro360 inclui PCP agroindustrial com formulações versionadas, ordens, beneficiamento, apontamentos, consumo transacional, rendimento, perdas, qualidade, paradas, custos e rastreabilidade por lote. A execução local não exige Docker: configure o PostgreSQL externo em `ConnectionStrings__Agro360`, aplique `database/agro360-postgres-full.sql` e inicie API/Web com `dotnet run`. Consulte [o guia industrial](docs/INDUSTRIAL-PRODUCTION.md).

## Sprint 39 — Exportação e Trading

A área `/Export` integra clientes internacionais, contratos, documentos, embarques, câmbio manual, custos, compliance e rastreabilidade. Instale `database/migrations/039_sprint39_export_trading.sql` ou o script completo e configure o PostgreSQL externo em `ConnectionStrings__Agro360`. Consulte [a documentação operacional](docs/EXPORT-TRADING.md).

## Sprint 40 — Fiscal e Faturamento

Área operacional em `/Fiscal`, API em `/api/fiscal`, regras gerenciais parametrizadas, faturamento, documentos externos, conferência de compras, integrações de estoque/financeiro, auditoria e CSV. Não há emissão SEFAZ simulada; consulte [a documentação fiscal](docs/FISCAL-BILLING.md).

## Sprint 41 — Inteligência Agro360

Acesse `/inteligencia-agro360` para o Painel 360, indicadores, alertas, regras, riscos, recomendações baseadas em regras, auditoria, metas e CSVs executivos. Não há dados demonstrativos nem IA simulada: ausência de fonte aparece como indisponível. Consulte [`docs/INTELLIGENCE-BI.md`](docs/INTELLIGENCE-BI.md).

## Sprint 42 — Sustentabilidade e ESG
A área `/sustentabilidade` reúne conformidade ambiental, indicadores, recursos, emissões gerenciais, fornecedores, lotes, carbono, auditorias, ações, alertas e CSVs com isolamento por tenant. Consulte [a documentação operacional](docs/SUSTAINABILITY-ESG.md). Não há consulta oficial de CAR nem certificação de carbono configurada; registros manuais são auditáveis e não substituem certificação externa.

## Sprint 43 — Campo Mobile PWA

A área `/field` oferece operação responsiva, rascunho IndexedDB, fila idempotente, checklists versionados, evidências reais, ocorrências, QR opaco, localização autorizada e assinatura gerencial. O schema `field_mobile` aplica RLS por tenant e regras de conflito sem sobrescrita automática. Configure PostgreSQL externo por `ConnectionStrings__Agro360`; Docker não é necessário. Consulte [Campo Mobile PWA](docs/FIELD-MOBILE-PWA.md).

## Sprint 45 — Agro360 Enterprise

A plataforma SaaS agora formaliza super administração única MNSOFT, isolamento e governança por tenant, perfis/permissões, módulos contratados, cobrança exclusivamente gerencial, login por e-mail/CPF/CNPJ, culturas `pt-BR`, `en-US` e `es-ES` e ajuda contextual. O PostgreSQL é externo por connection string e não requer Docker. Consulte [`docs/SAAS-PLATFORM.md`](docs/SAAS-PLATFORM.md).

## Sprint 46 — Marketplace e Ecossistema Agro360

A área autenticada `/ecosystem` reúne marketplace de módulos, parceiros com acesso temporário, aplicações externas, chaves exibidas uma única vez e persistidas por SHA-256, webhooks HTTPS com tentativas finitas, catálogo comercial, documentação do desenvolvedor e auditoria isolada por tenant. A ativação e mudança de plano continuam administrativas: não existe cobrança automática sem provedor homologado. Aplique `database/migrations/046_marketplace_ecosystem.sql` ou o instalador completo em PostgreSQL externo; Docker não é necessário.

## Sprint 47 — CRM e ciclo do cliente

A plataforma integra CRM, pipeline, propostas com total no backend, contratos SaaS, implantação assistida, suporte/SLA, saúde explicável, conhecimento e portal isolado. As novas rotas exigem permissões específicas, as tabelas usam auditoria/RLS por tenant e toda comunicação sem provedor permanece pendente na outbox. A experiência responsiva usa funil, timeline, badges e o componente recolhível **Como usar esta tela**. Consulte `docs/CRM-COMMERCIAL.md`, `docs/CUSTOMER-SUCCESS.md`, `docs/SUPPORT.md` e a migração `047_crm_customer_lifecycle.sql`.

## Sprint 48 — Governança, Migração, LGPD e Performance

Governança persistente e isolada por tenant: importação CSV pré-validada, qualidade de dados, exportação gerencial segura, solicitações LGPD, auditoria avançada, sessões e telemetria de performance. A migração `048_data_governance.sql` cria constraints, FKs, índices e RLS. Consulte `docs/DATA-GOVERNANCE.md`, `docs/IMPORT-MIGRATION.md`, `docs/LGPD-SECURITY.md` e `docs/PERFORMANCE.md`.

## Sprint 49 — Workflows inteligentes

A Central de Processos em `/Work` reúne tarefas, aprovações, notificações internas, outbox honesta, agenda, SLA e automações tenant-safe. A migration `049_workflow_automation.sql` adiciona versões, eventos, templates localizados, escalonamentos e execuções idempotentes. Consulte `docs/WORKFLOWS.md`, `docs/NOTIFICATIONS.md` e `docs/SLA-AUTOMATION.md`.

## Sprint 50 — qualidade transversal de UX

A camada global de formulários adiciona validação acessível por campo e resumo, estado de envio, confirmação funcional para ações sensíveis e ajuda contextual compacta. O backend dispõe de validação canônica para CPF/CNPJ, e-mail, decimal por cultura e datas. O schema `ui` guarda conteúdo traduzível, catálogo de mensagens, regras, confirmações e eventos com RLS por tenant. Consulte `docs/UX-FORMS-VALIDATION.md` e `docs/CONTEXTUAL-HELP.md`.

## Instalação PostgreSQL sem Docker (schema canônico)
Configure `ConnectionStrings__Agro360` para um PostgreSQL externo e execute `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql`. O instalador cria exclusivamente o schema `agro360`; os módulos são preservados por prefixos (`identity_users`, `finance_payables`, etc.). A senha inicial do administrador nunca pertence ao SQL: defina `AGRO360_SUPERADMIN_INITIAL_PASSWORD` antes do bootstrap seguro da aplicação. Em produção não há senha padrão; em Development uma senha configurada continua exigindo troca no primeiro acesso.

## Operação assistida e experiência do usuário

- Consulte o [Manual do usuário](docs/USER-MANUAL.md) para acesso, perfis, módulos, dashboards, alertas e suporte.
- Siga as [Diretrizes de UX](docs/UX-GUIDELINES.md) ao criar telas, mini manuais, ajuda de campo e confirmações.
- Execute o [Checklist de QA](docs/QA-CHECKLIST.md) antes de homologar uma entrega.
# Maturidade SaaS e operação guiada

O shell web mantém a sessão e o token compatível entre os módulos, apresenta menus por permissão e oferece acesso direto à **Central de Implantação** e à **Central de Tarefas e Alertas**. A implantação calcula sua prontidão exclusivamente a partir dos cadastros persistidos do tenant (usuários, perfis, módulos, fazendas e checklist), inclusive em banco vazio.

## Comercial Agro 360 (sprint atual)

Consulte `docs/COMMERCIAL-AGRO.md` para fluxo, regras implementadas, modelo persistente e pendências reais de integração.
