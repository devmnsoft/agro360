# Release v0.2.0

Esta release entrega os hosts nativos API, Web, Worker e Migrator, módulos operacionais multi-tenant, persistência Dapper/PostgreSQL, tratamento global de erros, dashboard responsivo e pacote SQL autônomo.

O caminho homologável não usa Docker. A atualização recomendada é: backup, `migrate validate`, `migrate status`, `migrate migrate`, smoke tests e liberação dos hosts publicados. Seeds `demo` jamais devem ser executados em produção; `minimal` contém apenas dados estruturais necessários.

Consulte `RELEASE-NOTES-v0.2.0.md` e `HOMOLOGATION-REPORT-v0.2.0.md` para escopo, evidências e limitações conhecidas.
