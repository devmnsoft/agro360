# BI, relatórios e mapas — Sprint 25

A **Inteligência Agro360** consulta exclusivamente dados persistidos do tenant ativo. Indicadores e gráficos recebem período, propriedade, safra e status; conjuntos vazios retornam zero ou empty state, nunca dados demonstrativos. Os painéis cobrem agricultura, pecuária, estoque, comercial, financeiro, logística, rastreabilidade, documentos, compliance, cooperativas e RH/SST conforme a disponibilidade de cada módulo.

A Central de **Relatórios** apresenta o catálogo autorizado, executa consultas parametrizadas no serviço de aplicação e exporta CSV UTF-8. Selecione datas e o relatório pelo nome; nenhuma chave técnica é solicitada. PDF é uma pendência explícita: será habilitado somente com renderizador, storage e trilha de auditoria reais.

A geovisualização usa propriedades, talhões, ocorrências, evidências GPS, lotes e pontos de rotas persistidos. `geo_locations`, `geo_areas`, `geo_routes` e `geo_route_points` validam latitude entre -90 e 90 e longitude entre -180 e 180. A solução não exige provedor cartográfico: lista coordenada, camadas e painel lateral continuam operacionais sem Internet. RLS e filtros `tenant_id` impedem leitura cruzada.

## Homologação

1. Entre com usuário que possua `bi.read`; use `bi.export` para exportação.
2. Valide períodos com e sem registros e altere filtros funcionais.
3. Confirme o CSV no Excel/LibreOffice (UTF-8 com BOM e separador `;`).
4. Teste coordenadas nos limites e rejeite valores fora deles.
5. Simule dois tenants e confirme que dashboards, dropdowns, relatórios e mapas não cruzam dados.
