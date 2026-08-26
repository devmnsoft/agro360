# BI e relatórios

`GET /api/intelligence/indicators` consolida financeiro, agricultura, pecuária, estoque, máquinas, armazenagem, logística e conformidade. Todos os cálculos informam unidade e explicação. `GET /reports` cataloga 23 relatórios; `POST /reports/run?id=...` aplica período, propriedade, safra e status, e `GET /reports/{id}/export/csv` exporta CSV UTF-8. Datas invertidas e períodos acima de dez anos são rejeitados. O identificador do relatório vem do catálogo, não de SQL do cliente.
