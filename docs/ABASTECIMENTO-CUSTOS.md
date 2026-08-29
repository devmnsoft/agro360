# Abastecimento e Custos Operacionais

O abastecimento seleciona ativo, operador, combustível, propriedade e centro de custo por lookup restrito ao tenant. Quantidade deve ser positiva, preço não pode ser negativo e `total = quantidade × valor unitário` é calculado no serviço. Leitura regressiva requer justificativa e permissão. Cancelamento exige motivo.

Cada abastecimento gera uma única linha de custo graças à chave `(tenant_id, origin_type, origin_id)`. A estrutura também recebe lubrificante, peça, terceiro, mão de obra, pneus, preventivas, corretivas, paradas e depreciação, com alocações por ativo, propriedade, safra, talhão, operação, rota, centro de custo, produto/cultura e cliente/pedido. O painel usa somente custos ativos e rastreáveis.

A integração real com estoque/financeiro usa referências de movimento/conta quando disponível. Sem configuração, fica pendente e não é simulada. Anomalias de consumo somente devem ser publicadas quando o motor de inteligência estiver ativo.
