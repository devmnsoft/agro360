# Ajuda contextual

Toda tela principal deve apresentar um bloco recolhível **Como usar esta tela** antes da área operacional. O conteúdo descreve finalidade, ações, obrigatórios, filtros, permissões, consequência e próximo passo em poucas frases, sem dados inventados.

A configuração persistida em `ui.contextual_help` distingue página, módulo, cultura (`pt-BR`, `en-US`, `es-ES`) e público (`ADMIN`, `TENANT`, `OPERATIONAL`). Registros globais têm `tenant_id` nulo; conteúdo customizado só é lido dentro do tenant da sessão. O fallback web fornece orientação específica para módulos conhecidos e uma orientação operacional honesta para páginas legadas, sem criar ações.

Em mobile o painel permanece compacto. A ajuda nunca substitui autorização, validação backend ou documentação operacional e não deve afirmar que integrações externas funcionam quando não há provedor configurado.
