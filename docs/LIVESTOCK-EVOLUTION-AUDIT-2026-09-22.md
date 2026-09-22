# Auditoria da evolução pecuária — 2026-09-22

## Contexto e gate

A auditoria iniciou na branch `work`, commit `14e1483`. Não havia alterações locais
nem arquivos `AGENTS.md` no repositório ou diretório pai. Foram inspecionados a
solução .NET, contratos, domínio, serviços Dapper, controllers, Razor/JavaScript,
migrations, consolidado, seeds e suítes de teste.

O gate dinâmico permanece **bloqueado pelo ambiente** quando uma ferramenta não
estiver instalada. Uma validação estática aprovada não é apresentada como build,
teste PostgreSQL, isolamento RLS ou homologação de navegador.

Nesta execução, `dotnet` e `psql` não estavam no `PATH`; `node` estava disponível.
O validador de banco e o verificador offline passaram, enquanto restore, build,
testes .NET, PostgreSQL com role da aplicação e navegador ficaram não executados.

## Entidades canônicas e limites

- `livestock_animals` é o cadastro individual; identificadores ativos e histórico
  pertencem ao animal.
- `livestock_herds` é o lote pecuário. `INDIVIDUAL` representa membros cadastrados;
  `QUANTITY` representa somente a parcela ainda coletiva. Durante individualização
  parcial, o total conservado é a quantidade coletiva remanescente mais os
  indivíduos vinculados, nunca duas contagens da mesma cabeça.
- `livestock_handling_lots` é uma seleção operacional e temporal, não estoque nem
  lote pecuário.
- `inventory_stock_lots` é lote de insumo/produto físico. Seu ciclo de saldo e
  validade não é substituído pelo lote pecuário; manejos referenciam seu movimento
  canônico de consumo.
- Lotes de produto/rastreabilidade continuam no domínio de estoque, processamento
  e genealogia, sem vínculo de composição implícito com rebanhos.

## Matriz de fluxos

| Fluxo | Estado | Evidência e limite |
|---|---|---|
| Produtor, fazenda e instalação | **Parcial** | Fazenda e instalação possuem persistência e filtro tenant/fazenda; execução PostgreSQL e ACL real pendentes. |
| Animal individual | **Parcial** | Cadastro, identificação, troca com histórico, detalhe e situação existem; jornada real não foi executada. |
| Lote pecuário coletivo | **Parcial** | Modalidades canônicas e saldo coletivo existem. Esta entrega corrige a incompatibilidade legada `COLLECTIVE`/`QUANTITY`. |
| Individualização | **Implementado, não homologado** | Operação transacional agora aceita parcela, rejeita duplicados/excesso, trava o lote, suporta versão esperada e idempotência e registra reconciliação. Banco real permanece pendente. |
| Movimentações | **Parcial** | Entrada/transferência/saída, locks, situação terminal e chaves idempotentes existem; timeline retroativa completa e concorrência real não foram comprovadas. |
| Pesagens | **Parcial** | Individual/coletiva, unidade, população, suspeita, correção e GMD individual existem; comparabilidade coletiva por snapshot ainda requer fechamento. |
| Manejo e consumo | **Parcial** | Ordem, população materializada, atendimento parcial, restrição e materiais existem; execução integrada com estoque/custo precisa do E2E PostgreSQL. |
| Estoque e custos | **Parcial** | Entidades canônicas de lote/movimento/rateio existem e não foram duplicadas; reconciliação pecuária completa não foi executada. |
| Indicadores e central operacional | **Parcial** | Dashboard e tarefas de manejo existem; denominadores e pendências de composição ainda precisam de validação dinâmica. |
| Interface pecuária | **Parcial** | Navegação, filtros, ajuda e formulários existem; acessibilidade, celular e jornada visual não foram revalidados nesta execução. |
| Isolamento e autorização | **Parcial** | Filtros `tenant_id`, transações, RLS/policies e policies HTTP existem; dois tenants, anexos, relatórios e lote não foram exercitados. |

## Regras fechadas neste incremento

1. O vocabulário persistido é somente `INDIVIDUAL` ou `QUANTITY`; valores
   `COLLECTIVE` legados são migrados sem reescrever migrations aplicadas.
2. A lista da individualização precisa ser não vazia, distinta e não pode exceder
   o saldo coletivo bloqueado com `FOR UPDATE`.
3. Uma individualização parcial reduz o saldo coletivo exatamente pela quantidade
   de novos indivíduos. A última parcela muda o modo para `INDIVIDUAL` e recompõe
   `head_count` a partir dos membros persistidos.
4. Um animal já vinculado não pode consumir o saldo novamente. `ExpectedVersion`
   detecta edição concorrente e `IdempotencyKey`, única por tenant, torna o reenvio
   seguro.
5. Cada confirmação preserva quantidade anterior, quantidade individualizada,
   data, ator, motivo e chave da operação em histórico tenant-scoped.

## Atualização e recuperação

1. Faça backup conforme `database/maintenance/backup.sh`.
2. Execute o Migrator canônico: `dotnet run --project src/Hosts/Agro360.Migrator -- migrate`.
3. Alternativamente, somente em banco vazio, aplique
   `database/agro360-postgres-full.sql` com `ON_ERROR_STOP=1`.
4. Valide os modos: não deve existir `control_mode` fora de `INDIVIDUAL` e
   `QUANTITY`. Valide também reconciliações cuja quantidade identificada exceda a
   coletiva original (a constraint impede novas ocorrências).
5. Em falha antes do commit, PostgreSQL reverte a migration. Depois do commit, não
   edite a migration aplicada: recupere o backup ou publique uma migration
   compensatória.

## Backlog priorizado após o gate

1. Executar instalação limpa/reaplicação/upgrade e testes com role da aplicação e
   dois tenants, cobrindo concorrência e reenvio. Essa execução deve também
   confirmar a ordem das duas migrations históricas `068_*`; seus checksums não
   foram alterados por esta entrega.
2. Materializar snapshot de composição para pesagens coletivas e bloquear GMD
   quando as populações/metodologias não forem comparáveis.
3. Validar evento retroativo contra todos os eventos posteriores e implementar
   retificação/compensação explícita para movimentações confirmadas.
4. Fechar consumo e apropriação de custo por população histórica, gerando
   pendência quando não houver base válida.
5. Executar a jornada de navegador e auditoria de teclado, foco, contraste,
   estados e viewport móvel; registrar screenshots somente após execução real.
