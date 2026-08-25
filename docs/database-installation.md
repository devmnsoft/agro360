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
