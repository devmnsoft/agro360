# Logística multimodal — auditoria, operação e implantação

## Baseline e classificação

Auditoria executada em 22/09/2026 na branch `work`, baseline `e061a25`. Não havia alterações locais nem `AGENTS.md`. O agregado canônico é `fulfillment_*`: reserva não baixa estoque; a expedição cria uma única saída física; aceite, recusa, recebimento de retorno e decisão de qualidade são fatos separados.

| Capacidade | Situação verificada | Evidência / decisão |
|---|---|---|
| contratos e pedidos | comprovada estaticamente | pedidos, itens e contratos já alimentam a fila; contrato não cria expedição implicitamente |
| reserva, lote, estoque e qualidade | comprovada estaticamente | locks, saldo reservado, lote aprovado/validez e movimento idempotente na expedição |
| entrega parcial, recusa e retorno | comprovada estaticamente | tentativa por item; retorno físico aguarda inspeção e destinação |
| programação multimodal | implementada, runtime não verificado | plano idempotente, paradas, trechos ROAD/RIVER/MIXED e alocações sem reservar estoque |
| capacidade | parcial | unidade e capacidade por trecho são obrigatórias; peso/volume ausente continua desconhecido, sem conversão |
| transportador/recurso | parcial | transportador textual e `asset_id`; disponibilidade temporal automática depende de cadastro/tempos ainda não unificados |
| custos e indicadores | parcial | frete legado e indicadores de fulfillment existem; rateio auditável previsto/contratado/realizado ainda não foi criado |
| interface | parcial | lista responsiva de viagens e resumo persistido do plano; editor assistido de paradas/carga permanece pendente |
| otimização, telemetria e integrações | não disponível | nenhum dado meteorológico, nível de rio, autorização legal ou condição de navegação é inferido |

## Regras entregues

`POST /api/logistics/trips/plans` exige `logistics.write`; consulta do plano exige `logistics.read`. A transação usa chave idempotente por tenant, trava consultiva por item e reconsulta o saldo ainda programável. Consolidação é possível entre itens compatíveis, mas a unidade precisa coincidir e a soma concorrente nunca pode exceder a quantidade conferida. Programar não cria reserva nem movimento.

Cada trecho guarda modal, recurso e capacidade com unidade explícita. Trecho fluvial exige fonte e validade da informação manual. Isso é somente evidência informada: não equivale a autorização de navegação. Peso e volume opcionais jamais são convertidos ou usados para declarar folga quando ausentes.

## Atualização e recuperação

1. Faça backup conforme `docs/BACKUP-RESTORE.md`.
2. Aplique as migrations pelo `Agro360.Migrator`; a `103_multimodal_logistics_planning.sql` é incremental e mantém os fatos anteriores.
3. Inicie API e Web com a mesma conexão, autentique um usuário tenant com `logistics.read`/`logistics.write` e valide criação/reenvio do plano.
4. Para recuperação, restaure o backup. Não remova somente as tabelas 103 após haver planos, pois as referências constituem histórico operacional.

## Gate e backlog explícito

O ambiente desta execução não contém SDK .NET, PostgreSQL, `psql` ou navegador configurado. Portanto build Release, instalação limpa, upgrade representativo, seed repetido, teste com role da aplicação e jornada de navegador estão **bloqueados por ambiente**, e não aprovados. Antes de produção, executar a jornada programar → separar → carregar → expedir → entregar parcialmente → receber retorno → inspecionar/destinar → encerrar.

Backlog: editor móvel completo do plano; restrição de sobreposição integrada ao cadastro canônico de frota/embarcação e tempos de preparação; transbordo operacional com custódia; revisão/reabertura e fechamento conciliado da viagem; custos com rateio e resíduos; indicadores navegáveis com denominadores; anexos; otimização automática; telemetria; meteorologia, hidrologia e autorizações externas.
