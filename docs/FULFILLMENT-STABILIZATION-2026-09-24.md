# Estabilização operacional do fulfillment

## Semântica canônica

O saldo ativo de uma reserva é sempre `quantity - consumed_quantity - released_quantity`. Os três campos são cumulativos históricos e nunca são sobrescritos para esconder uma operação anterior. Quantidade zero não significa conferência: a conclusão é registrada por `check_completed`.

No despacho, a implementação preserva a política já adotada pelo fluxo: consome somente a quantidade conferida e libera o remanescente **ativo naquele instante**. O saldo agregado reservado é reduzido pelo saldo ativo, nunca pela quantidade original. A alteração da reserva, lote, saldo, movimento e documento ocorre na mesma transação e qualquer contagem inesperada provoca rollback.

## Reabertura

Antes do despacho, um documento `CHECKED` pode ser reaberto por usuários com `LogisticsWrite`. A requisição exige versão, motivo, chave idempotente e a lista explícita de itens. Somente os itens escolhidos têm separação/conferência zeradas; seus valores anteriores ficam em `fulfillment_preparation_reopens` e na auditoria. Reabrir não libera estoque. Depois disso, liberação e cancelamento permanecem decisões separadas. Após despacho a operação é recusada; material expedido deve seguir o fluxo de devolução.

## Concorrência e recuperação

As mutações usam o bloqueio consultivo do item comercial antes dos registros físicos, versões otimistas e bloqueios `FOR UPDATE`. Chaves idempotentes vinculam tenant, operação, agregado e hash do conteúdo. A tela mantém a chave em `sessionStorage` para uma nova tentativa do mesmo conteúdo, inclusive após resultado de rede desconhecido; uma intenção alterada recebe outra chave.

## Diagnóstico e upgrade

A migration `111_fulfillment_reopen_and_active_balance.sql` é incremental e não altera a migration 110. A consulta comentada ao final identifica reservas negativas, excedidas ou marcadas como ativas sem saldo. Linhas retornadas devem ser investigadas e corrigidas por procedimento auditado; a migration não aplica compensações destrutivas.

## Próximo pacote

Após a homologação deste marco, o próximo pacote é o editor de propostas e a apresentação correta de moedas. Ele não integra esta entrega.
