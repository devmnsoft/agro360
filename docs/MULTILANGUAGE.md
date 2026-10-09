# Multi-idioma

Culturas iniciais: `pt-BR` (fallback), `en-US` e `es-ES`. A escolha é persistida como preferência, aplicada ao atributo `lang` e deve reger recursos, validações, ajuda, datas e valores. Recursos ausentes sempre retornam `pt-BR`. CPF, CNPJ e códigos fiscais não são traduzidos; decimais são recebidos como números JSON e valores monetários usam `decimal`/`numeric`, nunca `double`.

## Sprint 46
A central oferece pt-BR, en-US e es-ES e persiste a cultura escolhida. Títulos, ajuda contextual, estados, validações e documentação devem usar chaves traduzíveis; conteúdo técnico preserva códigos de escopo/evento invariantes.

## Sprint 47 — CRM e ciclo do cliente

A plataforma integra CRM, pipeline, propostas com total no backend, contratos SaaS, implantação assistida, suporte/SLA, saúde explicável, conhecimento e portal isolado. As novas rotas exigem permissões específicas, as tabelas usam auditoria/RLS por tenant e toda comunicação sem provedor permanece pendente na outbox. A experiência responsiva usa funil, timeline, badges e o componente recolhível **Como usar esta tela**. Consulte `docs/CRM-COMMERCIAL.md`, `docs/CUSTOMER-SUCCESS.md`, `docs/SUPPORT.md` e a migração `047_crm_customer_lifecycle.sql`.

## Sprint 49 — processos
Templates persistidos aceitam apenas `pt-BR`, `en-US` e `es-ES`, com fallback controlado. Textos de ajuda e estados das novas telas devem usar recursos localizáveis; payload externo resolve primeiro o idioma preferido do usuário.

## Sprint 50 — formulários e ajuda contextual

Validação backend continua sendo a fonte da verdade; a interface oferece resumo e erros por campo, loading, confirmação com consequência real e motivo nas ações definidas pela regra. Ajuda curta é recolhível e localizada em pt-BR, en-US e es-ES. Configurações e eventos de UX usam as tabelas `agro360.ui_*`, com auditoria e RLS por tenant. Detalhes: `docs/UX-FORMS-VALIDATION.md` e `docs/CONTEXTUAL-HELP.md`.

## 2026-10-09 — resolução de cultura ponta a ponta (kernel + cliente)

- **Cadeia de resolução no servidor** (canônica, em `TenantContextMiddleware`, replicada em login/refresh/validação de sessão): `X-Culture` do pedido → preferência do usuário (`platform_user_preferences.language`) → idioma do tenant (`platform_tenant_settings.language`) → `platform_tenants.default_language` → `pt-BR`. O conjunto habilitado é `platform_languages.active`; a cultura canônica vem sempre do banco (casing autorizado) e a resolução nunca lança — cultura desconhecida simplesmente cai na cadeia.
- **`app.culture`**: cada transação de tenant reativa o GUC `app.culture` (setting já previsto no schema), permitindo regras dependentes de idioma sem parametrizar toda query.
- **Sessão carrega o idioma**: `AuthenticationResult`/`SessionValidationResult` devolvem `language`; o cliente persiste em `localStorage agro360.culture` (mesma convenção de `forms.js`/`saas.js`) e sincroniza o atributo `lang`.
- **Preferência explícita**: `GET/PUT api/v1/auth/preferences/language` usa `tenantId/userId` das claims; o `PUT` resolve por `SaasGovernanceRules.ResolveCulture` com queda silenciosa para `pt-BR` (nunca lança).
- **Cliente compartilhado** (`agro360.js`): resolvedor `agro360Culture()`, cabeçalho `X-Culture` em toda chamada da API, fábricas `Intl` (moeda BRL, números, tempo relativo) recriadas por idioma, seletor no topo (`_Layout`) e evento `agro360:culture` para os módulos re-renderizarem. Módulos delegam formatação ao idioma ativo (Começa por `procurement.js`; demais módulos seguem o mesmo padrão).
- **Invariantes**: idioma nunca altera permissões, plano, moeda contratual (BRL), tributação, UoM ou estado de negócio. Códigos persistidos (status, permissões, eventos) e contratos de API não são traduzidos — apenas rótulos de apresentação. Mensagens de erro mantêm `problem.Extensions["code"]` estável; o texto localizado nunca é chave de integração.
