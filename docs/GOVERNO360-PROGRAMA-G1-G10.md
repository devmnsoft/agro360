# GOVERNO360 — Programa de sprints G1–G10 (gestão pública do desenvolvimento rural)

**Data da solicitação:** 2026-10-12 · **Status:** PLANEJADO — fila de execução **após** Bloco B (§7–§8) e Pecuária P1→P10.
**Repositório:** `devmnsoft/agro360` · **Regra de ouro:** auditar o estado vigente antes de cada sprint, reaproveitar estruturas existentes e implementar apenas lacunas; nunca superficialmente.

## Objetivo central

Jornada: **território → diagnóstico → programa → público elegível → solicitação → análise → atendimento/apoio → acompanhamento → resultado**.

Módulo para órgãos públicos (estados, municípios, consórcios, instituições parceiras) que planejam, executam e acompanham políticas de desenvolvimento agrícola, pecuário, pesqueiro, aquícola e de agricultura familiar. **Configurável, não fixo**: nenhum nome, organograma, programa ou regra de secretaria específica é gravado em código.

Integra à gestão operacional do Agro360 preservando a **separação entre dados institucionais e dados privados dos produtores**. O Governo360 **não tem acesso automático** aos dados de todos os clientes: compartilhamento entre tenants exige vínculo explícito com finalidade, escopo, vigência e revogação.

## Invariantes (integridade, não configuração)

- **Não criar segundo cadastro** de produtor, estoque, financeiro ou sistema documental — reutilizar agregados canônicos existentes.
- **Não implementar automaticamente** competências de fiscalização, licenciamento, certificação ou execução orçamentária oficial — só com escopo explícito configurado.
- **Dado declarado × verificado × estimado × oficial** são categorias distintas, sempre rotuladas.
- Cadastro operacional **≠ censo**; coordenada informada **não comprova limite fundiário**; indicador amostral informa cobertura.
- Produção declarada e verificada diferenciadas; resultados declarados ≠ comprovados; negócio em evento ≠ receita financeira confirmada.
- **Nenhuma certificação/crédito/indicação geográfica oficial fictícia**: registro interno documenta etapas e documentos; a concessão oficial é externa.
- Orçamento estimado **≠ empenho/liquidação/pagamento**; acompanhamento interno **≠ aprovação formal de contas**.
- Critérios, metas, decisões e formulários **versionados**; mudança não altera respostas/decisões históricas.
- Programação ativa **≠ inscrições abertas**; encerramento preserva execução e pendências.
- Pontuação existe **só com regra documentada e memória de cálculo**; IA auxilia leitura/organização, **não decide** elegibilidade/prioridade autonomamente.
- **Planejado ≠ realizado** (visitas, capacitações, entregas).
- Super administrador técnico **≠ autoridade decisória** do programa.
- Autorização por: **tenant + módulo contratado + perfil + programa + território + atribuição**.
- Linguagem independente de moeda/estado/módulo; 4 idiomas (pt-BR/en-US/es-ES/fr-FR) em todas as telas.
- Offline reutiliza a infraestrutura existente; sincronização revalida identidade, escopo, versão e idempotência; decisão institucional **não é confirmada no servidor enquanto desconectado**.

## Menu / navegação institucional (somente o contratado + autorizado aparece)

Visão geral · Territórios e diagnóstico · Famílias, produtores e organizações · Programas, projetos e ações · Levantamentos · Solicitações e seleção · Atendimentos e visitas · Capacitações · Benefícios e equipamentos · Agricultura e pecuária · Pesca e aquicultura · Comercialização · Sustentabilidade · Parcerias e instrumentos · Documentos · Indicadores e relatórios · Portal de atendimento · Administração e auditoria.

Entradas setoriais reutilizam dados/jornadas existentes oferecendo visão institucional — **não duplicam módulos operacionais**.

## Sprints

### G1 — Fundação institucional, responsabilidades e acesso (§2 + §17)
Auditar primeiro: SaaS/tenancy (organizações, módulos contratados, papéis/perfis), cadastros de órgãos/unidades existentes, documentação e auditoria. Modelar/reutilizar: organização contratante, órgãos e unidades administrativas, equipes e responsáveis, territórios de atuação, instituições parceiras, acordos de compartilhamento. Perfis configuráveis (gestor institucional, coordenador, analista, técnico, parceiro territorial, auditor, beneficiário). Registro de concessão/revogação de acesso, compartilhamentos, decisões, entregas, alterações sensíveis e exportações. Uma organização gere vários programas e atua em vários territórios.

### G2 — Territórios, famílias, produtores e organizações (§4 + §5)
Entidades separadas: **pessoa × núcleo familiar × produtor × unidade produtiva × associação × cooperativa × empreendimento**. Vários produtores na mesma família (sem dupla contagem); várias UPs por produtor; participação em organizações e múltiplos programas; alteração de composição familiar **com histórico**. CPF do responsável **não** é identidade permanente da família. Responsável pelo cadastro × representante autorizado × participante produtivo diferenciados. Duplicidade: análise + consolidação **auditável**, sem fusão automática por nome semelhante. Diagnóstico territorial: cadeias produtivas, infraestrutura/acesso, demandas, cobertura de atendimentos, origem/período/método dos levantamentos; mapa público sem exposição indevida de pessoas/locais sensíveis.

### G3 — Programas, projetos e ações + levantamentos versionados (§6 + §7)
Estrutura **programa → projeto → ação → execução → resultado**: objetivos e público, instituição responsável e parceiros, territórios/cadeias, vigência, metas e unidades, critérios de participação, documentos, responsáveis, recursos de referência (rastreabilidade por fonte), evidências e avaliação. Formulários versionados e configuráveis por programa (identificação/localização; composição familiar pertinente; atividades produtivas; área declarada e acesso à terra; produção e comercialização; infraestrutura/água/energia; organização coletiva; apoios anteriores; necessidades e encaminhamentos). Coleta mínima: renda/documentos/pessoal só quando necessários à finalidade. CAF/identificadores: número, situação, fonte, data e método de verificação — sem consulta oficial, apresentado como **declarado** ou **documento conferido**. Sem critérios aplicáveis + avaliação adequada, ninguém é classificado automaticamente como agricultor familiar elegível. Fluxo: **rascunho → enviado → revisão → diligência → validado para a finalidade**.

### G4 — Solicitações, elegibilidade e seleção (§8)
Jornada: solicitação → documentação → análise → diligência → decisão → participação → acompanhamento. Protocolo legível; critério avaliado individualmente com evidência e responsável; pendências com prazo; justificativa obrigatória de aprovação/recusa; revisão/recurso conforme política; segregação de funções quando exigida. Participação em outro programa **não implica impedimento automático**; restrições de acumulação explícitas.

### G5 — Atendimentos, visitas e capacitações (§9)
Agenda, equipe, visita, diagnóstico, orientação, encaminhamento e retorno. Capacitações: tema, local, inscrições, presença, carga horária, avaliação. Presença requer registro adequado; certificado de participação **≠ habilitação profissional**; evidência vinculada ao evento e ao escopo correto. **Integrar pendências à Central de Trabalho existente** (§4 do incremento anterior — mesma projeção de origem/causa/unidade/responsável/prazo/impacto/próxima ação).

### G6 — Benefícios, insumos e equipamentos (§10)
Fluxo: demanda → análise → aprovação → disponibilidade → entrega/cessão → confirmação → acompanhamento. Benefício vinculado ao programa e beneficiário; quantidade, unidade, origem e comprovante; disponibilidade real; **entrega idempotente**; correção e devolução auditáveis; limites/acumulação por critérios vigentes. **Estoque e patrimônio canônicos** (reuso do ledger de inventário existente — nunca paralelos). Bens cedidos: custodiante, localização, finalidade, utilização, manutenção e encerramento da cessão.

### G7 — Agricultura e pecuária institucionais (§11)
Visões setoriais reutilizando os módulos existentes (agrícola + programa Pecuária P1–P10): culturas, produção, práticas, unidades demonstrativas, beneficiamento, iniciativas de corte/leite atendidas. Compartilhar **apenas os dados necessários** mediante vínculo do G1. Unidades compatíveis; origem e período obrigatórios; parâmetros técnicos com fonte e revisão. Cadeias distintas **não são forçadas ao mesmo ciclo de safra**.

### G8 — Pesca e aquicultura (§11)
Pesca: levantamentos, organizações, desembarque e apoio produtivo conforme finalidade; registro interno **não substitui autorização oficial**. Aquicultura: **empreendimento → unidade de cultivo → povoamento → manejo → biometria → perdas → despesca → comercialização**.

### G9 — Comercialização, sustentabilidade e parcerias (§12 + §13 + §14)
Feiras/eventos/organizações/acesso a mercados/beneficiamento/identidade territorial: inscrição e seleção, apoio recebido, participação confirmada, resultados declarados ou comprovados, encaminhamentos. Indicação geográfica/marcas coletivas: etapas e documentos registrados; **cadastro interno ≠ concessão oficial**. Sustentabilidade: situação inicial, intervenção, prática apoiada, área, responsável, acompanhamento; indicador exige método e fonte; evidência de prática **não comprova regularidade ambiental**; sem crédito de carbono automático; estimativas mostram parâmetros; sem causalidade sem avaliação adequada. Parcerias/instrumentos: instituições, responsabilidades, vigência, metas, obrigações e entregas; responsável × executor × parceiro × beneficiário diferenciados; alertas de prazo/pendência; histórico de alterações; prestação de contas rastreável **sem se apresentar como aprovação formal**.

### G10 — Portais, indicadores e homologação integrada (§15 + §16 + §20)
Portal do beneficiário (cadastro, solicitações, documentos, pendências, atendimentos, acompanhamento) e portal parceiro (programas, territórios, tarefas autorizadas). Representação de família/organização validada; usuário **não acessa automaticamente** dados de todos os integrantes; vínculo revogado encerra acesso; atualização sensível pode exigir revisão; notificações externas dependem de integração real e preferências. Indicadores separados (pessoas, famílias únicas, produtores, UPs, organizações, participações, atendimentos, benefícios, metas×resultados) cada um com **definição, período, fonte, cobertura e atualização**; sem dupla contagem entre programas/territórios; relatório distingue execução física × referência financeira × resultado observado; painéis públicos agregados com avaliação de risco de identificação.

## Regras transversais por sprint (mesmas portas de Pecuária)

1. Auditar estado vigente antes de codar; reaproveitar; só lacunas.
2. Autenticação/autORIZAÇÃO no servidor: tenant + módulo + perfil + (+programa +território +atribuição quando aplicável); menu só com o contratado/autorizado.
3. PT/EN/ES/FR + labels permanentes + ajuda “Como preencher” + seletores por nome + loading/vazio/erro/sucesso + rascunho preservado + teclado/foco/contraste + responsivo (360/768/1440 px + zoom 200%).
4. Validação e confirmação em ações sensíveis; feedback de persistência.
5. Histórico e auditoria; concorrência (versões/advisory locks); **idempotência** de comandos.
6. Migrations incrementais protegendo dados; install limpo + upgrade descartáveis.
7. Testes comportamentais (E2E do sprint) + validação visual das telas alteradas.
8. Fontes canônicas únicas (inventário, financeiro, documentos, central de trabalho, identidade/SaaS).
9. Registrar **aprovado/falhou/bloqueado/não-executado** por item; regressões preservadas (jornada A–J, Compras 12/12, inspeção S1–S7, E2E de cada sprint de Pecuária concluído).

## Aceite do módulo (§20) — jornada mínima executável inicial

Instituição → programa → família/produtor → levantamento → análise → atendimento → painel básico, comprovando:

- Duas instituições configuradas com regras distintas.
- Família com múltiplos produtores **sem dupla contagem**.
- Participação em dois programas.
- Formulário versionado + diligência.
- Decisão com critérios e evidências.
- Atendimento e benefício **sem duplicação**.
- Compartilhamento autorizado + revogação.
- Negação de acesso fora do território/programa.
- Indicadores reconciliados (api==db).
- Dados públicos agregados e dados pessoais protegidos.

Build + testes + SQL + install limpo/upgrade + validação visual; entregar matriz de processos, modelo de relacionamentos, funcionalidades, permissões, migrations, evidências e pendências. **Não declarar todo o Governo360 homologado enquanto houver jornadas incompletas ou processos institucionais não validados.**

## Perguntas abertas (responder na auditoria de G1)

- QG1. Módulo 'governance'/'government' no catálogo SaaS (code, descrição, perfil CORE vs contratado)? Impacto em `_Layout`, menu, entitlements e papéis-semente.
- QG2. Territórios: reutilizar `geo_farms`/estrutura geográfica existente ou camadas novas (município/região/comunidade/localidade)? Hierarquia e vínculo com propriedades.
- QG3. Famílias/UPs: onde vivem hoje os cadastros de pessoas/propriedades (tenancy, geo, livestock?) — quais entidades reutilizar vs criar (núcleo familiar, associação, cooperativa).
- QG4. Form versionado: há infra de formulário dinâmico/documentos reutilizável (modulos docs, jsonb patterns de specs/qualidade)?
- QG5. Central de Trabalho: como projetar pendências de governo (origem/cause/unit/responsible/deadline/impact/next-action) no modelo existente.
- QG6. Estoque/patrimônio para benefícios: capacidade do ledger canônico para cessão/bens (custodiante, fim, manutenção) vs lacuna.
- QG7. Portais (beneficiário/parceiro): representação e delegação na identidade atual (usuários, papéis, vínculos) — o que falta.
- QG8. Compartilhamento cross-tenant: mecânica existente de consentimento/entitlement a estender (finalidade, escopo, vigência, revogação, auditoria).
