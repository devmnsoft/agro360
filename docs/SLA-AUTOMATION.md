# SLA, escalonamentos e automações — Sprint 49

Políticas combinam tenant, módulo, ocorrência e prioridade, com prazo em minutos, responsável, gestor, ação e timezone IANA. Vencimentos criam alerta; itens críticos escalam ao gestor e registram ator, motivo e horário. O cálculo deve converter do fuso do tenant/usuário para UTC apenas na persistência.

Automações aceitam somente gatilhos e ações enumerados. A condição é JSON declarativo; SQL e conteúdo executável são rejeitados. Regras ativas exigem condição, ações críticas exigem permissão e cada execução possui chave idempotente única por tenant/regra. Falhas ficam em `automation.executions` e não revertem a operação de origem.
