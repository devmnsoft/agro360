# PWA Mobile

Acesse `/field` por HTTPS e use **Instalar aplicativo** no navegador. O manifest inicia na home de campo; o service worker cacheia apenas shell estático e o bootstrap autenticado separado. Respostas mutáveis e uploads nunca são armazenados no Cache API.

Para testar: abra DevTools > Application, confirme manifest/service worker; carregue a tela autenticado; em Network selecione Offline; crie um registro usando a busca por nome; confirme a pendência; volte a Online e sincronize. Teste câmera/arquivo e GPS em aparelho real, concedendo ou negando permissão. Botões têm alvo grande e o layout se adapta a celular/tablet.
