# Sprint 6 — módulos operacionais

A versão 0.3.0 fecha o fluxo fornecedor → compra → recebimento → estoque e os fluxos máquina → manutenção/peças e máquina → abastecimento/combustível. Todos os registros pertencem ao tenant autenticado e as escritas críticas são transacionais e auditadas.

## Fluxos

* **Fornecedores:** cadastro, situação, categorias, contato, endereço, pesquisa e exclusão lógica.
* **Compras:** rascunho, envio, aprovação, cancelamento e recebimentos parciais/totais. O recebimento bloqueia pedidos cancelados e lança cada item no estoque.
* **Estoque/insumos:** entradas, saídas, ajustes com motivo, transferências, lote, validade, custo médio e alertas. Saldo negativo é bloqueado no banco sob bloqueio de linha.
* **Frota:** máquinas e equipamentos, identificação, medidores e situação operacional.
* **Manutenção:** ordem preventiva/corretiva, peças, mão de obra, agenda, conclusão e retorno configurável da máquina. Peças são baixadas na abertura.
* **Abastecimento:** baixa transacional do produto combustível, custo e leitura de horímetro/odômetro.
* **Dashboard:** doze indicadores consolidados em uma query Dapper.

A API REST está sob `/api` e exige JWT, claim de permissão e header `X-Tenant-Id` compatível com o token. A interface inicial mostra os indicadores operacionais e oferece navegação responsiva pelos módulos.

## Limitações conhecidas

Notificações externas de manutenção e importação de documentos fiscais não fazem parte da Sprint 6; alertas são consultáveis no dashboard. A unidade de consumo médio depende de pelo menos duas leituras coerentes do mesmo medidor.
