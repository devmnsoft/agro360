# API v1

Base local: `http://localhost:8081/api/v1`.

## Autenticação

### Bootstrap de desenvolvimento

`POST /bootstrap`

Só funciona quando `Bootstrap:Enabled=true` e não existe tenant. Depois do primeiro uso deve ser desabilitado.

### Login

`POST /auth/login`

```json
{
  "tenantSlug": "fazenda-boa-vista",
  "email": "admin@empresa.com.br",
  "password": "SenhaForte!123"
}
```

### Refresh

`POST /auth/refresh`. O token utilizado é revogado e substituído; reutilização falha.

## Headers

| Header | Uso |
|---|---|
| `Authorization: Bearer ...` | obrigatório fora de bootstrap/login/refresh/health |
| `X-Correlation-ID` | UUID opcional; API gera se ausente |
| `X-Farm-ID` | restringe o contexto operacional quando suportado |
| `X-Organization-ID` | contexto organizacional |
| `X-Timezone` | timezone IANA do cliente |

O tenant nunca é aceito por header: vem da claim assinada.

## Endpoints entregues

| Método | Rota | Permissão | Efeito principal |
|---|---|---|---|
| POST | `/properties` | `properties.write` | cria fazenda |
| GET | `/properties` | `properties.read` | lista paginada |
| POST | `/fields` | `properties.write` | cria talhão/PostGIS |
| GET | `/properties/{id}/fields` | `properties.read` | lista talhões |
| POST | `/inventory/products` | `inventory.move` | cria produto |
| POST | `/inventory/warehouses` | `inventory.move` | cria depósito |
| POST | `/inventory/movements/receipts` | `inventory.move` | entrada e custo médio |
| POST | `/inventory/movements/consumptions` | `inventory.move` | consumo com bloqueio negativo |
| GET | `/inventory/balances` | `inventory.read` | posição paginada |
| POST | `/agriculture/seasons` | `agriculture.write` | cria plano de safra |
| GET | `/agriculture/seasons` | `agriculture.read` | lista safras |
| POST | `/agriculture/operations/planting` | `agriculture.write` | estoque + custo + AgroGraph |
| POST | `/agriculture/operations/harvest` | `agriculture.write` | produção + estoque + AgroGraph |
| POST | `/livestock/animals` | `livestock.write` | registra animal/timeline |
| GET | `/livestock/animals` | `livestock.read` | lista rebanho |
| POST | `/livestock/animals/{id}/weights` | `livestock.write` | pesagem e GMD |
| POST | `/livestock/animals/{id}/treatments` | `livestock.write` | sanidade + estoque + carência + custo |
| POST | `/commercial/sales` | `commercial.write` | venda + recebível + rastreio |
| GET | `/dashboard/command-center` | `dashboard.read` | KPIs e operações recentes |
| GET | `/search?query=` | `dashboard.read` | busca global autorizada |
| GET | `/traceability/{type}/{id}` | `dashboard.read` | grafo de cadeia de custódia |

## Paginação

Listagens retornam:

```json
{
  "items": [],
  "page": 1,
  "pageSize": 25,
  "total": 0,
  "totalPages": 0
}
```

`pageSize` é limitado a 100.

## Idempotência

Movimentos, plantio, colheita, pesagem, tratamentos e vendas aceitam `idempotencyKey`. Repetir a mesma chave no tenant retorna a operação original em vez de duplicar estoque/custo/financeiro.

## Problem Details

```json
{
  "type": "https://mnsoft.com.br/problems/business_conflict",
  "title": "Conflito de negócio",
  "status": 409,
  "detail": "Saldo disponível insuficiente; estoque negativo não é permitido.",
  "instance": "/api/v1/inventory/movements/consumptions",
  "traceId": "...",
  "code": "inventory.insufficient_stock"
}
```

## Fluxo E2E agrícola

1. bootstrap/login;
2. `POST /properties`;
3. `POST /fields`;
4. `POST /agriculture/seasons`;
5. criar semente e produto colhido;
6. criar depósito;
7. entrada de semente;
8. plantio — baixa estoque e gera custo;
9. colheita — entra produção com custo alocado;
10. venda CROP — baixa produto, cria recebível e grafo;
11. consultar Command Center e rastreabilidade.

## Fluxo E2E animal

1. cadastrar medicamento e entrada de estoque;
2. registrar animal;
3. registrar primeira/segunda pesagem;
4. tratar/vacinar — consome produto e define carência;
5. tentativa de venda durante carência retorna 409;
6. venda após carência cria recebível e relação `ANIMAL → SOLD_IN → SALE`.
