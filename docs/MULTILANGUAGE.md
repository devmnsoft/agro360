# Multi-idioma

Culturas iniciais: `pt-BR` (fallback), `en-US` e `es-ES`. A escolha é persistida como preferência, aplicada ao atributo `lang` e deve reger recursos, validações, ajuda, datas e valores. Recursos ausentes sempre retornam `pt-BR`. CPF, CNPJ e códigos fiscais não são traduzidos; decimais são recebidos como números JSON e valores monetários usam `decimal`/`numeric`, nunca `double`.

## Sprint 46
A central oferece pt-BR, en-US e es-ES e persiste a cultura escolhida. Títulos, ajuda contextual, estados, validações e documentação devem usar chaves traduzíveis; conteúdo técnico preserva códigos de escopo/evento invariantes.

## Sprint 47 — CRM e ciclo do cliente

A plataforma integra CRM, pipeline, propostas com total no backend, contratos SaaS, implantação assistida, suporte/SLA, saúde explicável, conhecimento e portal isolado. As novas rotas exigem permissões específicas, as tabelas usam auditoria/RLS por tenant e toda comunicação sem provedor permanece pendente na outbox. A experiência responsiva usa funil, timeline, badges e o componente recolhível **Como usar esta tela**. Consulte `docs/CRM-COMMERCIAL.md`, `docs/CUSTOMER-SUCCESS.md`, `docs/SUPPORT.md` e a migração `047_crm_customer_lifecycle.sql`.

## Sprint 49 — processos
Templates persistidos aceitam apenas `pt-BR`, `en-US` e `es-ES`, com fallback controlado. Textos de ajuda e estados das novas telas devem usar recursos localizáveis; payload externo resolve primeiro o idioma preferido do usuário.

## Sprint 50 — formulários e ajuda contextual

Validação backend continua sendo a fonte da verdade; a interface oferece resumo e erros por campo, loading, confirmação com consequência real e motivo nas ações definidas pela regra. Ajuda curta é recolhível e localizada em pt-BR, en-US e es-ES. Configurações e eventos de UX usam o schema `ui`, auditoria e RLS por tenant. Detalhes: `docs/UX-FORMS-VALIDATION.md` e `docs/CONTEXTUAL-HELP.md`.
