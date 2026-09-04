# PostgreSQL — Agro360

O arquivo `agro360-postgres-full.sql` é o instalador canônico, único e autocontido do Agro360. Ele cria exclusivamente o schema `agro360`, incluindo tabelas, chaves estrangeiras, constraints, índices, views, funções, triggers, catálogos e dados mínimos de inicialização. Não depende de Docker, migrations externas, `\\i`, caminhos locais ou dados de conexão embutidos.

## Pré-requisitos

- PostgreSQL 15 ou superior;
- extensões `pgcrypto`, `pg_trgm` e `unaccent` disponíveis;
- uma conta com autorização para `CREATE EXTENSION` e `CREATE SCHEMA` na primeira instalação.

## Executar em um banco limpo

O projeto **não usa nem aceita arquivos binários de banco de dados** (`.backup`, `.dump`, `.tar`, `.zip`, `.bak` ou equivalentes). A entrega principal e restaurável é somente o script SQL em texto puro `database/agro360-postgres-full.sql`.

O arquivo `.sql` **não deve ser restaurado com `pg_restore`**. `pg_restore` é destinado aos formatos de arquivo produzidos pelo `pg_dump`; este instalador deve ser executado pelo `psql` ou pelo Query Tool do pgAdmin.

Crie antes um banco PostgreSQL vazio chamado `agro360` e execute, a partir da raiz do projeto:

```bash
psql -h localhost -p 5432 -U postgres -d agro360 -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql
```

A opção `ON_ERROR_STOP=1` é obrigatória na validação: o script principal precisa concluir sem qualquer erro. Host, porta e usuário podem ser ajustados ao ambiente, sem editar nem adicionar credenciais ao arquivo SQL.

### Execução pelo pgAdmin

1. Crie o banco `agro360`.
2. Abra o banco no pgAdmin.
3. Abra o **Query Tool**.
4. Carregue `database/agro360-postgres-full.sql`.
5. Execute o script completo.

Não use a opção **Restore** do pgAdmin para esse arquivo, pois ela aciona o fluxo de `pg_restore` em vez de executar o SQL texto.

## Acesso inicial (somente Development/local)

- **Tenant:** `agro360-platform`
- **Plataforma:** `MNSOFT / Agro360 Platform`
- **Nome:** `Super Administrador MNSOFT`
- **Login/e-mail:** `superadmin@mnsoft.com.br`
- **Documento:** `18.160.057/0001-13` (`CNPJ`)
- **Perfil:** `SUPER_ADMIN`
- **Senha inicial:** `MNSoft@Agro360#2026`
- **Status:** Ativo

> **Atenção:** esta senha é exclusivamente para desenvolvimento/local. O usuário é criado com `must_change_password = true`; troque-a no primeiro acesso e nunca reutilize essa credencial em produção.

A senha não é armazenada em texto puro. O instalador contém somente um hash `PBKDF2-HMAC-SHA512`, com 210.000 iterações, salt de 16 bytes e chave de 32 bytes, compatível com `Agro360.Infrastructure.Security.PasswordHasher` — o mesmo componente usado no login.

## Validações depois da restauração

```sql
-- Deve listar somente agro360 entre schemas da aplicação.
select schema_name from information_schema.schemata
where schema_name in ('identity','finance','audit','workflow','inventory','platform','tenancy');

-- Deve retornar zero.
select count(*) from information_schema.tables where table_schema = 'public';

-- Inventário e FKs do schema canônico.
select count(*) from information_schema.tables where table_schema = 'agro360';
select count(*) from information_schema.table_constraints
where constraint_schema = 'agro360' and constraint_type = 'FOREIGN KEY';

-- Confirma o bootstrap sem revelar o hash.
select u.name,u.email,u.normalized_document,u.document_type,u.status,
       u.must_change_password,r.code as profile,t.name as tenant
from agro360.identity_users u
join agro360.identity_user_roles ur on ur.tenant_id=u.tenant_id and ur.user_id=u.id
join agro360.identity_roles r on r.tenant_id=ur.tenant_id and r.id=ur.role_id
join agro360.tenancy_tenants t on t.id=u.tenant_id
where u.email='superadmin@mnsoft.com.br';
```

Antes de publicar, execute também `./scripts/validate-full-sql.sh`, `dotnet restore`, `dotnet build --no-restore` e `dotnet test --no-build`.

## Cliente de homologação incluído

O instalador completo também inclui, de forma idempotente, o cliente **Fazenda Santa Clara** no plano Profissional. Para o teste local, use o tenant `santa-clara`, o e-mail `admin@santaclara.agro360.local` e a senha inicial `SantaClara@2026!` (perfil **Administrador do Cliente**). O SQL armazena exclusivamente hashes PBKDF2; as senhas em texto desta documentação são credenciais descartáveis de homologação e não devem ser usadas em produção.

## Seed operacional opcional de desenvolvimento

Depois da instalação principal, carregue a Fazenda Santa Clara, talhões, safra, usuário exemplo e estoque inicial com:

```bash
psql -v ON_ERROR_STOP=1 -d agro360 -f database/agro360-postgres-seed-dev.sql
```

Esse arquivo é opcional; `agro360-postgres-full.sql` sozinho já deixa a plataforma inicializável.
# Execução do instalador SQL

`agro360-postgres-full.sql` é um script de texto e deve ser executado pelo **Query Tool** do PostgreSQL ou por:

```bash
psql "$AGRO360_CONNECTION_STRING" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql
```

Não use `pg_restore` com esse arquivo; esse comando é destinado a dumps em formatos próprios. O bootstrap é idempotente e inclui o tenant `santa-clara`, o Administrador do Cliente e o Super Administrador com hashes PBKDF2-SHA512 compatíveis com a aplicação.

## Comercial Agro 360 (sprint atual)

Consulte `docs/COMMERCIAL-AGRO.md` para fluxo, regras implementadas, modelo persistente e pendências reais de integração.
