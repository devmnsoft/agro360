# Registro de execução — operações e materiais

## Estado verificado

- A solução usa .NET 10, Razor Pages com JavaScript/CSS nativos e PostgreSQL via Dapper.
- Ordens de campo já possuíam detalhe, recursos, apontamentos, custódia de materiais e transações tenant-scoped.
- `MaterialConsumptionCommand` possui uma única definição canônica em `InventoryContracts.cs`; a produção industrial usa o contrato distinto `ProductionMaterialConsumptionCommand`. Os testes de arquitetura cobrem a regressão de CS0101/CS8863.

## Lacuna e implementação

- O detalhe não retornava nem apresentava os eventos de material, e a interface não oferecia consumo ou estorno.
- Uma chave idempotente repetida era aceita mesmo com conteúdo divergente.
- A API aceitava movimentações após consolidação e não havia estorno explícito, imutável e vinculado ao consumo original.
- Esta entrega expõe o histórico, registra consumo, revalida estado/saldo na transação e implementa estorno integral único com justificativa, movimento compensatório e vínculo ao evento original.

## Evidência e limites da validação

- Validação estática: contrato canônico, SQL parametrizado, escopo por tenant, transação existente, índice único de estorno e testes de arquitetura.
- O ambiente da tarefa não contém o SDK .NET 10 nem uma instância PostgreSQL de testes. Restore, build, suíte .NET, aplicação da migration e percurso no navegador ficam explicitamente pendentes; esta execução não constitui homologação completa.
- A regra disponível não define estorno parcial nem conversão automática entre unidades. O estorno permanece integral e a unidade base cadastrada é preservada, sem conversões inventadas.
