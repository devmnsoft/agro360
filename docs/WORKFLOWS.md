# Workflows inteligentes — Sprint 49

O motor evolui a central operacional existente com definições versionadas, etapas tipadas e instâncias/eventos auditáveis. Uma definição ativa é imutável: alterações geram versão em rascunho; ativação exige ao menos uma etapa. Etapas obrigatórias exigem justificativa para exceção, e evidência/comentário são validados antes da conclusão.

Todo acesso transacional define `app.tenant_id`; RLS e chaves compostas mantêm o isolamento. O super administrador MNSOFT pode consultar múltiplos tenants somente por rotas/políticas administrativas explícitas; a sessão de cliente nunca remove o filtro do tenant. A origem usa módulo e vínculo de entidade, sem solicitar UUID técnico em tela.

Tarefas cobrem aberta, em andamento, espera de terceiro/cliente, concluída, cancelada, atrasada e reaberta. Cancelamento e reabertura exigem motivo; conclusão exige relato. Aprovações registram solicitante, aprovador, decisão e horário, impedindo autoaprovação quando a segregação está habilitada.
