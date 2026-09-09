# Manual interno do sistema Agro360

## Diagnóstico de acesso — correção E0

Na tela de acesso, informe o cliente e sua identidade conforme o cadastro atual. **Testar conexão com a API** só anuncia sucesso após validar readiness do banco/schema e um documento OpenAPI válido. Em falha de rede, a mensagem apresenta a URL configurada; banco sem schema retorna indisponibilidade, mesmo que o processo esteja vivo. Não compartilhe senha/token ao pedir suporte.

O shell público pode ser recuperado offline, mas respostas de API, dados autenticados, health e Swagger não são substituídos por páginas em cache. O cache legado de lookups é invalidado; dados operacionais offline seguros continuam pendentes. CNPJ como contexto da organização e seleção pós-login ainda são requisitos E1, não funcionalidades já homologadas.

Plano/estado verificável: [mestre](execucao/AGRO360-MASTER-PLAN.md), [checkpoint](EXECUTION-CHECKPOINT.md) e [matriz](TRACEABILITY-MATRIX-v0.2.0.md).

## Visão geral

O Agro360 é um SaaS B2B multi-tenant para administrar clientes, usuários e a operação do agronegócio. A API é a fonte das permissões, regras de negócio e validações; o menu apenas reflete os módulos e permissões devolvidos no login. Todos os dados operacionais devem permanecer limitados ao tenant autenticado.

Em qualquer tela, abra **Como usar esta tela** para consultar objetivo, ações, regras e consequências. Campos obrigatórios são marcados, relacionamentos usam seletores por nome e ações críticas pedem confirmação e, quando aplicável, justificativa.

## Perfis de acesso

- **Super Admin MNSOFT:** visão global de clientes, planos, módulos, cobranças, bloqueios, usuários, auditoria e saúde da plataforma. Pode alternar o contexto administrativo, mas toda ação global é registrada.
- **Administrador do Cliente:** administra usuários, perfis, configurações e dados somente do próprio tenant, respeitando plano e módulos contratados. Não cria Super Admin nem altera o próprio plano.
- **Usuário operacional:** acessa apenas telas e ações liberadas pelos perfis associados. Permissões de leitura, escrita, aprovação e exportação são independentes.

Uma opção ausente no menu normalmente indica módulo não contratado ou falta de permissão. A API repete a autorização mesmo quando a URL é aberta diretamente.

## Módulos e telas principais

- **Dashboard:** indicadores executivos e operacionais, alertas e atividade recente do contexto autorizado.
- **Administração SaaS:** clientes/tenants, planos, módulos, limites, cobranças, bloqueios, consumo e auditoria global.
- **Usuários e perfis:** usuários do cliente, papéis, permissões, convites, sessões e dispositivos.
- **Fazendas e Agricultura:** propriedades, talhões, safras, operações, insumos, colheita, custos e produtividade.
- **Pecuária:** animais, rebanhos, manejo, sanidade, reprodução, nutrição, pesagens e produção.
- **Estoque e Armazenagem:** itens, saldos, movimentações, recebimentos, lotes, armazéns e rastreabilidade.
- **Compras:** fornecedores, homologação, catálogo, requisições, pedidos, recebimentos e exportações.
- **Produção/Indústria:** ordens, consumo, apontamentos, qualidade, rendimento e rastreabilidade industrial.
- **Comercial:** CRM, oportunidades, propostas, contratos, vendas e entregas.
- **Fiscal:** documentos fiscais, emissão, eventos, regras tributárias e acompanhamento de falhas.
- **Financeiro:** plano de contas, centros de custo, títulos, baixas, conciliação, orçamento, fluxo de caixa e DRE.
- **Logística e Mapas:** rotas, viagens, ocorrências e recursos geoespaciais em GeoJSON.
- **Qualidade e Compliance:** inspeções, requisitos, não conformidades, CAPA, auditorias, evidências e ESG.
- **Documentos:** documentos, evidências, dossiês e certificados.
- **Relatórios e Inteligência:** indicadores, análises, recomendações, exportações e mapas.
- **Tarefas e Aprovações:** tarefas, alertas, regras, workflows, calendário, notificações e outbox.
- **Configurações e Governança:** organização, integrações, importações, preferências, idioma e trilhas de governança.
- **Ajuda e Suporte:** base de conhecimento, chamados, SLA e orientação operacional.

## Configurar um cliente

1. Entre como Super Admin e abra **Administração SaaS MNSOFT**.
2. Cadastre o tenant com nome, slug, fuso horário, plano e estado inicial.
3. Associe os módulos e limites contratados; não libere funcionalidades fora do contrato.
4. Crie o administrador do cliente e associe o perfil administrativo local.
5. Revise o checklist de implantação, as configurações da organização e as permissões.
6. Valide o primeiro acesso e confirme que listagens, buscas, dashboards e relatórios não exibem outro tenant.

Bloquear, suspender ou cancelar exige motivo. Reativar limpa o motivo de bloqueio conforme a regra persistida e gera auditoria.

## Criar usuários e perfis

1. No tenant correto, abra **Usuários e Perfis**.
2. Crie o usuário com nome, e-mail e, quando aplicável, CPF/CNPJ normalizado.
3. Associe um ou mais perfis; escolha o perfil primário e aplique o menor conjunto de permissões necessário.
4. Envie o convite pelo fluxo configurado ou entregue a credencial inicial por canal seguro.
5. Revogue sessões e dispositivos quando houver suspeita de acesso indevido.

Senhas nunca são armazenadas em texto puro. O login aceita e-mail, CPF ou CNPJ previamente cadastrado, valida o hash PBKDF2 no banco e devolve mensagem genérica para credencial inválida.

## Liberar ou bloquear módulos

Somente o Super Admin altera plano, módulo contratado, limite ou override de feature. Selecione o cliente, revise o contrato, informe o motivo e confirme a alteração. O frontend oculta módulos indisponíveis, e o backend continua validando permissão, tenant, plano e feature flag.

## Sessão, erros e auditoria

O access token identifica usuário, tenant, perfil e permissões. O refresh token é rotacionado; quando expira ou é revogado, a aplicação limpa a sessão e volta ao login. Logout e revogação encerram o acesso real.

Erros funcionais são mostrados em toast/modal com orientação. Erros técnicos preservam `traceId` para suporte sem revelar senha, token, hash ou documento completo. Alterações críticas geram eventos de auditoria com ator, tenant, operação e data.

## Swagger e execução local

Em Development, inicie a API e abra `/swagger` na URL configurada no perfil de execução. O OpenAPI JSON também fica disponível em `/openapi/v1.json`.

```powershell
dotnet restore MNSOFT.Agro360.sln
dotnet build MNSOFT.Agro360.sln --no-restore
dotnet run --project src/Hosts/Agro360.Api/Agro360.Api.csproj
```

Se a tela de login não alcançar a API, use **Testar conexão com a API** e confira a URL em `ApiBaseUrl`. Em Development, a própria tela oferece o link para o Swagger.

## Instalar ou restaurar o banco

`database/agro360-postgres-full.sql` é texto SQL autocontido e usa somente o schema `agro360`. Execute com Query Tool ou `psql`; não use `pg_restore`.

```powershell
psql -h localhost -p 5432 -U postgres -d agro360 -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql
```

O usuário precisa poder criar as extensões `pgcrypto`, `pg_trgm` e `unaccent`. Execute sempre em banco vazio ou descartável durante homologação e confira as validações de `database/README.md`.

## Credenciais locais de homologação

| Perfil | Tenant | Login de referência | Ativação |
|---|---|---|---|
| Super Admin MNSOFT | `agro360-platform` | `superadmin@mnsoft.com.br` | Provisionador seguro da aplicação, com segredo local |
| Administrador Fazenda Santa Clara | `santa-clara` | `admin@santaclara.agro360.local` | Convite seguro ou provisionamento explícito |

O instalador não distribui senhas fixas: as contas de referência permanecem sem credencial utilizável até a ativação explícita. Nunca grave segredos em documentação, scripts ou código-fonte.

## Checklist de homologação

1. Execute `dotnet restore`, `dotnet build`, `dotnet format` e `dotnet test`.
2. Instale o SQL com `ON_ERROR_STOP=1` em PostgreSQL limpo.
3. Abra Swagger e confirme que não há conflito de rotas.
4. Teste os dois logins reais e a rotação/expiração do refresh token.
5. Abra Dashboard, menu principal e telas prioritárias em desktop, tablet e celular.
6. Confirme validação por campo, toast, loading, empty state e modal de ação crítica.
7. Valide RBAC, módulos contratados, isolamento por tenant e auditoria.
8. Confirme que nenhum binário ou credencial de produção foi adicionado.

## Pendências reais de ambiente

- O teste de instalação SQL e os logins reais dependem de uma instância PostgreSQL acessível e da variável `AGRO360_TEST_CONNECTION_STRING` ou `ConnectionStrings__Agro360`.
- Integrações externas, envio de e-mail/mensagens, emissão fiscal e armazenamento de documentos exigem provedores e segredos configurados; quando ausentes, o sistema deve manter o evento pendente e informar a configuração necessária.
- A homologação visual final deve ser repetida nos navegadores e resoluções suportados antes de cada release.
