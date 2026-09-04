# Diretrizes de UX do Agro360

## Princípios visuais

- Interface clara por padrão, fundo neutro, superfícies brancas/off-white, bordas suaves e sombras discretas.
- Verde é cor de apoio, foco e estado agro; não deve dominar grandes fundos.
- Contraste, foco visível, teclado, zoom de 200% e layouts de 320 a 1920 px são requisitos.
- Ícones devem ser coerentes e acompanhados por texto quando a ação não for inequívoca.

## Orientação em tela

Toda tela funcional recebe um `details` recolhível denominado **Como usar esta tela**, compacto no desktop e com comportamento de accordion no mobile. O conteúdo deve dizer: finalidade, dados a preencher, ações, regras relevantes e consequência após salvar/aprovar/cancelar.

Todo formulário deve:

- marcar obrigatórios com `*` e texto acessível;
- oferecer ajuda contextual para tenant, perfil, permissão, responsável, CPF/CNPJ, documento, data, valor, quantidade, unidade, status e centro de custo;
- apresentar exemplo sem usar valor real sensível;
- usar autocomplete/dropdown pesquisável para relacionamentos, nunca caixa de GUID;
- mostrar validação junto ao campo e resumo no topo.
- desabilitar o envio enquanto a requisição estiver ativa e restaurar o botão também em falhas de rede;
- aplicar máscaras apenas na apresentação, enviando documentos, telefone, CEP, moeda e quantidade normalizados para o backend;
- usar data ISO no transporte e data localizada na interface, sem inferir fuso silenciosamente.

Os formulários prioritários são login, tenant, usuário, perfil, fazenda, talhão, rebanho/lote, estoque, compra/fornecedor, pedido, produção, financeiro, documento e relatório. Novas telas devem reutilizar as primitivas de `forms.css` e o componente global de mensagens, em vez de criar comportamento isolado.

## Feedback e ações críticas

Sucesso, erro, alerta e sessão expirada usam mensagens humanas com próxima ação. Erros da API podem mostrar `traceId`, nunca stack trace. Excluir, cancelar, bloquear, reprovar, encerrar, mudar status crítico e revogar acesso usam modal de confirmação; justificativa é obrigatória quando a regra exigir.

Permissão negada, registro inexistente e módulo não contratado são estados orientativos, não falhas genéricas: informe por que a ação não está disponível e indique administrador, plano ou contexto que pode resolvê-la.

## Fluxo consistente

A experiência deve refletir a ordem do backend: cadastro → validação → persistência → auditoria → evento/log → mensagem → atualização do dashboard. A validação no cliente melhora a experiência, mas não substitui a regra no backend.
# Padrão das centrais guiadas

- Mostrar progresso com texto e barra acessível, sem depender apenas de cor.
- Em estados vazios, explicar o primeiro cadastro real que desbloqueia o fluxo.
- Próximas ações devem apontar para rotas existentes e nunca solicitar GUIDs.
- Erros da API devem preservar a mensagem clara e acrescentar o `traceId` como código de suporte quando fornecido.
- Em larguras até 760 px, indicadores e colunas de ação devem se reorganizar sem rolagem horizontal.

## Comercial Agro 360 (sprint atual)

Consulte `docs/COMMERCIAL-AGRO.md` para fluxo, regras implementadas, modelo persistente e pendências reais de integração.

## Baseline visual de recuperação

- O tema inicial é claro, com fundo neutro, cards brancos, bordas suaves e verde reservado a identidade, foco e status.
- Nenhuma página pode provocar overflow horizontal no documento; tabelas extensas devem rolar dentro de `.table-wrap`.
- Inicializadores globais devem tratar controles específicos de página como opcionais.
- O item correspondente à rota atual deve ser o único link ativo na navegação.
