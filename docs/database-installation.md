# Instalação do banco

Crie um banco vazio em qualquer PostgreSQL 14+ compatível. O administrador deve habilitar PostGIS, `pgcrypto`, `pg_trgm` e `unaccent`; usuário, servidor, porta e banco não são fixados pelo projeto.

## Migrator

```bash
export ConnectionStrings__Agro360='Host=SERVIDOR;Database=BANCO;Username=USUARIO;Password=SENHA'
./scripts/migrate.sh status
./scripts/migrate.sh validate
./scripts/migrate.sh migrate
./scripts/migrate.sh seed minimal
# somente homologação/desenvolvimento:
./scripts/migrate.sh seed demo
```

O Migrator lê `database/migrations`, mantém `platform.schema_migrations`, compara SHA-256, serializa execuções por advisory lock e aplica cada arquivo em transação.

## Cliente SQL

Execute `database/releases/v0.2.0/agro360-v0.2.0-full-install.sql` em banco vazio pelo `psql -v ON_ERROR_STOP=1 -f ARQUIVO`, pgAdmin ou DBeaver. O arquivo é autônomo e não contém `\i`, credenciais ou caminhos locais.

## Backup e restore

Configure `PGHOST`, `PGPORT`, `PGUSER` e `PGDATABASE`; guarde a senha em `.pgpass`/`pgpass.conf`. Use `./scripts/backup-db.sh PREFIXO` e, apontando `PGDATABASE` para um banco vazio, `./scripts/restore-db.sh ARQUIVO.sql` ou `.backup`. Nunca restaure sobre produção sem backup e validação prévios.

## Instalação completa v0.3.0 (arquivo único)

Defina uma connection string PostgreSQL com SSL conforme seu ambiente e execute, a partir da raiz:

```bash
psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql
```

O arquivo é autossuficiente: cria extensões, schemas, estruturas, índices, funções, triggers/políticas, views, permissões, perfis, configurações e dados mínimos. Não contém host, credencial, porta ou nome de banco. No pgAdmin/DBeaver, conecte-se ao banco desejado e execute o arquivo inteiro.

Para manutenção incremental, execute `database/migrations/005_sprint6_operations.sql` após as migrações anteriores ou use o Migrator. A release imutável está em `database/releases/v0.3.0/` com checksums SHA-256.

## Instalador completo v0.4.0 (Sprint 7)

O arquivo autossuficiente `database/agro360-postgres-full.sql` contém todo o histórico até a Pecuária 360. Em banco PostgreSQL vazio com PostGIS disponível:

```bash
psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql
```

No pgAdmin/DBeaver, abra o mesmo arquivo no editor conectado ao banco alvo. Para atualizar uma instalação v0.3.0, execute o Migrator ou `database/migrations/006_sprint7_livestock360.sql`. O pacote versionado está em `database/releases/v0.4.0/` e `checksums.sha256` permite validar sua integridade.

## Sprint 8 / v0.5.0

Instalação integral e portável: `psql "$ConnectionStrings__Agro360" -f database/agro360-postgres-full.sql`. Para manutenção incremental, aplique `database/migrations/007_sprint8_finance.sql`. O release reproduzível está em `database/releases/v0.5.0/` com checksums SHA-256.

## Instalador completo até a Sprint 9

`database/agro360-postgres-full.sql` contém fundação e todas as migrações até armazenagem/logística. Ele não seleciona nem cria banco, usuário ou senha.

```bash
psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql
```

No pgAdmin ou DBeaver, conecte-se ao banco desejado, abra o mesmo arquivo e execute-o integralmente. Valide portabilidade com `./scripts/validate-full-sql.sh`.
