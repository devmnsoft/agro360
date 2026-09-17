# Contrato do template Agro360 (shell)

Complementa `docs/DESIGN-SYSTEM.md`. Fonte canônica de CSS compartilhado:
`src/Hosts/Agro360.Web/wwwroot/css/agro360.css`. Evolução incremental desta
rodada: `wwwroot/css/shell-evolution.css` + `_Layout.cshtml`.

## 1. Princípio

O template é o sistema operacional do cliente, não um marketing site.
Tema padrão da página autenticada: **claro** (`data-theme="light"`), branco e
cinza neutro, verde só como acento. Login permanece identidade clara comercial.
Não reverter o shell inteiro para paleta escura sem decisão explícita.

## 2. Anatomia obrigatória

1. Skip-link “Ir para o conteúdo” visível no foco.
2. Sidebar com marca, contexto ativo, navegação agrupada e rodapé (sync + usuário).
3. Topbar com menu mobile, busca global (Ctrl/Cmd K), tema e alertas.
4. Faixa `assisted-context-banner` visível somente em modo SuperAdmin/assistido.
5. Breadcrumb `#page-breadcrumb` preenchido pela página ou por `agro360.js`.
6. Ajuda recolhível “Como usar esta tela”, sobrescritível pela section `ScreenHelp`.
7. `@RenderBody()` dentro de `main#main-content`.
8. Modal de login, command palette, toasts e diálogo de confirmação com motivo.

## 3. Grupos de navegação (operação do cliente)

Ordem estável. Itens sem permissão ou sem rota funcional permanecem no DOM
com `hidden` aplicado pelo JS existente; não remover `data-permissions`.

1. Visão geral — Dashboard, Tarefas e Alertas, Central de Implantação
2. Fazendas e unidades — Propriedades
3. Agricultura — Agricultura, Colheita e Recebimento, Custos da Safra
4. Pecuária — Pecuária
5. Produção agroindustrial — Produção
6. Estoque e almoxarifado — Estoque, Reposição
7. Compras — Compras e Fornecedores
8. Comercial — Comercial e Vendas, Pós-venda
9. Financeiro e controladoria — Financeiro e Custos
10. Fiscal e documentos — Fiscal, Documentos e Evidências
11. Logística — Expedição e Entrega
12. Qualidade e compliance — Qualidade, Modelos e Inspeções
13. Frota e manutenção — Frota
14. Relatórios e BI — Relatórios
15. Administração da conta — Usuários e Perfis, Meus módulos, Configurações
16. Ajuda — Manual
17. Administração MNSOFT — `/Saas` com `data-super-admin="true"` no topo, isolado

Não listar no menu rotas de catálogo comercial como se fossem operação.

## 4. Regras de componente no shell

- Alvo de toque ≥ 44 px; formulário em uma coluna ≤ 760 px.
- Cor nunca é o único sinal; badges têm texto.
- Empty state explica o próximo passo; loading usa texto + `aria-busy`.
- GUID/FK nunca aparecem no seletor.
- Ação destrutiva: diálogo nativo + motivo quando a regra exigir.
- `prefers-reduced-motion`, teclado, contraste AA, zoom 200%.
- Troca de tenant: limpar listas, abortar fetches e ignorar respostas atrasadas.

## 5. Slots para páginas

```html
@section ScreenHelp { ... }   <!-- substitui o texto genérico -->
@section Styles { }
@section Scripts { }
@section Head { }
```

A página define `ViewData["Title"]` e, se possível, `ViewData["Breadcrumb"]`
como texto humano (`Fazendas / Talhão Norte`). Sem IDs técnicos.

## 6. O que esta evolução não faz

- Não introduz outro frontend, design system externo ou biblioteca de ícones.
- Não cacheia API, health, OpenAPI ou dados autenticados no service worker.
- Não autoriza no cliente: menu oculto ≠ permissão.
- Não simula SuperAdmin operando como o usuário do cliente.
