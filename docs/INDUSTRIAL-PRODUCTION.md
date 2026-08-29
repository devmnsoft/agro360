# Produção Agroindustrial — Sprint 38

## Escopo operacional
O módulo **Produção Agroindustrial** executa PCP, formulações versionadas, beneficiamento, apontamento, consumo transacional por lote, rendimento, perdas, paradas, qualidade, custos e genealogia de lotes. Toda consulta e escrita usa o `tenant_id` da sessão; RLS reforça o isolamento no PostgreSQL. Não há dados demonstrativos no dashboard.

## Implantação sem Docker
1. Instale .NET SDK e PostgreSQL em infraestrutura externa.
2. Defina `ConnectionStrings__Agro360="Host=...;Database=...;Username=...;Password=...;SSL Mode=Require"` nos processos API, Web, Worker e Migrator.
3. Execute `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql`.
4. Execute `dotnet restore`, `dotnet build` e `dotnet test`; depois `dotnet run --project src/Hosts/Agro360.Api` e `dotnet run --project src/Hosts/Agro360.Web`.
Docker continua opcional e não participa desse fluxo.

## Fluxo homologável
1. Cadastre unidade industrial, linha, centro, turno, produtos e etapas no banco/API autorizado.
2. Em **Produção > Formulações**, selecione produto e insumos (nunca IDs digitados), salve a versão e aprove com `production.release`. Versão aprovada é imutável; mudanças criam uma versão.
3. Abra uma ordem com formulação aprovada, lote, quantidade, linha e responsável. Libere-a somente com itens; quando configurado, reserve estoque antes do início.
4. Aponte início/fim, operador, etapa, produzido/consumido, perdas, lote e temperatura crítica. Perda exige motivo.
5. Consuma o lote real. A transação bloqueia saldo insuficiente, lote vencido sem justificativa e lote bloqueado.
6. Registre parada com motivo e duração. Registre inspeção/laudo/evidência e libere ou bloqueie o lote.
7. Consulte a genealogia pelo número do lote, os custos persistidos e exporte ordens/formulações em CSV.

## Regras críticas
Ordens encerradas não mudam de estado; cancelamento exige motivo; conclusão requer apontamento, etapas críticas conformes e qualidade aprovada. Tucupi pode usar etapa `mandatory_for_tucupi` com tempo/temperatura; não conformidade mantém a ordem/lote bloqueados. Produtos amazônicos e exportáveis carregam requisitos de origem/documentos na configuração. Apontamento concluído é preservado (eventual correção usa estado `VOIDED`, sem exclusão física). Reprocesso referencia o lote original. Toda mudança de estado gera evento auditável.

## Integrações reais
Os contratos `IProductionStockGateway`, `IProductionAlertGateway`, `IProductionFinanceGateway` e `IProductionWorkflowGateway` definem fronteiras para estoque, alertas, controladoria e workflows. O consumo já usa a mesma transação PostgreSQL do saldo de lote. A contabilização ampliada de mão de obra/máquina depende dos respectivos cadastros e nunca inventa valores ausentes.

## Homologação
Validar tenant A/B, permissões por ação, formulação/versão, reserva, apontamento crítico, saldo/validade/bloqueio, perda, parada, qualidade, genealogia, custo, dashboard vazio e CSV. Confirmar que telas usam seletores e que API retorna Problem Details nos erros de domínio.
