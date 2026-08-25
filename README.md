# MNSOFT Agro 360

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

## Instalação do banco

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
