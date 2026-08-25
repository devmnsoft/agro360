# Implantação em staging

## Configuração obrigatória

Copie `.env.example` para um secret store/arquivo fora do versionamento e substitua todas as sentinelas `change-this`/`replace-with`. Use senhas distintas para proprietário/migração e `agro360_app`, JWT aleatório com pelo menos 32 bytes, origens CORS exatas e TLS no proxy. Não exponha PostgreSQL publicamente.

## Gate reproduzível

```bash
dotnet restore MNSOFT.Agro360.sln
dotnet format MNSOFT.Agro360.sln --verify-no-changes --no-restore
dotnet build MNSOFT.Agro360.sln -c Release --no-restore
docker compose up -d postgres
dotnet run --project src/Hosts/Agro360.Migrator -c Release --no-build
dotnet run --project src/Hosts/Agro360.Migrator -c Release --no-build
dotnet test MNSOFT.Agro360.sln -c Release --no-build --report-trx --results-directory TestResults
docker compose build api web worker migrator
docker compose up -d
curl --fail http://localhost:8081/health/live
curl --fail http://localhost:8081/health
```

O Migrator usa o proprietário; os hosts usam `agro360_app`. Só habilite `Bootstrap__Enabled` durante criação controlada do primeiro tenant e desligue em seguida. O bootstrap atual não é um seed completo de homologação.

## Promoção e rollback

Promova imagens por digest, registre versão/digest e faça backup antes da mudança. Aguarde o Migrator terminar, readiness da API ficar saudável e então libere API/Worker. Para rollback, siga `BACKUP-RESTORE.md`; migrations publicadas são forward-only.
