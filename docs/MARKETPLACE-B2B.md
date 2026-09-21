# Marketplace B2B Agro360

O módulo Marketplace B2B do Agro360 conecta a oferta produtiva auditada da organização a compradores e clientes B2B qualificados, garantindo transparência, procedência e integridade operacional.

## Arquitetura e Catálogo

1. **Ofertas (`portal_marketplace_listings`)**:
   - Cada oferta anunciada vincula-se obrigatoriamente a uma cultura, produto ou lote de rastreabilidade certificado.
   - Informações públicas incluem produto, variedade, safra, quantidade disponível, unidade de medida, especificações técnicas e certificações vigentes (`portal_marketplace_listing_certificates`).
   - Preços de referência ou sob consulta; é estritamente vedada a exposição de custos de produção, margens internas ou dados fiscais protegidos.

2. **Cotações e Demandas (`portal_marketplace_quote_requests`)**:
   - Compradores e clientes externos autenticados podem solicitar cotações estruturadas para múltiplos itens.
   - Cada item (`portal_marketplace_quote_request_items`) especifica o produto desejado, volume, unidade de medida, especificações complementares e data limite de entrega.
   - O envio da cotação gera um evento auditado em `portal_marketplace_quote_events`.

## Regras de Negócio e Integridade Transacional

- **Não Simulação de Faturamento**: A solicitação de cotação externa entra no sistema com estado inicial `REQUESTED`. Ela não gera títulos no módulo financeiro, nem gera ordens de faturamento ou movimentação de estoque física até a homologação comercial formal na área administrativa interna.
- **Isolamento de Cotações**: Compradores visualizam estritamente as suas próprias cotações (`GET /api/portal/marketplace/my-quotes`) protegidas por RLS e vínculo do usuário autenticado.
- **Fornecedores e Compradores Bloqueados**: Usuários vinculados a parceiros comerciais com restrições cadastrais, pendências jurídicas ou bloqueios sanitários são sumariamente impedidos de emitir novas cotações (`PortalRules.ValidateSupplierPrequalification`).

## Endpoints do Portal

- `GET /api/portal/marketplace/catalog`: Consulta ao catálogo de produtos e safras ativas.
- `POST /api/portal/marketplace/quotes`: Submissão de nova solicitação de cotação comercial.
- `GET /api/portal/marketplace/my-quotes`: Histórico de cotações emitidas pelo usuário do portal.
