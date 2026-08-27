# Motor de recomendações

Uma regra registra nome, descrição, módulo, tipo, condição JSON rastreável, severidade, peso, ação sugerida, periodicidade, permissão e estado. Regra inativa deve ser marcada `SKIPPED`; cada execução registra início, fim, quantidade e erro seguro. A chave `(tenant_id, fingerprint)` impede recomendação aberta duplicada.

Somente fatos persistidos podem gerar recomendação e toda ocorrência exige ao menos uma fonte. Regras críticas podem alimentar alertas. Geração de tarefa/workflow ocorre somente após aceite, permissão e seleção de responsável; a recomendação nunca executa ação destrutiva. Feature flag e plano devem ser verificados pelo orquestrador antes da execução.

Condições recomendadas incluem estoque abaixo do mínimo, atividade/manejo vencido, lote bloqueado, conta/pedido/entrega atrasados, desconto anormal, não conformidade crítica, certificado/requisito vencendo e falha recorrente fiscal ou de integração.
