# Sprint 18 — Cooperativas e Marketplace

A Sprint 18 integra cooperados, assistência técnica, programas produtivos, marketplace B2B, compras coletivas, contratos, bônus, repasses e pré-análise rural. A API preserva Clean Architecture: controllers delegam a `ICooperativeService`, a infraestrutura usa Dapper parametrizado e todas as consultas incluem o tenant.

## Fluxos
- Cooperado: documento único por tenant, organização, classificação e propriedade selecionadas por lookup.
- Visita: produtor e recurso produtivo selecionados, agenda, GPS, checklist, evidência e plano de ação.
- Programa: tipos açaí, tucupi, cacau, leite, carne, grãos, mandioca/farinha ou genérico; metas e conformidade por participante.
- Marketplace: oferta rastreável obrigatoriamente ligada a lote; demanda, negociação, reserva e confirmação.
- Compra coletiva: rateios positivos devem fechar exatamente o volume total antes da aprovação.
- Contrato: rascunho, ativação, encerramento ou cancelamento auditáveis; painel sinaliza vencimento em 30 dias.
- Crédito: parecer é exclusivamente interno. Não existe aprovação, chamada ou simulação bancária.

Permissões: `cooperative.read`, `cooperative.write` e `cooperative.approve`.
