# SLA de atendimento

Políticas combinam tenant/plano, contrato, categoria, prioridade e severidade. Os tempos de primeira resposta e resolução são minutos positivos. Na abertura, a política do tenant prevalece sobre uma global; sem política, aplica-se contingência de 480/2880 minutos.

`WAITING_CUSTOMER` pode pausar o relógio quando configurado; `WAITING_THIRD_PARTY` não pausa implicitamente. Toda alteração deve produzir `support_sla_events`. A central destaca chamados vencidos e percentual cumprido. Para homologar, crie políticas Start e Enterprise, abra chamados equivalentes, compare prazos e force um vencimento em ambiente de teste.
