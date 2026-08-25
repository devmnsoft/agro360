# Catálogo de módulos

Status não é marketing: `CORE` significa fluxo conectado nesta versão; `FOUNDATION` significa schema/contrato inicial; `PLANNED` ainda não deve aparecer como concluído comercialmente.

| Domínio | Abrangência | Estado | Fase |
|---|---|---:|---:|
| Identidade/Tenancy | JWT, refresh, tenant, organização, RBAC | CORE | 1 |
| Propriedades | fazenda, talhão, PostGIS, documentos cadastrais | CORE | 2 |
| Digital Twin | mapa operacional por camadas | PLANNED | 2–24 |
| Agricultura | API de safra, plantio e colheita; UI operacional pendente | FOUNDATION | 3 |
| Caderno de Campo | aplicação, operador, GPS, condição, fotos | FOUNDATION | 3 |
| Agronomia | solo, folha, fertilidade, praga, receituário | PLANNED | 3+ |
| Precisão | produtividade, manejo, taxa variável, satélite | PLANNED | 3+ |
| Clima/Irrigação | estação, alertas, pivô, lâmina e energia | PLANNED | 23 |
| Estoque | API de produto, depósito, saldo e movimento; UI operacional pendente | FOUNDATION | 4 |
| Inventário | contagem, divergência e ajuste auditável | PLANNED | 4 |
| Frota | ativo e horímetro | FOUNDATION | 5 |
| Manutenção | preventiva, corretiva e preditiva | PLANNED | 5 |
| Combustível | tanque, abastecimento, anomalia e custo | PLANNED | 5 |
| Pecuária | API de animal, identificação e timeline; UI operacional pendente | FOUNDATION | 6 |
| Pesagem | API individual e GMD; UI operacional pendente | FOUNDATION | 6 |
| Sanidade | API de tratamento, medicamento, custo e carência; UI operacional pendente | FOUNDATION | 6 |
| Reprodução/Genética | cio, IA, gestação, parto, genealogia, DEP | PLANNED | 6 |
| Pastagens | pasto, piquete, lotação e descanso | PLANNED | 7 |
| Confinamento | dieta, trato, conversão e custo/@ | PLANNED | 7 |
| Compras | requisição, cotação, alçada, pedido e recebimento | FOUNDATION | 8 |
| Fornecedores | histórico, documento, preço e score | PLANNED | 8 |
| Financeiro | recebível integrado a venda | FOUNDATION | 9 |
| Caixa/Bancos/DRE | pagar, receber, conciliar, orçamento e fluxo | PLANNED | 9 |
| Motor de Custos | apropriação transacional inicial por origem | FOUNDATION | 10 |
| Rateio | regra reproduzível e auditável | PLANNED | 10 |
| Comercial | API de venda vegetal/animal e recebível; UI operacional pendente | FOUNDATION | 11 |
| Contratos | compra, venda, barter, transporte e arrendamento | PLANNED | 11 |
| Armazenagem | recepção, qualidade, secagem, silo e expedição | PLANNED | 12 |
| Balança | duas pesagens e peso líquido | PLANNED | 12 |
| Logística | fundação de carga e estados | FOUNDATION | 13 |
| Torre Logística | mapa, atraso e bloqueio | PLANNED | 13 |
| AgroGraph | nós, arestas e consulta API autorizada; navegação UI pendente | FOUNDATION | 14 |
| QR de Origem | visão pública controlada | PLANNED | 14 |
| Web/PWA | Command Center responsivo e shell offline; formulários operacionais pendentes | FOUNDATION | 15 |
| Mobile MAUI | contratos Local Outbox e conflitos | FOUNDATION | 15–16 |
| GED Agro | metadados, checksum, storage, versão e OCR status | FOUNDATION | 17 |
| Ambiental/ESG | compliance e vencimentos | FOUNDATION | 18 |
| Carbono | atividade, fator e versão metodológica | PLANNED | 18 |
| Cooperativas | cooperado, recebimento, saldo e assistência | PLANNED | 19 |
| Revendas/CRM | oportunidade, pedido, crédito e cobrança | PLANNED | 20 |
| Agroindústria | matéria-prima, processo, lote e qualidade | PLANNED | 21 |
| Fábrica de Ração | fórmula, ingrediente, lote, custo e consumo | PLANNED | 21 |
| Leite | ordenha, tanque, CCS, CBT e lactação | PLANNED | 22 |
| Aves/Suínos | alojamento, ração, mortalidade e conversão | PLANNED | 22 |
| Aquicultura | viveiro, biomassa, água, ração e despesca | PLANNED | 22 |
| Florestal | inventário, manejo, corte e origem | PLANNED | 22 |
| IoT | dispositivo e telemetria validável | FOUNDATION | 23 |
| BI/Data Quality | Command Center e base de score | CORE/FOUNDATION | 24 |
| Copilot | contexto, permissão e explicabilidade | PLANNED | 25 |
| Preditivo | produtividade, peso, compra, caixa e anomalia | PLANNED | 26 |
| Workflow/Regras | definição/instância configurável | FOUNDATION | transversal |
| Notificações | alerta interno e canais por adapters | FOUNDATION | transversal |
| RH/Segurança | trabalhador, escala, EPI, risco e acidente | FOUNDATION | transversal |

## Setores atendidos pelo modelo

- vegetal: soja, milho, arroz, feijão, trigo, sorgo, algodão, cana, café, cacau, açaí, dendê, citrus, uva, frutas, hortaliças, flores, sementes, pasto, orgânico e irrigado;
- animal: bovino de corte/leite, bubalino, suíno, aves, ovino, caprino, equino, peixe, camarão, aquicultura e apicultura;
- florestal: eucalipto, pinus, manejo, reflorestamento e agrofloresta;
- pós-produção: silo, cerealista, beneficiamento, algodoeira, agroindústria, fábrica de ração, frigorífico e laticínio;
- cadeia: produtor, grupo econômico, cooperativa, associação, revenda, trading, comprador, transportadora, prestador, instituição financeira, seguradora e laboratório.
