# Sprint 7 — Pecuária 360

A Sprint 7 entrega fluxos multi-tenant reais para animais e lotes, pastagens/piquetes, manejo, sanidade, reprodução, nutrição e produção. Todos os comandos passam pela Application (`ILivestock360Service`), usam transação e queries Dapper parametrizadas na Infrastructure, RLS por tenant e auditoria.

## Fluxos

- **Rebanho:** identificação única no tenant, propriedade obrigatória, status, transferência com localização e histórico.
- **Pastagens:** seis estados, capacidade, descanso, ocupação e alertas de sobrelotação.
- **Manejo:** pesagem atualiza o animal; venda, mortalidade e descarte encerram o manejo normal.
- **Sanidade:** dose positiva, calendário, carência e consumo transacional do estoque.
- **Reprodução:** somente fêmeas, um ciclo ativo, previsão por espécie, aborto/parto encerram ciclo e parto pode cadastrar cria.
- **Nutrição:** dieta com itens, custo diário/por cabeça e fornecimento com baixa de estoque.
- **Produção:** leite e descarte, alerta de carência e GMD derivado de pesagens.
- **Dashboard:** `GET /api/livestock/dashboard` consolida os indicadores no banco, respeitando tenant.

A UI operacional exibe o painel Pecuária 360 e acessos responsivos. Autenticação e as policies `livestock.read`, `livestock.write` e `dashboard.read` protegem todos os endpoints. A versão atual do schema é `0.4.0`.
