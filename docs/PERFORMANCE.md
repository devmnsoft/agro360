# Performance — Sprint 48

Listagens de governança usam `LIMIT/OFFSET`, limite máximo de 100, total separado e filtros por tenant. Índices compostos cobrem tenant, status, severidade, usuário, duração e data. Dashboards devem usar agregações SQL e tolerar resultado vazio. Dapper recebe somente parâmetros; não concatenar filtros do usuário.

Consultas lentas podem ser registradas por nome lógico, módulo, duração, linhas e correlação — nunca SQL com valores sensíveis. Exportações são enfileiradas; arquivos grandes não devem ser materializados em memória. Antes da produção, definir SLO, retenção e rotina de análise com `EXPLAIN (ANALYZE, BUFFERS)` em ambiente seguro.
