# Portal Externo Agro360

O Portal Agro360 é uma superfície SaaS completamente segregada da administração interna. Tokens externos recebem estritamente a claim de escopo `portal.access` combinada com as claims do perfil externo atribuído (`agro360.portal_profile.*`). O token é terminantemente desprovido de qualquer permissão administrativa (`platform.admin`, `account.*`, `portal.manage`, etc.). Todas as consultas combinam o `tenant_id` com o usuário externo ativo e seus vínculos operacionais homologados.

## Perfis Homologados

1. **Produtor (`PRODUCER`)**: Gestão de safras, talhões, laudos agronômicos e cotações operacionais.
2. **Cooperado (`COOPERATIVE_MEMBER`)**: Visualização de entregas, cotas e comunicados cooperativos.
3. **Cliente B2B (`B2B_CUSTOMER`)**: Rastreabilidade de lotes, laudos de conformidade e pedidos.
4. **Comprador (`BUYER`)**: Consulta ao catálogo do marketplace, solicitação e acompanhamento de cotações reais.
5. **Fornecedor (`SUPPLIER`)**: Pré-qualificação cadastral e cotações autorizadas.
6. **Transportador (`CARRIER`)**: Registro de atualizações de entrega vinculadas exclusivamente às suas viagens.
7. **Representante Comercial (`COMMERCIAL_REP`)**: Acompanhamento de pedidos autorizados.
8. **Auditor Externo (`EXTERNAL_AUDITOR`)**: Acesso a laudos, dossiês e certificações de qualidade sem permissões de escrita.
9. **Técnico Parceiro (`TECHNICAL_CONSULTANT`)**: Acesso técnico consultivo às operações autorizadas.

## Ciclo de Vida do Acesso

1. **Geração de Convite Seguro (`POST /api/portal/invitations`)**:
   - Administrador autorizado seleciona tenant, perfil e entidade associada.
   - O serviço gera 384 bits aleatórios em base64url, persiste unicamente o hash SHA-256 no banco e agenda `PortalInvitationRequested` na outbox.
   - O segredo nunca é gravado em texto puro e não há envio simulado de e-mail sem provedor configurado.
2. **Primeiro Acesso e Aceite de Termos (`/Portal/Accept` / `POST /api/portal/accept`)**:
   - Validação do token de convite via hash contra expiração (7 dias padrão), revogação ou reutilização.
   - Aceite formal do termo de uso versionado vigente (`portal_terms`).
   - Definição de senha forte com validação em `PortalRules.ValidatePasswordStrength`: mínimo 10 caracteres, letra maiúscula, letra minúscula, número e caractere especial.
3. **Autenticação Segura (`/Portal/Login` / `POST /api/portal/login`)**:
   - Autenticação por slug do tenant, e-mail e senha com hash Argon2id / PBKDF2.
   - Retorno de JWT com escopo restrito (`portal.access`).
   - Auditoria persistida em `portal_external_audit_events`.
4. **Gestão de Perfil e Senha (`/Portal/Profile` / `POST /api/portal/change-password`)**:
   - Consulta de dados cadastrais e vínculos ativos.
   - Alteração de senha com confirmação da senha atual e validação de complexidade forte.

## Módulos e Autoatendimento

### 1. Dashboard por Perfil (`/Portal` / `GET /api/portal/dashboard`)
- Indicadores em tempo real baseados nos vínculos reais do usuário.
- Mural de comunicados oficiais filtrados por perfil (`portal_announcements`).
- Registro de leitura em `portal_announcement_reads`.
- Acesso rápido a solicitações e atalhos operacionais.

### 2. Central de Solicitações (`/Portal/Requests`)
- `GET /api/portal/requests`: Lista paginada e filtrável por status (`OPEN`, `IN_PROGRESS`, `RESOLVED`, `REJECTED`, `CANCELLED`).
- `POST /api/portal/requests`: Abertura de chamados (suporte, cotação, entrega, laudo, pré-qualificação, etc.) com upload e hash de anexos.
- `GET /api/portal/requests/{id}`: Detalhes com timeline cronológica completa de eventos e mensagens.
- `POST /api/portal/requests/{id}/cancel`: Cancelamento motivado com justificativa obrigatória auditada.

### 3. Rastreabilidade Pública e Segura (`/Portal/Traceability`)
- `GET /api/portal/traceability/{lotCode}`: Consulta operacional de lotes.
- **Higienização estrita**: São rigorosamente omitidos custos de produção, custos de insumos, margens, preços internos, `tenant_id`, GUIDs técnicos, operadores internos e inconformidades/NCs internas não resolvidas.
- Exibe dados públicos: cultura, variedade, safra, fazenda/talhão, datas de colheita/beneficiamento, timeline de etapas e selos/certificados vigentes.

### 4. Marketplace B2B Operacional (`/Portal/Marketplace`)
- `GET /api/portal/marketplace/catalog`: Catálogo filtrável por categoria, cultura e palavras-chave.
- `POST /api/portal/marketplace/quotes`: Envio de cotação com itens, quantidades e datas desejadas.
- `GET /api/portal/marketplace/my-quotes`: Consulta às cotações submetidas pelo usuário autenticado.
- **Regra de integridade**: O marketplace opera em fluxo de cotação (`REQUESTED`); não simula ordens de venda faturadas, nem títulos a receber ou pagamentos fictícios.

### 5. Documentos e Laudos Autorizados (`/Portal/Documents`)
- `GET /api/portal/documents`: Consulta a certificados, laudos e fichas autorizadas pelo tenant para as entidades vinculadas ao usuário (`portal_document_permissions`).
- `GET /api/portal/documents/{id}/download`: Download seguro auditado, verificando expiração e validade jurídica.

### 6. Central de Suporte (`/Portal/Support`)
- `GET /api/portal/support/articles`: Artigos públicos de autoatendimento organizados por categoria.
- Formulário integrado de abertura de chamado com vinculação direta à Central de Solicitações.

## Segurança e Governança no Banco de Dados

- **Row-Level Security (RLS)**: Todas as 26 tabelas `portal_*` possuem RLS ativo garantido por `agro360.platform_enable_tenant_rls(...)`.
- **Índices Operacionais**: Índices compostos por `(tenant_id, ...)` em todas as tabelas de alta cardinalidade para garantir performance sub-segundo.
- **Auditoria Imutável**: Registros de auditoria em `portal_external_audit_events` com trilha de data/hora UTC, IP, agente de usuário e metadados contextuais.
