# Fiscal & Compliance 360 — adapters de provider

O núcleo fiscal depende de `IFiscalProvider`; nenhum controller conhece SDK, município, SEFAZ ou fornecedor SaaS. O registro resolve adapters pela chave configurada. Ausência de adapter resulta em `ProviderNotConfigured`, nunca em autorização.

## Invariantes

- Apenas uma resposta real do adapter pode produzir `Authorized`, `Rejected` ou `Cancelled`.
- Uma autorização é recusada se não trouxer identificador do provider e número externo.
- Um cancelamento é recusado sem código de confirmação do provider.
- Retentativas usam a mesma chave de idempotência e consultas não criam outro documento.
- Configurações persistidas contêm somente metadados não sensíveis e referência de credencial. Segredos pertencem ao secret manager/variáveis do ambiente.
- Toda operação externa gera tentativa auditável sem request, certificado, token ou payload sensível.

## Implementação de adapter

Um adapter implementa `SubmitAsync`, `QueryAsync` e `CancelAsync`, registra sua `ProviderKey` via DI e traduz o retorno oficial para `FiscalProviderResult`. O adapter não deve converter timeout em rejeição, nem inferir autorização. Em resposta ambígua, retorna `Submitted` e permite consulta posterior com o identificador real.

Não há adapter demonstrativo: um fake poderia ser acionado indevidamente e violaria a fronteira de compliance.
