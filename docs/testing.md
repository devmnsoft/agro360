# Testes

Execute `./scripts/test.sh` (ou `./scripts/test.ps1`) para restore, build Release e todas as suítes. Testes unitários e de arquitetura não exigem banco nem Docker.

Testes de integração usam exclusivamente `AGRO360_TEST_CONNECTION_STRING`. Sem essa variável eles são ignorados com mensagem explícita, sem falhar as demais suítes. Para executá-los:

```bash
export AGRO360_TEST_CONNECTION_STRING='Host=SERVIDOR;Database=agro360_test;Username=USUARIO;Password=SENHA'
export ConnectionStrings__Agro360="$AGRO360_TEST_CONNECTION_STRING"
./scripts/migrate.sh migrate
dotnet test tests/Agro360.IntegrationTests -c Release
```

Use um banco descartável já criado e com as extensões requeridas. A CI padrão prova restore, formatação, compilação e testes sem serviço Docker; validação PostgreSQL completa pode ser executada em ambiente protegido que forneça a variável.

## Sprint 6 e PostgreSQL de integração

As regras operacionais têm testes unitários sem infraestrutura. Testes que abrem PostgreSQL leem exclusivamente `AGRO360_TEST_CONNECTION_STRING`; quando ausente, são ignorados com mensagem explícita. Execute `dotnet test --configuration Release`. O SQL único também pode ser validado com `psql "$AGRO360_TEST_CONNECTION_STRING" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql` em um banco descartável.

## Testes da Sprint 7

Testes unitários não exigem banco. Integrações reais usam exclusivamente `AGRO360_TEST_CONNECTION_STRING`; sem a variável, são ignoradas de forma explícita. Para validar o instalador consolidado também execute `./scripts/validate-full-sql.sh`.

## Sprint 8

As regras financeiras são cobertas pelos testes unitários. Testes que abrem PostgreSQL leem exclusivamente `AGRO360_TEST_CONNECTION_STRING` e são ignorados, com justificativa, quando ela não está definida. Valide também o artefato portável com `bash scripts/validate-full-sql.sh`.

## Sprint 9

Os testes unitários cobrem pesos, descontos, capacidade, bloqueio/saldo de lote e frete. Testes que abrem PostgreSQL leem exclusivamente `AGRO360_TEST_CONNECTION_STRING`; sem a variável são ignorados com mensagem explícita.

```bash
dotnet restore
dotnet build --configuration Release
dotnet test --configuration Release
./scripts/validate-full-sql.sh
```

## Cobertura da Sprint 11

`dotnet test --configuration Release` inclui regras de dose, períodos de irrigação, clima, talhão duplicado, formulário sem inputs de IDs técnicos e integridade do SQL standalone. Testes de integração precisam de `ConnectionStrings__Agro360` apontando para PostgreSQL descartável.
