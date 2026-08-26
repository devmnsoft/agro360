# Importação e exportação

O fluxo CSV é upload (`POST /api/imports`), validação (`/{id}/validate`), revisão dos erros por linha e confirmação atômica (`/{id}/confirm`). Tipos: produtores, propriedades, talhões, fornecedores, estoque, animais, máquinas e clientes. Arquivos inválidos nunca são confirmados. `GET /api/exports?entity=properties` entrega CSV UTF-8; produtores e clientes também são aceitos.
