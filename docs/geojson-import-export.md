# Importação e exportação GeoJSON

## Importar

Envie `POST /api/maps/imports/geojson` com `entityType`, `fileName`, uma `FeatureCollection` em `geoJson` e `confirm`. Cada feature precisa de `properties.name` e geometria suportada. A resposta lista erros por número de feature. Com qualquer erro, nenhuma feature é gravada; o lote e seus erros ficam registrados. Com `confirm=false`, ocorre somente revisão. Com `confirm=true` e validação integral, o lote é persistido atomicamente.

## Exportar

`GET /api/maps/exports/geojson?entityType=FIELD` produz `application/geo+json`. O filtro é opcional e o resultado preserva nome, tipo, status, área e origem, respeitando tenant e autorização.
