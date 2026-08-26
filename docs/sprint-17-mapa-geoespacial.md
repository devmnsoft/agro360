# Sprint 17 — Mapa Agro e geoespacial

A Sprint 17 entrega uma camada territorial multi-tenant em PostgreSQL puro, API Dapper e uma central Razor responsiva. O domínio aceita `Point`, `LineString`, `Polygon` e `MultiPolygon`; valida estrutura e limites das coordenadas antes da persistência.

## Entregas

- mapa operacional com fundo vetorial local e camadas de propriedades, talhões, culturas, pastagens, rotas, logística, ocorrências, armazenagem e alertas;
- edição validada de propriedade, talhão, pastagem, piquete, rota e zona, sempre selecionando relações;
- ocorrências com tipo, severidade, ponto, responsável e acompanhamento;
- validação/revisão/importação atômica e exportação `FeatureCollection`;
- trechos fluviais, vicinais, balsas e rodovias; um trecho `BLOCKED` requer autorização explícita;
- painel territorial e dashboard calculados exclusivamente dos registros do tenant.

Todos os endpoints exigem `maps.read`; mutações exigem `maps.write`. O middleware estabelece o tenant e as políticas RLS aplicam isolamento adicional.
