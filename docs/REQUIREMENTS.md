# Requisitos da release candidate

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
