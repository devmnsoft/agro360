# Instalação PostgreSQL sem Docker

Requisitos: PostgreSQL com `psql`, .NET SDK definido em `global.json` e uma base vazia acessível. Não há host, usuário, senha ou banco embutido no SQL.

```bash
export ConnectionStrings__Agro360='Host=localhost;Port=5432;Database=agro360;Username=SEU_USUARIO;Password=SUA_SENHA'
psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql
dotnet restore
dotnet run --project src/Hosts/Agro360.Api
dotnet run --project src/Hosts/Agro360.Web
```

O arquivo único contém todas as estruturas até a Sprint 15, extensões, permissões, índices e RLS. É idempotente para instalação/atualização estrutural; backups continuam obrigatórios antes de reaplicar em produção.
