# CRM e Comercial SaaS — Sprint 47

O CRM da MNSOFT persiste leads, contatos, oportunidades, atividades, propostas e contratos em PostgreSQL, sempre com `tenant_id`, auditoria e RLS. CPF/CNPJ e e-mail possuem índices únicos por tenant e a API também retorna uma mensagem explícita de duplicidade. O pipeline usa os estados `NEW`, `QUALIFYING`, `QUALIFIED`, `PROPOSAL_SENT`, `NEGOTIATING`, `WON`, `LOST`, `SUSPENDED` e `CANCELLED`; perda exige motivo. A conversão de oportunidade ganha pode criar o tenant real.

Propostas são calculadas no backend por `mensalidade + implantação + suporte - desconto`. Descontos negativos ou superiores ao subtotal são rejeitados; envio exige lead/cliente e plano; aceite vencido exige revalidação e grava ator/data. Contratos só nascem de proposta aceita vinculada ao cliente, atualizam o plano em `saas.organizations`, mantêm timeline auditável e usam o termo assinatura eletrônica. Não há PDF ou ICP-Brasil simulados.

A permissão `crm.read` protege leitura; `crm.write` e `commercial-saas.write` protegem mutações. O superadministrador MNSOFT recebe essas permissões, enquanto o tenant comum só atravessa a RLS de seu contexto.
