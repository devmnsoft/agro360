# CRM Agro e Comercial — Sprint 22

## Visão funcional
O módulo `/Commercial` opera dados reais do tenant autenticado. As áreas disponíveis são clientes e prospects, contatos, representantes, funil, atividades, tabelas de preço, pedidos B2B, contratos, comissões, split interno, metas e dashboard. Filtros, busca e paginação consultam `/api/commercial`; relacionamentos são resolvidos por lookups pesquisáveis, sem exposição de GUIDs no formulário.

## Fluxos
1. Cadastre segmentos e representantes e crie o cliente (CPF/CNPJ opcional, e-mail e telefone validados).
2. Registre a oportunidade e mova-a por `NEW_LEAD`, `QUALIFICATION`, `PROPOSAL`, `NEGOTIATION`, `WON` ou `LOST`; perda exige motivo e cada transição gera histórico.
3. Crie o pedido com cliente ativo e ao menos um item. A API calcula o total, aplica o teto de desconto e permite aprovação somente a uma permissão superior. Mudanças persistem eventos para integrações financeiras/estoque.
4. Configure plano/regra de comissão, indicando exatamente percentual ou valor fixo. A chave única impede duplicidade por pedido, regra e representante.
5. Configure o acordo de split com participantes únicos. Cada participante usa percentual **ou** valor fixo; a soma percentual não pode superar 100%.

## Integrações e limites
`ORDER_APPROVED`, `ORDER_INVOICED` e cancelamentos são persistidos em `sales.commercial_events`, contrato interno durável para consumidores financeiro, estoque e logística. O split desta sprint é **controle contábil interno**; ele não transfere dinheiro e não declara integração bancária. A liberação por recebimento deve ser acionada apenas pelo módulo financeiro após a baixa real.

## Segurança
Todas as queries Dapper são parametrizadas e incluem `tenant_id`. RLS é habilitada nas entidades raiz. Policies distinguem leitura, aprovação de pedidos, exceção para cliente bloqueado, gestão de comissão e aprovação de split. Cliente inativo nunca recebe pedido; bloqueado depende de `commercial.customers.override-block`.

## Homologação
Execute o SQL completo, suba API e Web, autentique um tenant e valide: cliente/prospect; oportunidade perdida; atividade concluída; pedido vazio e desconto excedido; aprovação/cancelamento; comissão duplicada; split de 101% e válido; filtros e dashboard vazio/com dados. Confirme também que dois tenants não compartilham listagens nem lookups.
