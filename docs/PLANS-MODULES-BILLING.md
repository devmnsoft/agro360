# Planos, módulos e cobrança gerencial

## Três controles independentes

1. **Plano** define o que pode ser comercializado e seus limites.
2. **Módulo contratado** concede o entitlement funcional do tenant.
3. **Permissão** autoriza uma ação do usuário dentro do módulo.

Uma feature flag é controle técnico e nunca cria contrato. A autorização de comando deve exigir simultaneamente tenant operacional, módulo `ACTIVE` e permissão. Downgrade ou bloqueio muda acesso, não exclui dados.

## Catálogo

A migration 097 registra 18 módulos, códigos estáveis, descrição, essencial/adicional, preço base opcional, permissões e ordem de menu. Dependências são relações normalizadas. `platform-base` é dependência de todos os adicionais. `saas_plan_modules` guarda disponibilidade e limite comercial; `saas_tenant_modules` guarda contratação efetiva e seu histórico fica em `saas_tenant_module_events`.

Planos comerciais recomendados: Starter, Professional, Enterprise e Custom/Portos/Cooperativas. Os valores devem ser decididos e publicados por pessoa autorizada; o sistema não inventa preços.

## Cobrança MNSOFT

A cobrança usa `decimal/numeric`, competência no primeiro dia do mês, vencimento, plano, módulos, valor base, adicionais, desconto e total. Estados: `DRAFT`, `ISSUED`, `PAID`, `OVERDUE`, `CANCELLED` e `NEGOTIATING` (o legado `OPEN` é preservado para upgrade). Pagamento manual requer data, valor e observação; cancelamento requer motivo. Cada mudança gera evento auditável.

Não existe provider homologado neste recorte. Portanto não há geração de boleto/PIX/cartão, emissão fiscal ou lançamento no financeiro operacional do tenant. Exportações CSV devem passar cada célula por proteção contra prefixos `=`, `+`, `-`, `@`, tab e retorno de carro.

## Pendências

* Conectar o entitlement normalizado a todos os command handlers.
* Concluir UI de associação plano-módulo e de cobrança/baixa.
* Validar RLS/grants e upgrade da migration 097 em PostgreSQL descartável.
* Executar testes E2E de downgrade preservando históricos.
