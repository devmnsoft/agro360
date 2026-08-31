# Sustentabilidade e ESG — Sprint 42

## Escopo entregue
O módulo registra conformidade por propriedade, documentos ambientais, indicadores e medições ESG, inventário gerencial de emissões, água/energia/insumos, resíduos, risco de fornecedores, rastreabilidade de lotes, projetos gerenciais de carbono, auditorias, planos e alertas. O dashboard consulta exclusivamente dados persistidos e retorna zero em bases vazias. Quatorze relatórios CSV são gerados no backend, limitados ao tenant e protegidos por `sustainability.reports`.

## Governança e regras
Todas as tabelas possuem `tenant_id`, RLS forçada, chaves compostas nas relações relevantes, índices operacionais e autoria/data. Áreas e quantidades têm constraints; área produtiva não supera a total. Reprovação, cancelamento, conclusão e liberação com ressalva exigem seus motivos. Aprovações guardam usuário/data. Documento vencido, ação atrasada e avaliações vencidas são derivados da data real; jobs futuros poderão materializar alertas, sem simulação.

Emissões são **inventário gerencial**, não certificação. `quantidade × fator informado` usa `numeric/decimal`; sem fator, emissão permanece nula e `PENDING_FACTOR`. Projeto de carbono só pode declarar certificação quando há `certification_document_id`. Nenhum fator, score, integração ou arquivo é inventado.

## Documentos e integrações reais
`document_id`/`evidence_document_id` são contratos de vínculo com o módulo documental. O registro manual guarda metadados auditáveis, mas não cria arquivo nem URL. A instalação deve conectar esses campos ao identificador produzido pelo armazenamento documental real. CAR/licenças e certificações governamentais não são consultados externamente: não há provedor oficial configurado nesta versão.

Compras deve consultar a última avaliação do fornecedor antes de aprovar categoria crítica; exportação deve consultar a rastreabilidade e a conformidade da fazenda conforme configuração do tenant. As tabelas e permissões suportam o bloqueio, mas a política configurável por tenant e conectores externos oficiais continuam pendências explícitas, nunca respostas simuladas.

## API e permissões
- `GET /api/sustainability/dashboard`
- `GET|POST /api/sustainability/environmental-compliances`
- `GET /api/sustainability/reports/{report}.csv`
- `sustainability.read`, `sustainability.write`, `sustainability.approve`, `sustainability.reports`

Relatórios permitidos: `environmental-compliance`, `environmental-documents`, `indicators`, `measurements`, `emissions`, `water`, `energy`, `waste`, `suppliers`, `lots`, `carbon-projects`, `audits`, `action-plans` e `alerts`.

## Operação
Aplicar `database/migrations/042_sprint42_sustainability_esg.sql` em PostgreSQL externo usando a connection string normal. Não requer Docker. A página `/sustentabilidade` usa autenticação existente e lookup de fazendas do tenant; não pede GUID ao operador.
