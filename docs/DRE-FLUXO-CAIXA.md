# DRE gerencial e fluxo de caixa

O fluxo usa títulos abertos/parciais como **previsto** e baixas reais como **realizado**; cancelados não entram. A função `finance.cash_flow` aceita período e retorna entradas/saídas sem falhar quando vazia. Saldo previsto é recebível menos pagável, enquanto realizado deriva das baixas.

A DRE usa lançamentos reais classificados: receita bruta, deduções, receita líquida, custos diretos, margem bruta, despesas operacionais, resultado operacional, outras receitas/despesas e resultado gerencial. Itens sem classificação devem ser apresentados como **Não classificado**, nunca omitidos. Todos os filtros são submetidos com o tenant da sessão; divisão por zero retorna zero.

Na central, selecione datas e abra **Fluxo de caixa** ou **DRE gerencial**. O CSV é produzido localmente somente com o resultado autorizado já recebido. Para homologar, compare títulos, baixas e cancelamentos diretamente no PostgreSQL com um usuário de aplicação sujeito a RLS.
