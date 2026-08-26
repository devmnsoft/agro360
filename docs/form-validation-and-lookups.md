# Validação e lookups

Toda mutação valida novamente no backend. A página SaaS apresenta planos, perfis, usuários e organizações por nome; IDs viajam apenas como valores internos selecionados. É proibido `input` de `tenantId`, `roleId`, `planId` ou qualquer chave técnica. Formulários usam `required`, tipos semânticos, limites, mensagens acessíveis, confirmação para bloqueio/revogação e estados de loading, erro, vazio e sucesso.
