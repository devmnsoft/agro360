# Sprint 11 — Agricultura 360

A Sprint 11 entrega uma API multi-tenant e uma interface responsiva para caderno de campo, planos agrícolas, monitoramento fitossanitário, recomendações, aplicações, irrigação, clima e ordens de serviço.

## Fluxos

A tela `/agriculture` alterna entre os oito módulos. Propriedade, talhão, safra, cultura, pessoa e máquina são pesquisados pelos nomes exibidos; o UUID selecionado permanece oculto e é enviado apenas no payload. O formulário apresenta validação por campo, resumo, carregamento dos lookups, estado vazio, bloqueio de duplo envio e mensagens de resultado.

Os recursos usam `GET/POST /api/agriculture/{module}` e `PUT /api/agriculture/{module}/{id}`. Planos aceitam `approve` e `revise`; ordens aceitam `start`, `pause`, `complete` e `cancel` no endpoint de transição. `GET /api/agriculture/dashboard` consolida safra, área, atividades, custos, ocorrências, aplicações, irrigações e alertas.

## Regras efetivas

- Plano exige safra, cultura, propriedade e talhões únicos; aprovado fica bloqueado até revisão.
- Caderno exige propriedade e talhão. Conclusão com insumo realiza baixa atômica do saldo.
- Aplicação exige produto, dose positiva e saldo disponível; indisponibilidade ou validade operacional bloqueia a ação.
- Monitoramento exige talhão e severidade controlada (`LOW`, `MEDIUM`, `HIGH`, `CRITICAL`); críticos aparecem no dashboard.
- Irrigação exige área positiva e período cronológico.
- Clima valida chuva (0–1000 mm), temperatura (-60–70 °C), umidade (0–100%) e vento (0–250 km/h).
- Conclusão de OS exige responsável e checklist, quando obrigatório. Cancelamento exige motivo.
- Alterações e transições preservam usuário, tenant, versão e histórico de status.
