# SST Rural — Sprint 34

A Central SST reúne dashboard real, trabalhadores, funções, áreas, riscos, catálogo e entregas de EPI, treinamentos, controle administrativo de exames, incidentes/quase acidentes, investigações, planos e checklists. A API aplica tenant em toda consulta e gravação; relacionamentos são selecionados por nome na interface.

## Operação

1. Cadastre funções e áreas autorizadas; depois acesse **SST Rural > Trabalhadores**.
2. Em **Riscos**, informe severidade e probabilidade (1–5). O nível rastreável é `S × P`; 16 ou mais abre alerta.
3. Em **EPIs**, selecione trabalhador ativo e EPI ativo. Quantidade e datas são validadas e a entrega cria evento de histórico.
4. Catálogos, sessões e participantes representam treinamentos; validades vencidas alimentam dashboard.
5. Exames guardam apenas agenda, tipo, validade, resultado administrativo e documento autorizado.
6. Incidente grave exige investigação. Ações obrigatórias abertas impedem encerramento.
7. Checklist só conclui com todos os itens/evidências obrigatórios.

Alertas usam o mecanismo interno `operations.operational_alerts`; evidências referenciam documentos existentes. Não há integração oficial com eSocial, governo ou clínicas: sem provider real, qualquer comunicação externa permanece pendente/não configurada. Baixa de estoque de EPI também permanece pendente até um adaptador transacional ser configurado.
