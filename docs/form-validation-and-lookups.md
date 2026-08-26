# Validação de formulários e lookups

A tela da Sprint 15 usa `required`, tipos `date`/`number`, limites e `reportValidity()` no navegador. A API repete as restrições com Data Annotations e regras de domínio; o banco adiciona checks e chaves. Erros RFC Problem Details são exibidos junto ao formulário, o botão fica desabilitado durante envio e listas exibem loading, falha e estado vazio.

Nenhum formulário pede ID técnico. Produto, titular, responsável, propriedade, lote, comprador, template e demais relações são selecionados por listas pesquisáveis obtidas em `/api/lookups/{resource}`. A API recebe o UUID escolhido, mas o usuário vê rótulo contextual. Novos formulários devem seguir o mesmo padrão.

## Sprint 18
Os formulários cooperativos usam validação HTML (`required`, limites, tipo e faixas) e DataAnnotations no backend. Produtor, organização, classificação, propriedade, talhão, lote, animal, produto e fornecedor são sempre selects pesquisáveis/autocomplete; nenhum UUID é apresentado para digitação. Ações de aprovar, ativar, cancelar, encerrar e gerar repasse exigem confirmação.

## Sprint 19
A UI de RH Rural usa validação HTML (`required`, limites, tipos) antes do envio e Data Annotations/regras de domínio no backend. Relações são selecionadas por dropdown pesquisável (`data-lookup`) para pessoas, equipes, propriedades e recursos operacionais. Nenhum formulário solicita UUID/ID técnico digitável. Ações críticas exigem confirmação e todas as telas exibem loading, erro, vazio e filtros.
