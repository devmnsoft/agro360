# Frota e Máquinas — Sprint 35

## Visão funcional

A central `/Fleet` consolida ativos, operadores, planos preventivos, solicitações corretivas, ordens de serviço, abastecimentos, lubrificações, pneus/componentes, paradas, custos e relatórios. O dashboard consulta dados reais do tenant e permanece estável sem registros. Busca, status, tabelas responsivas, estados de carregamento/vazio e exportação CSV fazem parte da experiência.

## Operação

1. Cadastre tipos e combustíveis autorizados no banco e acesse **Frota e Máquinas**.
2. Em **Ativos**, selecione tipo, propriedade, centro de custo e operador pelos campos de escolha; IDs técnicos nunca são digitados.
3. Cadastre operadores ativos, vínculo, função, habilitações e validade. Operador inativo não deve ser escalado; validade vencida integra a fila de alertas.
4. Crie o plano preventivo com periodicidade positiva (data, odômetro, horímetro, safra, operações ou condição manual) e checklist.
5. Abra a OS preventiva/corretiva. Prioridade crítica bloqueia o ativo; conclusão exige serviço realizado e cancelamento exige motivo.
6. Registre abastecimento com quantidade positiva. O total é calculado no backend e gera uma única origem de custo.
7. Em pneus, cada componente possui vínculo único e eventos de montagem, posição, desgaste, recapagem, remoção e baixa.
8. Registre a parada; enquanto aberta, ela pode tornar o ativo indisponível. O período alimenta disponibilidade.

## Segurança, auditoria e integrações

Todas as consultas Dapper são parametrizadas e filtradas por `tenant_id`; RLS reforça o isolamento. Código e placa são únicos por tenant. Alterações de medidor, status e OS geram eventos auditáveis. Custos exigem permissões de frota/manutenção.

Estoque de combustível/peças, contas a pagar, logística, SST, documentos, alertas, BI e mobile são integrados por chaves de origem e IDs opcionais reais. Provedores externos de telemetria, posto, fabricante e rastreador **não estão configurados**: não há simulação nem mock apresentado como integração.
