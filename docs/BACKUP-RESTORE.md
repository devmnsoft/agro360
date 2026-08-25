# Backup, restauração e recuperação

## Pré-condições

Use uma estação autorizada com Docker Compose, espaço livre e `.env` protegido. Os scripts obtêm usuário/banco do container; nenhuma senha é aceita em argumento ou gravada no backup. O formato custom do `pg_dump` permite validação pelo `pg_restore`.

## Backup e ensaio de restauração

```bash
mkdir -p backups
scripts/backup-postgres.sh backups/agro360-$(date -u +%Y%m%dT%H%M%SZ).dump
scripts/restore-postgres.sh backups/<arquivo>.dump agro360_restore_test --confirm
```

O backup recusa sobrescrita; `--force` deve ser uma decisão explícita. A restauração recusa o banco de origem, valida SHA-256 quando disponível, recria um banco separado, usa `--exit-on-error` e consulta `platform.schema_migrations`. Após validar contagens e amostras por tenant, execute o Migrator apontando para o banco restaurado antes de promover a conexão.

## Rollback e Worker

1. interrompa API e Worker, preservando PostgreSQL;
2. restaure a imagem anterior pelo digest imutável;
3. migrations são forward-only: não reverta schema nem apague colunas; restaure um backup em banco separado se houver incompatibilidade;
4. mensagens que excedem `Outbox:MaximumAttempts` recebem `dead_lettered_at` e deixam a fila ativa;
5. investigue `tenant_id`, `id`, `event_type`, `aggregate_id`, `correlation_id`, `attempts` e `last_error` sem copiar o payload para tickets/logs;
6. reprocessamento exige operador autorizado, causa corrigida e auditoria. Em transação com contexto RLS, limpe `dead_lettered_at`/`last_error`, redefina `next_attempt_at=now()` e registre a ação em `audit.logs`. Não zere `attempts`, pois ele é evidência operacional.

## Retenção

Defina retenção e criptografia no storage corporativo, teste restauração periodicamente e limite acesso ao princípio do menor privilégio. A aplicação não oferece exclusão automática de backup.
