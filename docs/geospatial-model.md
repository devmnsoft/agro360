# Modelo geoespacial

`geospatial.features` é a base comum. Armazena tipo da entidade, geometria GeoJSON em JSONB, centroide latitude/longitude, bounding box, áreas informada e calculada, propriedade/pai, status, origem e auditoria. A área calculada fica nula quando não existe mecanismo confiável: a aplicação nunca inventa medições.

Entidades suportadas: propriedade, talhão, pasto, piquete, armazém, rota, ponto logístico, ocorrência, zona de manejo e área ambiental. `occurrences`, `occurrence_evidence`, `route_segments` e `management_zone_links` guardam atributos especializados. Todas as tabelas têm `tenant_id`, índices e RLS.

PostGIS não é requisito. Uma instalação pode, opcionalmente, derivar áreas e envelopes com PostGIS fora do caminho funcional básico, mantendo GeoJSON como formato canônico.
