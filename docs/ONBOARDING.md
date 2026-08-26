# Onboarding e implantação comercial

A área **Implantação** (`/Deployment`) conduz o administrador por organização, segmento, template, unidade produtiva, administrador, safra/ciclo, módulos, centros de custo, produtos e configurações iniciais opcionais. O formulário usa nomes comerciais e seletores; IDs internos nunca são solicitados.

## Fluxo homologável

1. Entre com um usuário que possua `deployment.read` e `deployment.write`.
2. Escolha o segmento e um dos cinco templates mantidos em `deployment.templates`.
3. Preencha a unidade, administrador e ciclo, selecione módulos e conclua.
4. Abra o checklist, confira o percentual calculado somente sobre itens obrigatórios e marque cada entrega após validá-la.
5. Em **Importar CSV**, escolha o tipo, arquivo delimitado por `;` e mapeie a coluna de nome. Revise erros linha a linha e só então confirme. Lotes confirmados aparecem no histórico; o endpoint de rollback mantém auditoria e não apaga a execução.

A API oferece `GET /api/deployment/templates`, `POST /api/deployment/onboarding`, checklist, dashboard e importações em `/api/deployment`. Pré-visualizações expiram em duas horas e nenhuma linha inválida pode ser confirmada.

## Templates

* `GRAINS`: soja/milho, operações, insumos, indicadores, centros de custo e pós-colheita.
* `LIVESTOCK`: categorias, manejo, sanidade, pesagem, reprodução, nutrição e indicadores.
* `AMAZON`: produtos regionais, beneficiamento, fervura, hash, lotes e logística regional.
* `COOPERATIVE`: cooperados, programas, coletivas, assistência, rateios e comissões.
* `AGROINDUSTRY`: recebimento, qualidade, processamento, lotes, expedição e dossiê.
