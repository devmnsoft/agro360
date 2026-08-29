# Manutenção e Ordens de Serviço

Planos preventivos aceitam periodicidade positiva e checklist obrigatório. Datas/leituras vencidas e próximas são apuradas no dashboard. Plano inativo não gera OS.

A manutenção corretiva registra defeito, severidade, bloqueio, diagnóstico, peças, mão de obra, terceiro, conclusão e reabertura. Severidade/OS crítica altera o ativo para manutenção. A OS percorre `OPEN`, `PLANNED`, `IN_PROGRESS`, esperas, `COMPLETED`, `CANCELLED` e `REOPENED`; toda transição escreve a timeline do ativo. Conclusão sem descrição, cancelamento sem motivo e peça sem quantidade positiva são rejeitados.

Redução de odômetro/horímetro somente ocorre com justificativa e a permissão específica `fleet.meter.override`; a mudança fica auditada. Serviços externos podem referenciar conta a pagar e peças podem referenciar movimento de estoque. Se esses módulos não estiverem configurados, a referência permanece pendente — nenhuma baixa ou conta fictícia é criada.
