# Agro360 Campo — operações mobile

A Sprint 26 consolida `/field` como a área mobile-first para manejo, ocorrência, check-in, evidência e checklist. Os relacionamentos são escolhidos por catálogo pesquisável carregado para o tenant autenticado; a interface nunca solicita GUID.

## Uso

1. Acesse o Agro360 por HTTPS e abra **Campo Mobile** no menu.
2. Escolha uma operação, preencha os campos e autorize o GPS somente quando necessário.
3. Sem GPS, o check-in manual exige justificativa. Sem rede, o registro recebe estado **Pendente** no IndexedDB do aparelho.
4. Use **Sincronizar** após recuperar a conexão. Itens recusados ficam visíveis como **Falhou** e podem ser reenviados.

Fotos aceitas: JPG, PNG e WebP; documentos: PDF; limite de 10 MB. O backend recalcula SHA-256. Senhas e tokens não são persistidos na fila. A fila pertence ao perfil do navegador: não use aparelho compartilhado sem encerrar a sessão e limpar os dados locais.

## Instalação e homologação

Em HTTPS, use **Instalar aplicativo** no menu do navegador. O service worker mantém apenas o shell público/estático e o catálogo operacional; respostas de APIs privadas não são armazenadas como páginas. Homologue em 360 px, 390 px, tablet e desktop, incluindo modo avião, negação do GPS, arquivo inválido, retry e sessão expirada.
