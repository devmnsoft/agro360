# MNSOFT Agro 360

Plataforma integrada, multi-tenant e modular para gestão do agronegócio. A solução conecta propriedade, agricultura, pecuária, estoque, custos, comercial, financeiro e rastreabilidade em uma única cadeia operacional.

> Todo acontecimento físico deve produzir, quando aplicável, reflexos operacionais, financeiros, logísticos, documentais e analíticos.

## Estado desta entrega

Esta versão fecha a fundação técnica e entrega uma primeira fatia vertical executável dos três fluxos que validam o produto:

1. propriedade → talhão → safra → insumo → plantio → colheita → venda → contas a receber;
2. animal → pesagem/vacinação → consumo de estoque → custo → venda → rastreabilidade;
3. milho → estoque → alimentação animal → custo do lote, preparado pela malha de rastreabilidade.

Os demais domínios estão planejados em `docs/ROADMAP.md` e catalogados em `docs/MODULE-CATALOG.md`. Um módulo só é marcado como concluído quando possui regra, persistência, autorização, auditoria, API, interface e teste.

## Arquitetura

- .NET 10, ASP.NET Core e C#;
- monólito modular orientado a domínio, pronto para extração futura;
- PostgreSQL + PostGIS, Dapper e SQL versionado;
- API REST com JWT, refresh token, Problem Details, paginação, idempotência, rate limit e correlation ID;
- Razor Pages/PWA responsiva, com tema claro/escuro e `Ctrl+K`;
- worker de Outbox;
- isolamento multi-tenant em aplicação e Row-Level Security;
- UUID como chave distribuída, `numeric` para dinheiro/medidas e concorrência otimista;
- Docker Compose com PostgreSQL/PostGIS, Redis, MinIO, API, Web, Worker e Migrator.

## Projetos

```text
src/
  BuildingBlocks/
    Agro360.SharedKernel
    Agro360.Multitenancy
  Modules/
    Agro360.Domain
    Agro360.Application
    Agro360.Infrastructure
  Hosts/
    Agro360.Api
    Agro360.Web
    Agro360.Worker
    Agro360.Migrator
  Mobile/
    Agro360.Mobile.Core
tests/
  Agro360.UnitTests
  Agro360.ArchitectureTests
  Agro360.IntegrationTests
database/migrations
deploy
docs
scripts
```

## Início rápido

Pré-requisitos: Docker Desktop ou Docker Engine com Compose.

```bash
cp .env.example .env
docker compose up --build
```

- interface: `http://localhost:8080`
- API: `http://localhost:8081`
- OpenAPI: `http://localhost:8081/openapi/v1.json`
- health: `http://localhost:8081/health`
- MinIO: `http://localhost:9001`

O migrador executa antes dos hosts. Em desenvolvimento, `POST /api/v1/bootstrap` cria o primeiro tenant e administrador. Desabilite `Bootstrap__Enabled` após a criação.

Exemplo:

```bash
curl -X POST http://localhost:8081/api/v1/bootstrap \
  -H 'Content-Type: application/json' \
  -d '{"tenantName":"Fazenda Demonstração","tenantSlug":"demo","adminName":"Administrador","email":"admin@agro360.local","password":"TroqueAgora!123"}'
```

Depois, autentique em `POST /api/v1/auth/login` e envie `Authorization: Bearer <token>`.

## Desenvolvimento local

```bash
dotnet restore MNSOFT.Agro360.sln
dotnet build MNSOFT.Agro360.sln --no-restore
dotnet test MNSOFT.Agro360.sln --no-build
```

Use `scripts/verify.sh` para executar formatação, build e testes. Consulte `docs/ARCHITECTURE.md`, `docs/BUSINESS-RULES.md`, `docs/API.md` e `docs/ROADMAP.md` antes de ampliar módulos.

## Segurança

- Nunca use os segredos de exemplo em produção.
- O header de tenant não substitui a claim JWT fora de desenvolvimento.
- O usuário da aplicação não deve ser proprietário do banco, para que RLS não seja contornada.
- Ações de suporte e bootstrap devem permanecer desabilitadas por padrão em produção.
