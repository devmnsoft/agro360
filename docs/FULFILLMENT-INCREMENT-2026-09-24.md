# Incremento de atendimento de pedidos — 2026-09-24

## Base auditada

Branch `work`, commit inicial `83129979e6ef4fafc9ee2082f11b14f968668c63`. A solução fixa .NET 10, Razor Pages, Dapper 2.1.66, Npgsql 10 e PostgreSQL. O executor transacional possui sobrecargas tipadas; as quatro expressões que retornam resultado em `Commercial360Service` usam explicitamente `InTenantTransactionAsync<T>`. A compilação não pôde ser executada porque o SDK .NET não está instalado neste ambiente.

| Área | Diagnóstico inicial | Resultado deste incremento |
|---|---|---|
| Proposta → pedido | implementado; build não verificado | moeda, arredondamento, conversão parcial e idempotência preservados |
| Reserva e saldo | parcial | mecanismo canônico reutilizado; disponibilidade e lock mantidos |
| Lote e qualidade | implementado | aprovação/validade revalidadas antes da saída |
| Separação/expedição | parcial | visão operacional e idempotência estrita da saída completadas |
| Entrega/devolução | implementado; não homologado neste ambiente | sem alteração funcional |
| Permissões/tenant/auditoria | implementado | endpoints continuam autorizados e todas as consultas tenant-scoped |
| Interface | parcial | fila paginada, busca comercial e detalhe em cinco seções |

## Responsabilidades e invariantes

Comercial registra o compromisso aceito; Estoque registra disponibilidade, reserva e movimento; Qualidade decide a elegibilidade do lote; Logística separa, expede, transporta e registra entrega; Financeiro reage apenas a seus próprios eventos. Pedido confirmado não é entrega, reserva não é saída e expedição não é pagamento ou autorização fiscal.

A visão calcula quantidades das tabelas de pedido, reservas e expedições, sem contadores paralelos. `pendente = pedida - cancelada - expedida`; reserva ativa e separação são subconjuntos do pendente. A migration ainda não adiciona cancelamento quantitativo: portanto, nesta versão `cancelada = 0`. Quantidade já expedida continua imutável e devolução mantém evento próprio.

## Homologação requerida

1. Aplicar migrations até `109` em cópia descartável e também criar uma base limpa pelo SQL consolidado.
2. Executar restore, build e todas as suítes.
3. Com PostgreSQL real, validar duas reservas concorrentes, rollback induzido e isolamento entre tenants.
4. Pela tela Logística, filtrar e paginar pedidos, abrir o detalhe, criar atendimento existente, confirmar saída duas vezes com a mesma chave e repetir com chave/conteúdo conflitante.
5. Validar desktop, viewport móvel, teclado e leitor de tela.

Cancelamento quantitativo de itens, liberação manual independente de reservas e um formulário pesquisável completo para criar reserva/separação continuam como próximos incrementos. Não se deve simular cancelamento alterando somente o status nem tratar devolução como edição da quantidade expedida.
