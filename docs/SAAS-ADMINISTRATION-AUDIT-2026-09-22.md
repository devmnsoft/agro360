# Auditoria da administração SaaS — 2026-09-22

## Baseline e método

Auditoria executada na branch `work`, baseline `765e2db`. Foram lidos os contratos, serviço SaaS, controladores, Razor/JavaScript, migrations 045 e 097, instalador consolidado, provisionamento de homologação e testes. O repositório iniciou limpo. Este relatório não trata documentos de sprint como evidência de execução.

## Classificação verificada antes desta evolução

| Capacidade | Estado | Evidência / divergência |
|---|---|---|
| Login, refresh e isolamento RLS | Parcial | Implementação e testes estáticos existem; PostgreSQL e .NET não estão instalados neste ambiente, logo o gate real não foi reexecutado. |
| Tenant e estados | Parcial | CRUD e eventos existiam, mas uma coluna agregava bloqueio administrativo e financeiro. Fazenda/filial permanecem entidades operacionais e não são convertidas em tenant. |
| Usuários, perfis e convites | Funcional com evidência de código | Escopo de tenant, proteção do último administrador, revogação de sessões e convite sem simular entrega estão implementados. A identidade física ainda é armazenada por tenant; separar identidade global exige migração compatível futura. |
| Planos e módulos | Parcial | Catálogo, dependências, limites e entitlements existem; a autorização uniforme de todos os comandos operacionais ainda é backlog. |
| Assinaturas e cobranças | Parcial | Assinatura e competência idempotente existiam; baixa parcial e crédito de excedente estavam ausentes. |
| Inadimplência | Parcial | Status agregado existia; causas independentes e histórico estavam ausentes. |
| Auditoria global | Funcional com evidência de código | Ações administrativas são persistidas com ator e tenant; não há impersonação silenciosa. |
| UI global/cliente | Parcial | Navegação contextual e telas principais existem; detalhe completo por tenant, paginação e edição de módulos seguem pendentes. |

## Decisões desta entrega

* `agro360.saas_*` continua canônico; as tabelas `platform_*` legadas não originam um segundo faturamento.
* Pagamento manual é um evento imutável e idempotente. O valor é aplicado ao saldo; excedente vira crédito explícito, nunca desaparece. Nenhum meio bancário ou fiscal é simulado.
* Restrições `ADMINISTRATIVE` e `FINANCIAL` possuem ciclos independentes. Encerrar uma causa não encerra a outra.
* A migration 104 é incremental, mantém dados e amplia os estados com `PARTIALLY_PAID`. RLS forçada e grants da role da aplicação são preservados.
* Datas de competência permanecem mensais no primeiro dia, valores usam `numeric/decimal` e arredondamento bancário de duas casas. Não foram inventados imposto, multa, juros, reajuste ou pró-rata.

## Atualização e recuperação

1. Faça backup e valide restore conforme `docs/BACKUP-RESTORE.md`.
2. Execute `dotnet run --project src/Hosts/Agro360.Migrator -- migrate` ou, em instalação limpa, aplique `database/agro360-postgres-full.sql`.
3. Valide com `dotnet run --project src/Hosts/Agro360.Migrator -- validate` e `./scripts/validate-database-assets.sh`.
4. Em rollback operacional, pare novas baixas e restaure o backup. Não remova a migration de um banco que já recebeu pagamentos; as tabelas são o histórico contábil gerencial.

## Limitações e backlog explícito

* Executar os gates PostgreSQL: instalação/reaplicação, upgrade representativo, seeds idempotentes, role da aplicação/pool e logins dos três perfis solicitados.
* Migrar identidade tenant-scoped para identidade global mais vínculo, sem quebrar tokens e auditoria.
* Aplicar entitlement módulo + permissão a cada command handler e adicionar proteção concorrente uniforme aos limites.
* Completar detalhe do tenant, preview de impacto de suspensão/downgrade, catálogo versionado, política automática de atraso e jornada E2E no navegador.
