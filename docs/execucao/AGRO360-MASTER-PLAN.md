# Agro360: prompt mestre de consolidacao SaaS e conclusao funcional

## Instrucao ao Codex Desktop

Trabalhe exclusivamente no checkout do Agro360, software da MNSOFT, cujo repositorio esperado e https://github.com/devmnsoft/agro360. Unifique as diretrizes anteriores neste plano executavel. Preserve funcionalidades existentes, corrija regressoes, complete fluxos parciais e implemente funcionalidades ausentes em etapas verificaveis. Nao trate esta tarefa como uma reescrita integral nem como simples producao de documentos.

Este documento define requisitos e uma sequencia proposta, nao comprova o estado do repositorio. Determine o que realmente existe por codigo, banco, execucao e documentos. Nao reutilize conclusoes ou namespaces do SIGOV-PLUS ou de outros produtos da MNSOFT.

## 1. Como executar agora e continuar depois

1. Leia AGENTS.md, README, configuracoes de SDK, solucao, projetos, CI e documentos de arquitetura e execucao. Registre branch, HEAD, remoto, tracking branch e estado do worktree. Preserve alteracoes anteriores do usuario.
2. Na primeira execucao, faca o inventario descrito abaixo e implemente a primeira entrega vertical prioritaria. Se a base estiver quebrada, a primeira entrega e recuperar inicializacao, login real, Swagger e uma pagina operacional. Se estiver saudavel, implemente o fluxo SaaS de maior prioridade ainda incompleto. Nao encerre apenas com planejamento quando for possivel implementar.
3. Em execucoes seguintes, consulte o checkpoint e o diff desde o HEAD registrado. Revalide o que mudou e continue a primeira atividade desbloqueada. Nao repita a leitura integral sem necessidade nem presuma que um prompt anterior foi executado.
4. Trabalhe em lotes completos: regra de negocio, persistencia, servico, autorizacao, endpoint, tela, validacao e verificacao. Evite iniciar varias telas ou modulos sem concluir seu fluxo principal.
5. Corrija bloqueios da entrega atual antes de avancar. Registre problemas independentes no backlog, sem transformar cada atividade em uma refatoracao global.
6. Termine cada execucao com uma entrega verificavel e checkpoint atualizado. Se houver bloqueio externo, documente evidencia, impacto e proximo comando; avance em trabalho independente quando seguro. Nao declare a etapa concluida com verificacoes essenciais pendentes.

## 2. Inventario e controle do escopo

Crie ou atualize documentos equivalentes aos seguintes, sem duplicar controles existentes:

- docs/execucao/FEATURE-MATRIX.md: inventario de todos os modulos e fluxos identificados.
- docs/execucao/EXECUTION-PLAN.md: backlog priorizado por dependencia e risco.
- docs/execucao/CHECKPOINT.md: estado atual, entregas verificadas, bloqueios e proxima atividade.
- docs/execucao/DECISIONS.md: decisoes de arquitetura e regras de negocio relevantes.

Para cada atividade use ID estavel e registre: modulo, jornada, finalidade, atores, precondicoes, estados/transicoes, validacoes, efeitos no banco, permissoes, dependencias, arquivos/evidencias, prioridade, criterio de aceite e verificacao executada. Separe funcionalidades apenas propostas das ja presentes no codigo.

Classifique o estado como: nao iniciado, parcial, com defeito, implementado nao validado, validado ou bloqueado. Percentuais de conclusao so podem vir de criterios cumpridos, nao de quantidade de arquivos ou telas criadas.

Mapeie pagina -> acao -> endpoint -> contrato -> servico -> consulta/tabela -> permissao -> modulo contratado. Identifique caminhos quebrados, duplicacoes e responsabilidades concorrentes. Inclua jobs, exportacoes, uploads e integracoes, nao apenas controllers.

Fixe o escopo da versao de conclusao com base nos modulos existentes, requisitos aprovados e frentes deste documento. Ideias adicionais vao para backlog futuro, sem aumentar indefinidamente a entrega em andamento. Recursos descritos como opcionais nao devem aparecer como entregues ou contrataveis antes de funcionarem.

## 3. Regras permanentes de engenharia

- Preserve stack e arquitetura reais do repositorio: ASP.NET Core, Razor quando existente, Dapper e PostgreSQL. Nao introduza Entity Framework, outro frontend ou novos frameworks sem necessidade demonstrada.
- Use queries parametrizadas, selecoes explicitas e DTOs com tipos/aliases compativeis. Parametros nao substituem identificadores SQL: tabelas e colunas dinamicas precisam de mapeamento fechado e autorizado.
- Defina o dono das transacoes. Operacoes acopladas devem usar a mesma transacao; integracoes externas devem ter estados persistidos, idempotencia e recuperacao. Nao marque sucesso antes de concluir os efeitos obrigatorios.
- Preserve tenant em consultas, comandos, caches, arquivos, filas, relatorios e tarefas agendadas. Nunca aceite tenant informado pelo cliente sem validar seu vinculo e autoridade.
- Nao implemente mock como funcionalidade real, botoes decorativos, indicadores inventados, pagamentos falsos, emissao fiscal simulada ou IA ficticia. Doubles de teste nao podem substituir servicos de producao.
- Use logs estruturados com TraceId, operacao, duracao e contexto seguro. Nao registre senha, hash, token, documento pessoal completo ou conteudo sensivel. Evite duplicar a mesma excecao em todas as camadas.
- Use try/catch quando houver recuperacao, traducao ou contexto util; mantenha inner exception, stack trace, cancelamento e rollback. Nao converta falhas em listas vazias ou respostas de sucesso.
- Nao desative analyzers, nullable ou testes validos para conseguir build. Formate arquivos alterados com o padrao existente; uma formatacao integral deve ser mecanica, separada e sem misturar refatoracao funcional.
- Nao crie classes de teste novas nesta primeira rodada, conforme orientacao anterior. Execute testes existentes e verificacoes reais de HTTP, banco e navegador. Registre cenarios de regressao e reutilize testes existentes quando adequado; a automacao adicional fica como atividade explicita posterior, sem fingir cobertura.
- Nao altere infraestrutura de producao, envie cobrancas reais ou mensagens para terceiros durante verificacoes. Use ambiente descartavel ou homologacao explicitamente configurado.

## 4. Modelo SaaS obrigatorio

Separe identidade global da pessoa, organizacao/tenant, unidade operacional, vinculo da pessoa com a organizacao, perfil, permissao, modulo, funcionalidade, contrato e cobranca. Reaproveite entidades existentes e migre os dados com seguranca.

Uma pessoa pode ter varios vinculos, com perfis e unidades diferentes. Bloquear um vinculo nao deve bloquear automaticamente a identidade em outros clientes. Para usuario de cliente, permita a operacao apenas quando identidade, vinculo, organizacao, contrato, funcionalidade e permissao aplicavel estiverem validos. A politica deve ser aplicada no backend e refletida no menu.

Feature flag controla disponibilidade tecnica; nao substitui contratacao. Contratacao libera possibilidades ao cliente; nao concede automaticamente todas as permissoes a seus funcionarios. Modulo dependente deve declarar requisitos e custos antes da contratacao, sem liberacao comercial silenciosa.

### Superadministrador MNSOFT

- Manter um unico superadministrador ativo, com garantia contra corrida de criacao, provisionamento controlado, MFA real e procedimento seguro de recuperacao e transferencia. Nao basta ocultar o perfil no frontend.
- Entregar administracao global de clientes, usuarios, perfis, contratos, precos, modulos, cobrancas, limites, suspensoes, auditoria e uso real do sistema.
- Permitir criar, editar, inativar, bloquear e desbloquear conforme regra. Proteger o ultimo acesso administrativo e impedir que administradores de clientes criem ou atribuam papel global.
- Permitir acesso a todas as telas e modulos em modo administrativo explicito. Ao operar dados de um cliente, exigir contexto selecionado, faixa visual permanente e auditoria do ator real. Nunca simular que o funcionario do cliente executou a acao.
- Para acesso assistido, registrar inicio, fim, motivo e cliente; exigir elevacao para operacoes sensiveis. Auditoria deve permitir distinguir administracao global de operacao normal. Nao permitir que um token comum escolha arbitrariamente esse modo.
- Acesso global nao autoriza quebrar estoque, transicoes, segregacao, integridade ou historico. Exclusao fisica apenas quando cabivel e sem dependencias; preferir inativacao/cancelamento para dados transacionais. Preservar auditoria.

### Administracao da conta cliente

- Entregar Minha Empresa, Unidades, Usuarios, Convites, Perfis e Permissoes, Contratos e Modulos, Cobrancas, Seguranca, Preferencias e Auditoria.
- Permitir cadastro, convite, edicao e bloqueio de usuarios do proprio cliente, com limites contratados. Diferenciar cargo organizacional de perfil de autorizacao.
- Permissoes por acao: consultar, criar, editar, aprovar, cancelar, exportar e administrar. Restringir por unidade quando aplicavel; oferecer perfis iniciais editaveis.
- Impedir autoelevacao, concessao alem da autoridade do administrador, referencia a perfis de outro tenant e remocao do ultimo administrador ativo. Atualizar autorizacao e invalidar caches/sessoes conforme o efeito da alteracao.

### Identificacao, login e sessao

- Autenticar pelo banco com hash real e mecanismo de seguranca existente. Permitir e-mail verificado, inclusive institucional, ou CPF cadastrado. O dominio do e-mail nao concede autoridade sobre uma empresa.
- CNPJ identifica a organizacao, nao um funcionario. Se informado, resolver o contexto empresarial e exigir identificacao pessoal e senha. Nao criar conta compartilhada obrigatoria para todos os funcionarios.
- Normalizar identificadores sem perder informacao valida. Conferir formatos aplicaveis em fontes oficiais antes de implementar validadores que possam ficar obsoletos; nao presumir que todo documento empresarial deve sempre conter apenas digitos.
- Selecionar organizacao apos autenticacao quando houver varios vinculos. Proteger contra enumeracao de contas, abuso, forca bruta, redirecionamento aberto e escalada de privilegios.
- Revisar convite, recuperacao, logout, expiracao e refresh. Rotacao de token deve tratar reutilizacao, concorrencia e revogacao de forma transacional; frontend deve evitar renovacoes simultaneas e loops de retry.
- Diferenciar autenticacao expirada de proibicao de uma operacao. Tratar indisponibilidade de rede separadamente. Nao usar a disponibilidade do Swagger como unica prova de saude da API.
- Conta bloqueada por seguranca nao autentica normalmente. Inadimplencia pode permitir area restrita de regularizacao, suporte e cobranca, segundo politica. Nao impedir o cliente de resolver a propria suspensao.

### Catalogo, contratacao e cobranca

- Catalogo com codigo estavel, descricao, funcionalidades, dependencias, preco, moeda, periodicidade, limites, trial e disponibilidade. Modulos incompletos nao podem ser vendidos como operacionais.
- Permitir escolha de modulos e pacotes, resumo de valores, condicoes, aceite, contratacao, renovacao, upgrade, downgrade e cancelamento. Calcular no servidor com decimal e preservar versao de preco/condicoes aceitas.
- Distinguir estados do contrato, acesso e pagamento. Definir inicio/fim da vigencia, carencia, suspensao e reativacao; transicoes devem ter precondicoes, responsavel e auditoria.
- Mudancas devem informar quando entram em vigor, eventual proporcionalidade e impacto nos limites. Reducao de limite nao apaga usuarios nem dados automaticamente. Definir comportamento quando o consumo atual excede o novo limite.
- Cobranca do SaaS MNSOFT e diferente do financeiro operacional da fazenda. Manter proprietarios, permissoes e consultas separados, ainda que reutilizem componentes tecnicos.
- Sem provedor real, registrar cobranca interna pendente e permitir conciliacao manual autorizada com evidencia; nao gerar boleto/PIX falso. Com provedor, validar assinatura de webhook, eventos repetidos/fora de ordem, estorno e conciliacao.
- Excecoes comerciais precisam de motivo, validade, aprovador e auditoria. Nao habilitar modulos com um booleano arbitrario sem origem contratual ou concessao formal.

## 5. Design e navegacao comuns a todas as etapas

Preserve identidade Agro360/MNSOFT e estabilize o template antes de modifica-lo. Evolua componentes compartilhados de forma incremental, com tema claro, branco/cinza neutro, texto legivel e verde como acento. Evite paginas muito escuras, fundos excessivamente verdes e decoracao sem funcao.

Separe os ambientes Administracao MNSOFT e Operacao do Cliente. Na operacao, agrupe menus na sequencia: Visao Geral; Fazendas e Unidades; Agricultura; Pecuaria; Producao Agroindustrial; Estoque e Almoxarifado; Compras; Comercial e CRM; Financeiro e Controladoria; Fiscal e Documentos; Logistica; Qualidade e Compliance; Frota e Manutencao; Relatorios e BI; Administracao da Conta; Ajuda. Acomode outros modulos encontrados no inventario em grupos coerentes, sem elimina-los.

Exiba somente rotas autorizadas e funcionais. Catalogo comercial fica separado. Use breadcrumbs, pagina ativa, pesquisa de menu e organizacao atual. Troca de cliente deve limpar dados e caches anteriores e impedir que respostas atrasadas sobrescrevam a tela do novo contexto.

Para cada pagina, entregue ajuda contextual pelo acesso "Como usar esta tela", com finalidade, pre-requisitos, sequencia do fluxo, efeitos das acoes e significado dos estados. Integre ao manual; mantenha a explicacao longa fora da area principal de trabalho. Campos precisam de rotulos claros e ajuda acessivel por teclado/toque, nao apenas hover.

Formularios devem seguir a ordem do trabalho, preservar preenchimento apos falha, validar no cliente e servidor, indicar obrigatoriedade, relacionar erros aos campos e bloquear duplo envio. Validar datas, intervalos, dinheiro, quantidades, unidades, duplicidade e relacionamentos conforme o contexto. Nunca exigir GUID ou FK manual: usar seletores pesquisaveis autorizados.

Use pop-ups acessiveis para erros/alertas, toast para sucesso e confirmacao para acoes criticas. Nao pedir confirmacao para toda navegacao. Implementar foco, fechamento por teclado, mensagens sem stack trace, referencia de atendimento e tratamento distinto de rede, validacao, autenticacao e autorizacao.

Verifique todas as acoes existentes: novo, editar, salvar, cancelar, filtrar, ordenar, paginar, exportar e voltar. Corrija botoes sem endpoint, links quebrados, menus duplicados, icones ausentes, assets 404 e seções Razor nao renderizadas. Sem binarios novos no Git; use icones existentes e imagens por URL quando necessarias, com alternativa para falha.

## 6. Etapas de implementacao e conclusao

Cada etapa inclui backend, banco, tela, permissao, ajuda, verificacao e documentacao. Reaproveite o que estiver validado; complete apenas lacunas reais. A numeracao abaixo e do plano, nao altera automaticamente os numeros de sprint existentes.

### E0 - Recuperacao e inventario

Atividades: executar restore/build/test; iniciar API e Web; corrigir bloqueios de configuracao, rotas, Swagger, DI, Razor, login, conexao e schema; gerar matriz e backlog. Investigar conflitos de porta e origem da URL sem matar processos desconhecidos. Ajustar CORS e HTTPS sem liberar indiscriminadamente origens ou desabilitar verificacao TLS.

Revisao dirigida dos bugs anteriores: tipos/records duplicados; lambdas Task versus Task<T>; nulabilidade; construtores Dapper; DateTime/DateTimeOffset e null; aliases; parametros SQL; colunas inexistentes; regex e parametros reservados em rotas; colisao de metodo/caminho no Swagger; seções Styles/Scripts; registros DI e scripts frontend. Nao mascarar rotas duplicadas escolhendo a primeira no Swagger.

Aceite: API/Web iniciam; documento OpenAPI gera sem 500; login consulta banco; pagina inicial renderiza; erros detectados tem causa e status documentados. Compilacao sozinha nao encerra E0.

### E1 - Identidade, cliente e autorizacao

Atividades: consolidar entidades/vinculos; provisionar superadministrador com MFA; implementar conta cliente, usuarios, convites e perfis; login por identidade e contexto empresarial; sessao/refresh/logout; politica central de acesso e menus. Aplicar a politica aos caminhos reais, incluindo busca e exportacao.

Aceite: superadministrador acessa contexto auditado de dois clientes; cada cliente enxerga somente seus dados; usuario restrito recebe negativa tambem pela URL/API; bloqueio de vinculo, troca de perfil e troca de tenant produzem efeito consistente. Nao modificar producao para executar estes cenarios.

### E2 - Catalogo, contratos e administracao global

Atividades: precos e pacotes; dependencias; selecao e aceite; contrato e vigencia; concessoes; limites; telas globais de cliente/usuario/perfil/modulo; painel de uso real. Diferenciar contratado, habilitado e utilizado. Definir periodo e fonte de cada indicador.

Aceite: cliente escolhe modulos e visualiza total calculado no servidor; contratacao valida habilita somente o previsto; perfil continua restringindo acoes; alterar preco nao muda contratos anteriores; modulo nao contratado e negado no backend; painel usa dados reais e funciona vazio.

### E3 - Cobranca e ciclo comercial SaaS

Atividades: faturas/cobrancas internas, vencimentos, conciliacao, carencia, suspensao, reativacao, upgrade/downgrade e cancelamento; integracao de pagamento somente se configurada. Impedir duplicidade por retries e jobs concorrentes. Auditar excecoes e separar financeiro da plataforma do financeiro rural.

Aceite: cobranca pendente nao aparece paga; confirmacao/conciliacao valida atualiza contrato conforme politica; evento repetido nao duplica efeitos; cliente suspenso tem apenas acesso permitido para regularizacao; cancelamento preserva historico e dados.

### E4 - Cadastros estruturantes e estoque

Atividades: concluir fazendas/unidades, produtores/parceiros, talhoes, safras, culturas, produtos/insumos, unidades de medida, centros de custo, depositos, lotes, validade, movimentos, reservas e inventario conforme cadastros existentes. Evitar duplicar fornecedor/parceiro/produto em modulos diferentes.

Regras: validacao dimensional nas conversoes; saldo disponivel considera reservas e bloqueios; movimentacao tem origem e responsavel; estorno por movimento compensatorio; nao permitir duplo consumo concorrente. Definir politica de estoque negativo explicitamente.

Aceite: entrada, reserva, baixa, transferencia e ajuste conferem com o historico, inclusive com chamadas repetidas e concorrentes; saldos e seletores respeitam tenant/unidade; itens de servico nao geram estoque fisico.

### E5 - Compras, comercial e financeiro operacional

Atividades: fechar fornecedor/homologacao -> requisicao -> cotacao -> comparacao -> alcada -> pedido -> recebimento parcial/total -> estoque -> previsao financeira. Fechar oportunidade -> proposta -> pedido de venda -> reserva -> entrega -> titulo financeiro, reaproveitando os contratos existentes.

Regras: fornecedor bloqueado, justificativa de escolha, autoaprovacao, divergencias, lote/validade, saldo pendente, cancelamento e reabertura. Financeiro com parcelas, vencimentos, baixas parciais, estorno e conciliacao; soma de parcelas igual ao total; previsao nao e pagamento. Origem unica e idempotente para titulos e movimentos.

Aceite: compra recebida produz entrada unica e obrigacao financeira correta; venda nao entrega mais que disponivel sem regra expressa; recebimento/pagamento real atualiza saldos uma unica vez; cancelamento nao apaga historico e exige tratamento dos efeitos anteriores.

### E6 - Agricultura e pecuaria

Atividades: concluir planejamento de safra/talhao, operacoes, aplicacoes, consumo, colheita, produtividade e custos. Concluir animais/lotes, origem, movimentacao, manejo, pesagem, sanidade, reproducao e destino quando previstos. Incluir leite, pastagens, floresta ou outras especialidades se constarem nos requisitos/inventario, com tarefas proprias.

Regras: datas coerentes, unidade/area/quantidade, rastreabilidade de insumo e lote, conversoes justificadas, transicoes de animal/lote e bloqueios operacionais. Parametros tecnicos e requisitos regulados devem ter fonte, versao e validacao competente; nao inventar dosagem ou protocolo sanitario.

Aceite: operacao rural percorre planejamento, apontamento, consumo, custo e resultado; manejo preserva historico individual/coletivo; dashboards usam a mesma fonte transacional e tratam ausencia de dados sem inventar resultados.

### E7 - Producao agroindustrial e qualidade

Atividades: receitas/formulacoes versionadas, ordem, reserva, consumo real, apontamento, perdas, refugos, paradas, produto acabado, custo e rastreabilidade. Fechar inspecao, nao conformidade, bloqueio, liberacao e reprocesso com documentos/evidencias reais.

Regras: receita aprovada imutavel; ordem tem estados autorizados; conclusao exige apontamentos e inspecoes obrigatorias; bloqueio impede uso/venda/expedicao; reprocesso preserva origem; perdas e rendimentos usam unidades compativeis e nao duplicam custos. Parametros de processo critico precisam ser configurados/validados, nunca inventados.

Aceite: materia-prima recebida pode ser rastreada ate o lote acabado e seu destino; consumo e producao sao consistentes; reprovacao bloqueia os fluxos dependentes; falha transacional nao deixa estoque e ordem divergentes.

### E8 - Logistica, frota, documentos e fiscal

Atividades: separar, expedir, transportar, entregar e registrar ocorrencias/prova de entrega; frota, abastecimento, manutencao, pecas e custos; documentos, validade e evidencias; integrar emissao fiscal existente somente com contratos e provedores reais.

Regras: entrega parcial mantem saldo; cancelamento trata reserva/movimentacao; documento obrigatorio ou qualidade bloqueada impede etapa configurada; emissao fiscal deve distinguir solicitacao, processamento, autorizacao, rejeicao e cancelamento. Frete/servico nao pode duplicar custo ja apropriado.

Aceite: pedido percorre expedicao e entrega com rastreabilidade e impactos coerentes; manutencao utiliza pecas sem duplicar baixa; documento e acessado apenas por quem tem permissao. Sem provedor fiscal, registrar impedimento real e nao considerar emissao entregue.

### E9 - Mobile/offline, idiomas, BI e integracoes

Atividades: fechar captura no campo, evidencias, sincronizacao, identidade de dispositivo/sessao, fila persistida, conflitos e idempotencia; centralizar textos pt-BR/en/es, formatos, moeda e fuso; concluir relatorios, filtros, CSV e indicadores por tenant/unidade/safra/modulo.

Regras: comando offline nao pode aplicar permissoes antigas sem revalidacao; logout/troca de cliente nao pode expor fila ou dados do cliente anterior; conflito precisa de resolucao explicita. Traducao nao muda calculo monetario nem formato de intercambio de dados. Indicadores possuem definicao e fonte; CSV respeita filtros, escaping e protecao contra formulas indevidas.

Planeje IoT, integracoes especializadas, exportacao/trading e inteligencia Agro360 se aprovados ou ja iniciados. Para cada um, registre provedor/dados necessarios, limites, custo, permissoes e aceite. Sem capacidade real, permaneça como pendencia identificada; nao publicar IA, previsoes ou conexoes ficticias.

Aceite: fluxo de campo demonstrado online/offline e apos reconexao; idiomas sem chaves expostas; dashboards conciliados com dados operacionais; exportacoes autorizadas. Itens sem insumos externos ficam bloqueados de forma explicita, nao "concluidos via interface".

### E10 - Fechamento da versao e operacao

Atividades: percorrer a matriz completa; finalizar lacunas dos modulos presentes nao nomeados acima, inclusive suporte, RH/SST, portal, workflows e notificacoes quando existirem; verificar integracoes cruzadas, carga representativa, acessibilidade, seguranca, backup/restauracao e atualizacao de versao.

Aceite: todo requisito do escopo fechado esta validado com evidencia ou removido formalmente da versao e mantido no backlog. Recursos bloqueados nao sao anunciados como disponiveis. Documentar riscos residuais, procedimentos operacionais e regressao; nao prometer ausencia absoluta de bugs.

## 7. Banco de dados e demonstracao

Atualize database/agro360-postgres-full.sql com todos os objetos da aplicacao no schema agro360. Nao crie subschemas ficticios. Preserve schemas do sistema/extensoes quando tecnicamente necessarios e documente excecoes; nao mova objetos de extensoes cegamente.

Revise estruturas existentes antes de criar tenants, unidades, settings, users, memberships, perfis/permissoes, catalogo/funcionalidades/precos, contratos/itens, cobrancas, flags, auditoria, convites, sessoes e uso. Os nomes finais seguem o padrao real do projeto. Identidades e catalogos globais nao devem receber tenant_id artificial; dados de cliente devem ter isolamento explicito, inclusive FKs coerentes entre tenants.

Entregue script de instalacao limpa e caminho de migracao incremental versionado, preservando dados e checksums de migracoes ja aplicadas. Ao consolidar schemas, mapeie colisoes de nomes e atualize views, funcoes, FKs, politicas, jobs e SQL da aplicacao. Nao use simples substituicao textual como prova de migracao correta.

Valide ordem de criacao/inserts, constraints, unicidade, indices, defaults, tipos monetarios/temporais, conversoes, permissao do usuario de banco, seeds idempotentes e rollback. IF NOT EXISTS nao substitui conferir se uma tabela existente possui a estrutura correta. Escolha indice pelo tipo e operadores realmente usados; nao aplicar GiST a JSONB sem suporte apropriado.

Execute em banco descartavel vazio, repita seeds/scripts quando previsto e execute migracao sobre uma base de versao anterior com dados de exemplo. Compare contagens e vinculos e valide queries reais do sistema. Use SQL textual via Query Tool/psql; nao chamar pg_restore sobre SQL simples nem entregar arquivo binario como requisito.

Disponibilize seed de demonstracao explicito, desligado por padrao em producao: Fazenda Santa Clara, slug santa-clara, admin@santaclara.agro360.local, perfil administrador do cliente e contratos demo dos modulos efetivamente disponiveis. Acrescente operador restrito e um segundo tenant de homologacao para verificar isolamento. Nao afirmar que um documento sintetico e inexistente no mundo real; use fixtures controladas sem dados de terceiros e respeite o modelo de cadastro.

Provisione superadmin@mnsoft.com.br separadamente. Senhas temporarias devem ser fornecidas/geradas localmente, nunca constantes universais versionadas; persistir apenas hash real, exigir troca inicial e nao redefinir senhas a cada seed. Registrar no relatorio os logins e como obter a senha no ambiente seguro, sem publicá-la no Git, PR ou logs. Nao enviar e-mail para enderecos demo. So afirmar que a conta funciona apos autenticar de verdade.

## 8. Verificacao e portas de qualidade

- Rodar dotnet restore, dotnet build e dotnet test conforme a solucao e configuracoes reais. Verificar formatacao com dotnet format --verify-no-changes quando compativel com o padrao do repositorio. Executar novamente apos ajustes relevantes e integracao Git.
- Validar no navegador login, selecao de organizacao, menu por perfil, operacao real, validacao de formulario, pop-up, logout e responsividade. Conferir console, rede, assets e imagens; capturas locais sao evidencias, nao binarios a adicionar ao repositorio.
- Validar chamadas HTTP diretas negadas por falta de permissao/contrato/tenant; arquivos, exports, buscas e jobs tambem devem ser abrangidos. Verificar acesso administrativo separadamente.
- Validar instalacao/migracao SQL e materializacao Dapper usando PostgreSQL real de homologacao. Testar retornos vazios, nulos, volumes representativos, datas e culturas relevantes. Nao substituir esse aceite apenas por busca textual.
- Exercitar repeticao, concorrencia, rollback, cancelamento e indisponibilidade nos fluxos financeiros, estoque, contratacao e sessao. Nao deixar confirmacoes externas ou efeitos nao transacionais sem reconciliacao.
- Registrar comando/cenario, ambiente, resultado e pendencias. Se SDK, banco, navegador, provedor ou credencial estiverem indisponiveis, registrar como nao verificado e fornecer instrucao exata de retomada.

## 9. Documentacao, Git e relatorio de continuidade

Atualizar README.md, docs/ROADMAP.md, docs/SPRINTS.md, docs/REQUIREMENTS.md, docs/SYSTEM-MANUAL.md, docs/SAAS-ADMINISTRATION.md, docs/QA-CHECKLIST.md e database/README.md, aproveitando equivalentes existentes. Documentar execucao sem Docker, PostgreSQL externo, SQL, Swagger, provisionamento seguro, fluxo cliente/contratacao/perfil e manual contextual de cada pagina entregue.

Ao integrar com Git, respeite a autorizacao e instrucoes do ambiente. Se commit/pull forem solicitados ou ja autorizados, revise o diff, inclua somente arquivos da entrega, verifique branch/tracking, valide antes, faca commit, fetch e pull da branch correta conforme politica do repositorio. Nao usar git add indiscriminado, reset --hard, descarte de alteracoes ou push forcado. Conflitos exigem resolucao fundamentada e nova validacao. Nao repetir pull como substituto de verificar o estado; nao fazer push sem solicitacao.

Relatorio final de cada execucao: HEAD e etapa; IDs concluidos com evidencias; bugs corrigidos e causa; fluxos/telas entregues; mudancas no banco; comandos/resultados; logins demo e provisionamento; verificacoes nao executadas; riscos e bloqueios; arquivos principais; confirmacao de ausencia de segredos/binarios novos; estado Git; primeira atividade da proxima etapa.

Checkpoint deve permitir retomar com pouco contexto: objetivo atual, IDs pendentes, dependencias, arquivos-chave, decisoes, comandos de verificacao e proximo passo. Gere um prompt curto de continuidade que apenas referencie os documentos locais e a proxima entrega. Nao exigir que o usuario reenvie este prompt inteiro.

## 10. Ordem de inicio desta execucao

Comece agora: confirme checkout e estado Git; leia instrucoes; estabeleca baseline; construa o inventario inicial; selecione E0 ou a primeira entrega incompleta desbloqueada; implemente-a de ponta a ponta; execute verificacoes; atualize matriz e checkpoint. Se ainda houver condicoes de continuar com seguranca, avance para a proxima entrega do mesmo fluxo. Nao finalize apenas com uma lista de intencoes e nao declare que todo o sistema foi revisado se parte dele nao foi examinada.
