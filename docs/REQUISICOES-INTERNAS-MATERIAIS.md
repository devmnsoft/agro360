# Requisições internas de materiais

## Regra operacional e eventos contábeis

A jornada canônica é **rascunho → submissão → aprovação configurável → reserva → separação/entrega → consumo → devolução**. A aprovação apenas autoriza; a reserva compromete `inventory_stock_balances.reserved`, mas não cria saída nem custo. A separação permanece representada pela reserva até a confirmação. A **entrega** reduz `available` e `reserved` na mesma transação e cria o movimento físico `CONSUMPTION` com origem `MATERIAL_DELIVERY`. O **consumo operacional** referencia a entrega e apropria seu custo médio ao destino, sem nova baixa física. Quando o operador marca consumo direto, a apropriação nasce junto da entrega e uma apropriação posterior é rejeitada. Uma devolução `GOOD` cria entrada pelo mesmo custo; `DAMAGED`/`UNKNOWN` aguarda inspeção e não fica disponível.

As requisições reutilizam produtos, depósitos, saldos, lotes, movimentos e custos existentes. Locks `FOR UPDATE`, versão e chaves idempotentes protegem reserva, entrega, consumo e devolução. Cancelar libera somente reserva remanescente; uma requisição com entrega exige devolução ou estorno controlado, nunca exclusão física.

## Critérios de aceite e uso

1. Escolher unidade, finalidade, data, prioridade e produto por nome; quantidade deve ser positiva e a unidade é herdada do produto.
2. Urgência exige justificativa. Depósito deve pertencer à unidade e todas as referências são filtradas pelo tenant.
3. A submissão consulta regras de aprovação configuradas por unidade, categoria e prioridade; não há alçada fixa. Segregação configurada impede autoaprovação.
4. A reserva revalida o disponível (`físico - reservado`) sob lock. A entrega deve somar exatamente os lotes elegíveis escolhidos e pode ser parcial.
5. Consumo e devolução nunca excedem o saldo da entrega. Material já consumido não é devolvido como sobra.
6. Todas as decisões geram histórico com ator e horário; reprovação e cancelamento exigem motivo.

## Auditoria dos formulários (2026-09-15)

| Formulário | Válido/inválido | Releitura | Estado/cancelamento | Permissão/concorrência | Situação |
|---|---|---|---|---|---|
| Nova requisição/rascunho | valida itens, produto, unidade, urgência e escopo | GET detalhe/lista | submissão persistida | `inventory.move`, tenant | Implementado; runtime PostgreSQL pendente |
| Decisão | motivo obrigatório para reprovar/ajustar | timeline | aprovado/reprovado/ajuste | `inventory.adjust`, versão e segregação | Implementado; workflow externo não duplicado |
| Reserva | limite solicitado/disponível | saldos no detalhe | cancelamento libera remanescente | lock + idempotência | Implementado; corrida real pendente |
| Entrega parcial | lote/soma/unidade | movimento + detalhe | parcial permanece pendente | lock + versão + idempotência | Implementado; navegador pendente |
| Consumo | saldo e consumo direto | histórico/custo | impede dupla apropriação | idempotência | Implementado; integração de relatórios pendente |
| Devolução | saldo, lote, condição | movimento/histórico | inspeção quando aplicável | idempotência | Implementado; decisão de qualidade da devolução pendente |

## Limitações verificáveis

O contêiner desta execução não possui o SDK .NET 10 nem um PostgreSQL configurado. Assim, restore/build/testes, instalação limpa, concorrência real, autenticação e testes desktop/mobile/teclado precisam ser executados em homologação. Procedimento: instalar SDK `10.0.100`, subir `docker compose up -d postgres`, executar o Migrator, `dotnet restore`, `dotnet build -c Release`, `dotnet test -c Release` e percorrer `/Inventory` com perfis de leitura, operação e ajuste.
