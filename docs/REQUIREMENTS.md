# Requisitos da release candidate

## Inteligência operacional

- Recomendações exigem motivo, fonte, módulo, severidade e aprovação antes de ações.
- Scores ficam entre 0 e 100 e preservam fórmula/fatores.
- Anomalias registram critério, observado e referência sem alegar fraude.
- Assistente funciona sem IA externa, respeita tenant/permissão e informa fonte.

O Agro360 atende operações agrícolas, pecuárias, financeiras, logísticas, de rastreabilidade, compliance, cooperativas e RH em contexto multiempresa. A release candidate exige PostgreSQL externo, .NET 10, autenticação JWT, autorização por permissão e persistência Dapper; Docker é apenas opcional.

Relacionamentos são escolhidos por lookups pesquisáveis, e comandos são validados no navegador e novamente na API. Toda consulta ou alteração privada deve usar o `tenant_id` obtido do token, nunca um tenant fornecido pelo corpo da requisição. O instalador canônico é `database/agro360-postgres-full.sql`.

Funcionalidades futuras devem entrar com contrato Application, regra Domain quando aplicável, implementação Infrastructure, endpoint autorizado, interface operacional e teste. Não são aceitos mocks, botões informativos ou dados sem persistência como funcionalidade entregue.

## Sprint 21 — implantação comercial

Entregue onboarding assistido, cinco templates agro, módulos por tenant, checklist percentual, dashboard real e importação CSV com mapeamento, pré-visualização, erros por linha, confirmação, histórico e rollback auditável. A homologação exige executar o SQL completo, carregar opcionalmente o seed demo, validar isolamento por tenant e percorrer `/Deployment` em desktop e mobile.

## Requisitos comerciais (Sprint 22)
- **COM-001:** clientes, prospects, contatos e equipe são isolados por tenant e selecionados por lookup.
- **COM-002:** funil registra toda transição; perda exige motivo.
- **COM-003:** pedido exige cliente elegível, item positivo e desconto autorizado; aprovação persiste evento de integração.
- **COM-004:** contrato controla vigência e quantidade entregue.
- **COM-005:** comissão usa uma única modalidade e não duplica pedido/regra/representante.
- **COM-006:** split usa participantes únicos, modalidade exclusiva e percentual acumulado de no máximo 100%; é controle interno.
- **COM-007:** dashboard e filtros usam apenas persistência real e são seguros quando vazios.

## Requisitos Sprint 23
- DOC-01: arquivo em raiz configurável, nome físico imprevisível, hash SHA-256, tipo/tamanho/extensão validados e download autorizado por tenant.
- DOC-02: versões anteriores são preservadas e nova versão exige motivo.
- EVI-01: validação registra ator/data; rejeição exige motivo e evidência validada não é removida fisicamente.
- DOS-01: aprovação exige checklist obrigatório concluído; reprovação exige motivo.
- CER-01: emissão exige vínculo válido; consulta pública omite IDs e dados internos; revogação exige motivo.

## Requisitos operacionais da Sprint 24

- **OP-01:** tarefa pertence ao tenant, exige responsável ativo, prioridade e prazo; conclusão/cancelamento exigem justificativa adequada.
- **OP-02:** alerta tem origem, severidade, histórico e chave ativa de deduplicação.
- **OP-03:** regra determinística é configurável, auditável e registra execução.
- **OP-04:** workflow registra solicitante, aprovador, decisão e impede autoaprovação segregada.
- **OP-05:** notificação pertence a tenant e destinatário; outbox só marca envio após provider real.
- **OP-06:** agenda e dashboard calculam dados persistidos e aceitam estado vazio.

## Requisitos Sprint 25
- BI, mapas e relatórios DEVEM filtrar pelo tenant e autorização da sessão.
- Filtros DEVEM usar entidades pelo nome; IDs técnicos não podem ser solicitados ao usuário.
- CSV DEVE representar a consulta real filtrada; vazio é um resultado válido.
- Coordenadas DEVEM respeitar latitude [-90,90] e longitude [-180,180].
- Interfaces DEVEM possuir foco visível, empty/loading/error states e funcionar a partir de 360 px.

## RF-MOB-26 — Agro360 Campo

O sistema deve permitir registrar manejo, ocorrência, check-in e evidência em experiência responsiva, enfileirar operações no IndexedDB durante indisponibilidade de rede e sincronizá-las com idempotência, validação server-side, tenant e usuário autenticado. Check-in GPS exige coordenadas válidas; check-in manual exige justificativa.

## Requisitos funcionais Sprint 27

- **RF-PORTAL-01:** autenticar usuário externo sem conceder permissões administrativas.
- **RF-PORTAL-02:** convidar com token aleatório armazenado apenas como hash, validade, revogação, termo e outbox real.
- **RF-PORTAL-03:** restringir consultas a tenant, usuário e entidade vinculada.
- **RF-PORTAL-04:** persistir solicitações, eventos, comunicados e leituras.
- **RF-MKT-01:** listar somente disponibilidade positiva e certificação autorizada.
- **RF-MKT-02:** rejeitar cotação vazia, quantidade inválida ou indisponível e não simular pagamento/pedido.
- **RNF-PORTAL-01:** validar formulários no navegador e no domínio, usar SQL parametrizado, RLS e trilha auditável.

## Sprint 28 — Qualidade e Compliance
Implementação persistente de requisitos configuráveis, especificações versionadas, inspeções, decisão auditável de lotes, não conformidades/CAPA, auditorias, beneficiamento e prontidão de exportação. Homologar regras e UX conforme [Qualidade e Compliance](QUALITY-COMPLIANCE.md) e [Prontidão para exportação](EXPORT-READINESS.md).

## Requisitos Sprint 29

- **RF-SAAS-01:** somente Super Admin gerencia tenants, planos, assinaturas, cobrancas, flags e overrides.
- **RF-SAAS-02:** mudancas criticas exigem motivo e auditoria; baixa paga exige data e permissao.
- **RF-SAAS-03:** bloqueio administrativo prevalece sobre plano; limite esgotado impede somente novos registros.
- **RF-SAAS-04:** onboarding exige etapas obrigatorias; white label depende do plano e valida logo/cores.
- **RNF-SAAS-01:** toda consulta tenant deve usar contexto, SQL parametrizado e RLS; nenhum seletor solicita GUID.

## Requisitos Sprint 30
Todo registro operacional possui `tenant_id`, RLS e auditoria. Chaves são persistidas somente por SHA-256; segredos/certificados são referências a cofre. Autorização fiscal exige resposta verificável de provider. Importações validam linhas e chaves naturais; exportações exigem permissão e geram auditoria.

## Atendimento e Customer Success (Sprint 33)

- **SUP-01:** todo acesso multiempresa deve filtrar `tenant_id` e respeitar RLS.
- **SUP-02:** cancelamento/reabertura exige motivo; resolução exige resposta; fechamento registra autor/data.
- **SUP-03:** SLA positivo é calculado na abertura e violações são observáveis.
- **SUP-04:** conteúdo rascunho/arquivado não é público; avaliação é única por usuário/artigo.
- **SUP-05:** Go-live requer homologação e toda fase requer checklist ou evidência.
- **SUP-06:** notas de treinamento são 1–5 e NPS/feedback 0–10.
- **SUP-07:** backlog é interno; releases respeitam público e persistem leitura.
- **SUP-08:** nenhuma tela solicita GUID; seleções relacionais exibem nomes autorizados.

## SST Rural (Sprint 34)
- Isolamento por tenant e permissões `sst.read`, `sst.write` e `sst.medical.read`.
- Risco = severidade × probabilidade; nível 16–25 é crítico e gera alerta.
- Entrega exige trabalhador/EPI ativo, quantidade positiva e datas coerentes.
- Incidente grave exige investigação; checklist exige respostas/evidências configuradas.
- Exames limitam-se a controle administrativo, sem laudo clínico detalhado.

## Requisitos Sprint 35 — Frota

- Isolamento por tenant em query e RLS; código/placa únicos; relacionamentos por lookup.
- Ativo inativo/baixado/vendido não recebe operação; OS crítica bloqueia o ativo.
- Medidores monotônicos, salvo justificativa + permissão + auditoria.
- Plano com periodicidade positiva; OS concluída/cancelada exige descrição/motivo.
- Abastecimento positivo, total calculado e custo idempotente; parada calcula disponibilidade.
- Custos e baixas protegidos por permissões; CSV e dashboard trabalham com dados reais e estado vazio.
