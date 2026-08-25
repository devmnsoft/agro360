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
