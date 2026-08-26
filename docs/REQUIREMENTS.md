# Requisitos da release candidate

O Agro360 atende operações agrícolas, pecuárias, financeiras, logísticas, de rastreabilidade, compliance, cooperativas e RH em contexto multiempresa. A release candidate exige PostgreSQL externo, .NET 10, autenticação JWT, autorização por permissão e persistência Dapper; Docker é apenas opcional.

Relacionamentos são escolhidos por lookups pesquisáveis, e comandos são validados no navegador e novamente na API. Toda consulta ou alteração privada deve usar o `tenant_id` obtido do token, nunca um tenant fornecido pelo corpo da requisição. O instalador canônico é `database/agro360-postgres-full.sql`.

Funcionalidades futuras devem entrar com contrato Application, regra Domain quando aplicável, implementação Infrastructure, endpoint autorizado, interface operacional e teste. Não são aceitos mocks, botões informativos ou dados sem persistência como funcionalidade entregue.

## Sprint 21 — implantação comercial

Entregue onboarding assistido, cinco templates agro, módulos por tenant, checklist percentual, dashboard real e importação CSV com mapeamento, pré-visualização, erros por linha, confirmação, histórico e rollback auditável. A homologação exige executar o SQL completo, carregar opcionalmente o seed demo, validar isolamento por tenant e percorrer `/Deployment` em desktop e mobile.
