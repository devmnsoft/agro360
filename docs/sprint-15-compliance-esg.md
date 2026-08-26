# Sprint 15 — Compliance Agro e ESG

## Escopo

O módulo atende produtor, cooperativa, agroindústria e exportador sem assumir uma cultura específica. Documentos cobrem CAR, CCIR, ITR, licenças, alvarás, GTA, laudos, certificados sanitários, MAPA/ANVISA, contratos e autorizações. Regras são configuradas por tenant, produto e mercado para açaí, tucupi, cacau, castanha, mandioca/farinha, grãos, carne, leite ou qualquer produto cadastrado.

Certificações mantêm escopo, requisitos, evidências e validade. Auditorias aceitam templates e perguntas ponderadas para produtor, propriedade, lote, fornecedor, beneficiamento, armazenagem, expedição e logística. Não conformidades registram causa raiz, ações corretiva/preventiva, severidade, responsável, prazo, aprovação e encerramento.

## Regras

* Documento é vencido após `expires_on` e fica em alerta nos 30 dias anteriores.
* Venda/exportação é bloqueada quando qualquer regra obrigatória aplicável não tem requisito conforme.
* Certificação só é válida se aprovada e dentro da validade.
* Score de auditoria é a soma dos pesos conformes dividida pelo total; respostas não conformes podem originar NC.
* Ações abertas após o prazo são atrasadas. Transições sensíveis requerem `compliance.approve` e motivo.
* Todas as consultas privadas executam em transação com contexto RLS do tenant.

## API e tela

Os recursos ficam em `documents`, `product-rules`, `certifications`, `audits`, `non-conformities`, `export-dossiers` e `dashboard` sob `/api/compliance`; ESG usa `/api/esg/indicators` e `/api/esg/carbon`. A tela `/compliance` oferece dashboard, filtros, estados loading/erro/vazio e formulários validados com seletores de entidades.
