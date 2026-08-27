# Marketplace Agro360 B2B

O catálogo publica somente `marketplace_listings` com status `AVAILABLE` e quantidade positiva. Filtros de produto, cultura, região, unidade e preço são parametrizados e sempre limitados ao tenant. Dados sensíveis do produtor e custos internos não pertencem ao read model público.

A ação **Solicitar cotação** grava cabeçalho e itens reais em uma transação. Cada quantidade deve ser positiva e não pode superar a disponibilidade atual. O protocolo pode então seguir por análise, proposta, aceite ou rejeição. Conversão em pedido depende do fluxo comercial homologado; pagamento online não é apresentado porque não há provedor contratado.

Certificações exibidas precisam estar emitidas e explicitamente associadas à oferta. Quando nenhum item atende aos filtros, o portal apresenta um estado vazio em vez de inventar estoque.
