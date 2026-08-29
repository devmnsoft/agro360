# Design System Agro360

## Componentes de decisão

Cards seguem título–motivo–fonte; badges incluem texto; score circular sempre acompanha valor; fatores usam timeline e estados vazios explicam o próximo passo. Cor nunca é o único sinal. Tabelas e ações devem funcionar a 320 px e por teclado.

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

## Padrão mobile de campo

Alvos de toque têm no mínimo 44 px; formulários passam a uma coluna em até 650 px; a navegação inferior permanece acessível em celular. Estados offline usam rótulos textuais além de cor (`Pendente`/`Falhou`) e tabelas operacionais devem virar cards sem scroll horizontal.

## Superfície externa premium

O Portal usa `portal.css`, identidade clara e comercial, largura máxima de 1240 px, cards de 16 px, foco visível e contraste sóbrio. Em até 800 px, grids viram cards em uma coluna e a navegação passa ao rodapé. Estados vazios explicam a próxima ação; skeletons são discretos; diálogos mantêm título, fechar, validação e feedback. Evite IDs técnicos, jargão administrativo, ações sem endpoint e disponibilidade fictícia.

## Sprint 28 — Qualidade e Compliance
Implementação persistente de requisitos configuráveis, especificações versionadas, inspeções, decisão auditável de lotes, não conformidades/CAPA, auditorias, beneficiamento e prontidão de exportação. Homologar regras e UX conforme [Qualidade e Compliance](QUALITY-COMPLIANCE.md) e [Prontidão para exportação](EXPORT-READINESS.md).

## Primitivas SaaS Sprint 29

Cards de plano usam hierarquia de preco, beneficios e CTA unico; status e beta usam badge textual, nunca somente cor. Uso combina valor, limite e barra com `aria-valuenow`. Timelines apresentam ator/data/motivo. Wizards conservam progresso, foco e resumo de erros. Dialogos criticos exigem motivo visivel. Tabelas viram cards em 360 px e todos os estados possuem loading, vazio e erro acionavel. Paletas white label precisam manter contraste WCAG AA.

## Central de integrações
Use hero calmo, cards responsivos, badges sem depender só de cor, timelines de tentativas e empty states explicativos. Tabelas viram cards no mobile; loading usa `aria-busy`, feedback usa `aria-live` e modais preservam foco/teclado. Nunca renderize segredo, XML ou payload sensível em cards.

## Atendimento premium (Sprint 33)

Cards com bordas discretas, fundo verde mineral, badges sem depender apenas de cor, timelines cronológicas e estados vazios humanizados formam o padrão da central. Formulários usam labels visíveis, foco nativo, mensagens em `role=alert`, grids que colapsam a uma coluna abaixo de 760 px e loading textual discreto. Ações destrutivas pedem motivo em modal; relacionamentos usam seletores pelo nome.

## SST Rural
Cards escuros calmos com realce verde representam conformidade; vermelho é reservado a criticidade real. Badges exibem nível e validade, tabelas têm overflow responsivo, formulários usam seletores por nome e estados vazios são explicativos. Validar teclado, contraste, 320/768/1440 px, loading discreto e mensagens em `role=status`.
