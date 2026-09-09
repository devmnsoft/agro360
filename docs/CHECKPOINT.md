# Checkpoint de continuidade

- Repositório: `devmnsoft/agro360`.
- Base inicial: branch `main`, commit `4b650fb74fc6d73050a5d84d0540f3962bf75f8c`.
- Alteração preexistente preservada: `.github/copilot-instructions.md` não rastreado.
- Configuração canônica: `ConnectionStrings:Agro360`; senha separada opcional em `PostgreSql:Password`; passfile em `PostgreSql:Passfile`.
- Banco: migration `064_procurement_receipt_integrations.sql` e instalador completo atualizados.
- Validação local: build sem avisos; 110 testes passaram; 4 testes de PostgreSQL foram ignorados pela ausência de `AGRO360_TEST_CONNECTION_STRING`.
- Cenário HTTP sem banco: liveness 200, readiness 503, login 503 com código/TraceId e refresh inválido 401 com código/TraceId.
- Navegador: Compras, o formulário integrado de recebimento e SaaS renderizaram; sem API/banco, as páginas exibiram indisponibilidade e tentativa manual, sem sucesso falso.
- Qualidade mecânica: JavaScript, 640 rotas, SQL consolidado, `git diff --check` e formatação dos C# alterados aprovados. A verificação global de formato segue bloqueada pela dívida preexistente de finais de linha CRLF fora deste escopo.
- Bloqueio externo: não há segredo PostgreSQL, passfile, `AGRO360_TEST_CONNECTION_STRING` ou container ativo nesta máquina. Configure localmente e reinicie os hosts antes da homologação real.

Próximo prompt: `Configure o segredo PostgreSQL local e execute a homologação real de migration, login/refresh, SaaS e recebimento idempotente.`
