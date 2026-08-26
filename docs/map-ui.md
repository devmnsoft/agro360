# Interface do Mapa Agro

Acesse `/Maps`. O fundo vetorial local não usa token e continua visível offline. A tela oferece loading, erro, vazio, filtros, camadas e doze visões: mapa, camadas, propriedade, talhão, pastagem/piquete, zonas, ocorrências, importação, exportação, rotas, território e dashboard.

Relações são selecionadas em dropdowns carregados da API; não há entrada de ID técnico. Campos usam validação HTML e os contratos da API repetem as regras no backend. GeoJSON é revisado no servidor, mesmo quando a validação do navegador passa.
