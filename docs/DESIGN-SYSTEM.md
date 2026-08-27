# Design System Agro360

O sistema usa uma linguagem SaaS B2B sóbria: superfícies escuras calmas, contraste alto, bordas discretas, tipografia legível e verde apenas para foco e ações. `agro360.css` é a fonte dos primitives compartilhados: shell, topbar, navegação, cards, botões, campos, tabelas responsivas, badges, modais, toast, loading e empty state.

## Regras de componentes

- Toda ação deve ser `button` ou link real, com foco visível e estado desabilitado durante processamento.
- Labels são obrigatórios; mensagens usam regiões `role=status`/`role=alert`. Não use placeholder como label.
- Relações usam select/autocomplete com rótulo humano. GUID e foreign key nunca aparecem ao operador.
- Tabelas ficam em `.table-wrap`; no mobile, células quebram linha. Formulários passam a uma coluna abaixo de 760 px.
- Empty states explicam o próximo passo sem inventar informação. Loading informa a operação em curso.
- Respeite `prefers-reduced-motion`, navegação por teclado, contraste WCAG AA e zoom de 200%.

## Checklist visual

Homologue em 360, 768, 1280 e 1920 px; temas claro/escuro; teclado; leitor de tela; vazio, carregando, sucesso, erro e permissão negada. Confirme alinhamento, alvo de toque, overflow de tabela e que todos os botões executam uma ação.
