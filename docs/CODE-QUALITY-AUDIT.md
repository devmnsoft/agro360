# Auditoria de qualidade do Agro360

## Escopo e diagnóstico

A sprint revisou contratos de aplicação, implementações de infraestrutura, logging, tarefas Dapper, exportações, instalador PostgreSQL e barreiras arquiteturais. A causa dominante era a evolução independente das interfaces e de implementações muito compactas: o tipo e a ordem dos argumentos continuavam compatíveis, porém nomes abreviados (`x`, `ct`, `id`) divergiam do contrato. Isso ativava CA1725 em cascata e tornava chamadas com argumentos nomeados inseguras. Logs por extension methods causavam CA1848/CA1873, e métodos auxiliares ocultavam os retornos concretos de Dapper, causando CA1859.

A ausência do SDK .NET 10 no ambiente desta auditoria impediu a execução local do compilador e dos testes. Por isso, toda mudança deve passar pelo pipeline com SDK 10 antes da promoção.

## Correções realizadas

- Os parâmetros públicos das implementações foram alinhados aos nomes definidos pelas interfaces, sem alterar os contratos.
- Logs diretos foram substituídos por métodos gerados por `LoggerMessage`, com IDs estáveis de 1001 a 1022, templates estruturados e exceções preservadas.
- Exportações técnicas passaram a formatar valores com `CultureInfo.InvariantCulture`.
- Auxiliares que encaminham resultados Dapper agora expõem `Task<int>` ou `Task<bool>` quando esse é o tipo concreto; métodos públicos não foram alterados.
- Coleções internas foram concretizadas quando não existe necessidade de abstração, e o gerador de número de compras foi tornado estático.
- Testes arquiteturais agora cobrem marcadores de conflito, logging direto, nomes de parâmetros, normalização cultural de texto, delimitadores raw string e schema canônico.
- O instalador foi inspecionado como SQL texto: usa somente o schema `agro360`; não foram adicionados artefatos de backup ou binários.

## Padrões proibidos

1. Suprimir analyzer ou reduzir severidade para ocultar defeitos.
2. Renomear parâmetros apenas na implementação de um contrato público.
3. Usar `LogInformation`, `LogWarning` ou `LogError` diretamente nos services; declarar evento com `LoggerMessage` e ID único.
4. Usar interpolação dependente de cultura em CSV, persistência ou integrações.
5. Concatenar entrada do usuário em SQL. Identificadores dinâmicos exigem allowlist fechada; valores sempre usam parâmetros Dapper.
6. Usar `ToLower()`/`ToUpper()` para comparação; usar `StringComparison` ou a variante `Invariant` apenas para normalização persistida.
7. Omitir `tenant_id` em leitura ou mutação de dados multiempresa.
8. Adicionar outro schema ao instalador canônico, índice GiST para JSONB sem operator class ou arquivo `.backup`, `.dump`, `.tar` ou `.zip`.

## Checklist para próximas sprints

- [ ] Alterar primeiro a interface e atualizar todas as implementações e consumidores na mesma mudança, quando a mudança contratual for realmente necessária.
- [ ] Executar `dotnet restore`, build da Infrastructure, build da solução e `dotnet test` com SDK 10.
- [ ] Confirmar ausência de warnings, pois o repositório usa `TreatWarningsAsErrors`.
- [ ] Revisar SQL quanto a parâmetros, filtro de tenant, aliases, total de paginação e transação.
- [ ] Adicionar cada novo evento à central de `LoggerMessage`, sem dados pessoais, credenciais ou connection strings.
- [ ] Exercitar serialização, DI e páginas Razor afetadas.
- [ ] Executar os testes arquiteturais antes de integrar.

## Pontos sensíveis e recomendações

### Dapper e SQL

Os services concentram SQL em linhas extensas, o que dificulta revisão de aliases e limites transacionais. Novas queries devem usar `CommandDefinition` com cancellation token, aliases compatíveis com propriedades C#, parâmetros para todos os valores e `tenant_id` em cada ramo e subconsulta multiempresa. Operações dependentes devem compartilhar a mesma conexão e transação. Paginação deve calcular `total` sem aplicar `limit/offset`. Identificadores dinâmicos só podem vir de dicionários/allowlists internos.

### Razor

Evitar lógica de domínio na view, conferir encoding padrão, antiforgery e validação server-side. Componentes devem receber view models tipados; `dynamic` mascara incompatibilidades até runtime.

### Injeção de dependência

Construtores devem reter apenas dependências realmente usadas. A inicialização da aplicação deve validar o container e manter lifetimes coerentes: contexto de tenant e conexão por escopo, serviços sem estado compartilhado e singletons sem dependência scoped.

### Serviços e observabilidade

Preferir código formatado e métodos pequenos a expressões comprimidas. Eventos de log precisam de ID estável, placeholders tipados e nenhuma senha, token, documento fiscal completo ou string de conexão. Não calcular payload caro se o nível estiver desabilitado; `LoggerMessage` é a barreira padrão.

### Banco canônico

O instalador deve continuar sendo SQL puro e idempotente no schema único `agro360`. Toda tabela usada por service novo deve ser criada no mesmo ciclo, com índices GIN para JSONB quando aplicável. Seeds essenciais (tenant MNSOFT, módulos, permissões, perfis e Super Administrador) devem ser verificados em revisão e em ambiente PostgreSQL descartável.
