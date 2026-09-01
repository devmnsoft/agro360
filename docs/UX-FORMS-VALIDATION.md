# Formulários e validação — Sprint 50

## Contrato obrigatório

O backend é a fonte final da verdade. HTML `required`, tipos e máscaras apenas antecipam erros; commands e regras de domínio devem repetir obrigatoriedade, formato, tenant e permissão antes de persistir. `FormValidation` centraliza normalização de e-mail/documentos, CPF/CNPJ, decimal por cultura e intervalos de data.

Cada formulário recebe resumo acessível, mensagem junto ao campo, marca de obrigatório, `aria-busy` durante envio e preserva a resposta específica do endpoint. IDs/GUIDs técnicos não são campos de digitação: relacionamentos vêm de lookups filtrados pelo tenant. Valores monetários permanecem `decimal` e a cultura precisa ser explícita.

## Ações críticas

Elementos que executam endpoints reais usam `data-confirm-action`, `data-confirm-title`, `data-confirm-consequence` e, quando a regra exigir, `data-require-reason="true"`. O diálogo global não executa nada ao cancelar. Ao confirmar, repassa `reason`/`justification` ao formulário real; o backend ainda valida motivo, permissão, tenant, estado e registra auditoria.

## Mensagens e estados

Mensagens são sucesso, erro, atenção, informação, confirmação, bloqueio, permissão negada, validação, evento registrado ou ação crítica. Não exibem stack trace nem prometem persistência antes da resposta. Listas mantêm estados de loading, vazio e falha distintos.

## Checklist de implementação

1. DTO tipado, regra backend e teste de fronteira.
2. Endpoint protegido por policy e serviço limitado ao tenant.
3. Label visível, helper curto, mensagem de campo e summary.
4. Consequência real no diálogo para ação sensível; motivo quando exigido.
5. Feedback específico conforme status HTTP e foco no primeiro erro.
6. Teste em pt-BR, en-US e es-ES; teclado, mobile e reduced motion.
