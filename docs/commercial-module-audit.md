# Auditoria do módulo Comercial

Data da auditoria: 2026-09-21.

## Escopo verificado

A auditoria cobriu as regras de domínio em `Domain/Commercial`, contratos da camada Application, serviços Dapper da Infrastructure, controllers da API, a página Razor comercial e as migrations de CRM, preços, atendimento, devoluções, estoque e contratos.

## Classificação

| Área | Estado | Evidência e observação |
|---|---|---|
| Regras de contratos, pedidos, CPF/CNPJ, preço e split | Pronto | `CommercialRules` centraliza validação e transições; regras adicionais cobrem saldo contratual, desconto especial, comissão e compliance. |
| Cadastro de cliente e CRM | Parcial | Cliente, segmento, documento, status, responsável e isolamento por tenant existem. A interface atual cria/edita clientes, mas ainda não oferece todos os filtros de cidade/UF nem uma tela dedicada de bloqueio. |
| Contratos | Parcial | Persistência, versionamento, idempotência, status e eventos auditáveis existem. A página única lista o recurso; formulários completos de edição e linha do tempo ainda dependem de evolução de UX. |
| Pedidos e política de preços | Parcial | Pedido avulso ou contratual, itens, tabela vigente, limite de desconto, saldo de contrato, eventos e tenant estão implementados. Aprovação dedicada de desconto absoluto ainda não está exposta pela aplicação. |
| Estoque, expedição e devolução | Parcial | Migrations e serviços próprios de estoque, fulfillment e pós-venda existem; não foi criada lógica comercial paralela. A orquestração completa pedido-reserva-expedição deve continuar nos serviços desses módulos. |
| Comissão e split | Pronto no backend | Cálculo, elegibilidade por status, persistência tenant-scoped, cancelamento e participantes de split existem. |
| Rastreabilidade e compliance amazônico | Parcial | Há módulos de rastreabilidade pública, genealogia e qualidade. O domínio comercial agora identifica produtos regionais que exigem rastreabilidade reforçada; a coleta das evidências permanece nos módulos proprietários. |
| Dashboard e Razor responsivo | Parcial | Dashboard, navegação por recursos, lookups sem digitação de IDs e bloco “Como usar” existem. Faltam jornadas Razor dedicadas para detalhe, aprovação, expedição e histórico. |
| SuperAdmin global | Não verificável sem execução manual | Consultas novas usam `tenant_id` e RLS. A visão cross-tenant depende do contexto global e das políticas de banco já existentes, que não foram alteradas nesta etapa. |
| Banco PostgreSQL | Pronto para o fluxo existente | As migrations 021, 066, 071, 088, 089, 098 e 099 cobrem CRM, snapshot de preço, fulfillment, reserva, devolução e contratos. Nenhuma tabela duplicada foi criada. |
| Execução integrada com PostgreSQL | Não verificável neste ambiente | Requer instância PostgreSQL com todas as migrations e credenciais de aplicação. |

## Decisões arquiteturais

- Regras determinísticas permanecem no domínio e não nos controllers ou views.
- Serviços Dapper filtram entidades por `tenant_id`; alterações de status e criações críticas escrevem eventos comerciais.
- Estoque, qualidade, genealogia e expedição continuam sob seus módulos existentes.
- A API pública read-only existente foi preservada; somente coleções privadas das transições usam tipos concretos para permitir otimização do analyzer.

## Pendências reais

1. Criar páginas Razor específicas para edição/detalhe de contratos e pedidos, aprovação comercial, reserva, expedição, entrega e devolução.
2. Modelar um workflow persistido de aprovação de desconto especial (solicitação, aprovador, justificativa e decisão), reutilizando permissões existentes.
3. Expor cidade/UF e classificação comercial nos comandos e filtros depois de confirmar a origem canônica desses dados de endereço.
4. Acrescentar testes de integração com PostgreSQL para concorrência de saldo contratual, RLS por tenant, reserva de lote e eventos de auditoria.
5. Validar manualmente a visão global do SuperAdmin e os fluxos responsivos em desktop, tablet e celular.
