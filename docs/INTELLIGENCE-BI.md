# Sprint 41 — Inteligência Agro360 e BI operacional

A rota `/inteligencia-agro360` consolida indicadores persistidos, alertas, riscos e recomendações do tenant autenticado. O painel nunca converte ausência de snapshot em zero: apresenta **Indisponível** e preserva o erro de cálculo. Os endpoints ficam em `/api/intelligence360`, exigem `intelligence.read` e mutações exigem `intelligence.write`; indicadores estratégicos também exigem `intelligence.strategic`.

## Indicadores e fontes

Definições aceitam somente fórmulas (`SUM`, `COUNT`, `AVERAGE`, `PERCENTAGE`, `BALANCE`, `MARGIN`, `PRODUCTIVITY`) e fontes cadastradas pelo backend. Nenhum SQL informado pelo navegador é executado. Valores usam `numeric`/`decimal`; percentuais são limitados a 0–100. Cada cálculo acrescenta snapshot; indisponibilidade ou erro é registrado sem interromper o painel.

## Alertas, riscos e recomendações

Regras inativas ou fora da vigência não devem ser avaliadas. Atribuição exige responsável, resolução exige comentário e alerta crítico ignorado exige justificativa. Fingerprints agrupam duplicidades abertas. Recomendações identificam a regra de origem, dependem de confirmação humana e rejeições altas/críticas exigem motivo. Sem provedor externo, a interface declara que as recomendações são determinísticas e baseadas em regras.

## Auditoria, CSV e segurança

Eventos usam correlação, usuário e tenant; payloads de auditoria devem ser sanitizados antes da gravação e nunca conter senha, token ou connection string. CSVs são produzidos no backend com filtros parametrizados, trilha em `intelligence_report_exports` e RLS forçada. Instale em PostgreSQL externo com `database/agro360-postgres-full.sql`; Docker permanece opcional.
