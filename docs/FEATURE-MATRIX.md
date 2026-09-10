# Matriz de funcionalidades

> Resumo histórico preservado. A classificação canônica, incluindo a distinção entre implementação e homologação, está em [TRACEABILITY-MATRIX-v0.2.0.md](TRACEABILITY-MATRIX-v0.2.0.md).

| Área | Estado | Evidência principal | Limitação conhecida |
|---|---|---|---|
| Configuração PostgreSQL | Entregue | Chave canônica, conflito legado detectado, senha/passfile e diagnóstico sem segredo | A senha real é externa ao repositório |
| Login e refresh | Entregue | Identidade real, hash PBKDF2, tenant, papéis/permissões e rotação de refresh | Validação integrada exige PostgreSQL acessível |
| Cliente de sessão | Entregue nas páginas alteradas | Refresh concorrente único, uma repetição só para leitura, sessão preservada em 503/rede | Clientes legados de módulos serão migrados gradualmente para `agro360Api` |
| Administração MNSOFT | Entregue | Organizações, planos, módulos, cobranças internas, features e auditoria | Sem gateway de pagamento simulado |
| Administração da conta | Entregue | Usuários, perfis, convites, segurança, limites e upgrade | Envio externo do convite depende de integração de comunicação |
| Compras → estoque → financeiro | Entregue | Recebimento idempotente, entrada física, qualidade e parcelas abertas | Inspeções pendentes mantêm estado divergente até liberação |

Menus são apresentação. Tenant, contrato e permissões continuam aplicados pela API e pelo RLS do schema `agro360`.
