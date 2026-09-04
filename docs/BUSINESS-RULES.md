# Regras de negócio

## Regras transversais

1. todo registro operacional pertence a um tenant;
2. tenant suspenso não cria operações;
3. dados históricos não somem por mudança de plano;
4. suporte é temporário, autorizado e auditado;
5. alteração concorrente nunca é sobrescrita silenciosamente;
6. operações repetíveis aceitam `idempotencyKey`;
7. toda quantidade usa unidade do catálogo ou conversão explícita;
8. dinheiro nunca usa ponto flutuante binário;
9. data/hora operacional é persistida em UTC; data agronômica usa `date` quando hora não importa;
10. ações de IA são recomendações; pagamento, venda, compra, ajuste, sanidade, aplicação química e permissão exigem humano.

## Propriedades e GIS

- fazenda pertence a uma organização do mesmo tenant;
- talhão pertence a uma fazenda e possui área positiva;
- GeoJSON é normalizado para SRID 4326;
- gêmeo digital agrega lavoura, rebanho, máquina, sensor, custo e ocorrência sem duplicar a fonte oficial;
- CAR, CCIR, ITR e licenças são controle documental, não substituem órgãos oficiais.

## Agricultura

- safra possui início anterior ao fim, cultura, área e produtividade esperada;
- plantio exige safra aberta, talhão da mesma fazenda e área menor ou igual à área do talhão;
- plantios incompatíveis não podem sobrepor talhão e período;
- insumo aplicado gera saída de estoque, custo no talhão/safra, operação, auditoria e evento Outbox na mesma transação;
- colheita exige safra ativa, registra origem no talhão/safra, entra no estoque e herda custo acumulado;
- colheita cria relações no AgroGraph: safra → operação → produto → estoque;
- operações manuais e mecanizadas ampliarão o mesmo modelo de custo, sem tabelas paralelas por cultura.

## Estoque

- estoque negativo é proibido por regra de domínio e `CHECK` no banco;
- reserva não é saída;
- consumo considera `available - reserved`;
- produto controlado exige lote; perecível exige validade;
- custo médio é recalculado apenas em entradas com valor;
- consumo preserva o custo médio corrente;
- transferência sempre possui origem e destino e será atômica;
- ajuste exige permissão, motivo e auditoria reforçada;
- movimento é imutável; correção usa movimento inverso;
- idempotência impede dupla entrada/baixa em repetição de rede.

## Pecuária

- animal pode ser individual ou pertencer a lote/rebanho;
- brinco é único no tenant; RFID, quando informado, também;
- toda alteração operacional entra na timeline;
- pesagem posterior calcula GMD: `(peso atual − peso anterior) / dias`;
- pesagem no mesmo dia ou anterior é rejeitada;
- tratamento consome medicamento e gera custo no animal;
- carência sanitária é estendida pela maior data vigente, nunca encurtada silenciosamente;
- venda é bloqueada enquanto `saleDate <= withdrawalUntil`;
- animal vendido, morto ou abatido não recebe manejo normal;
- genealogia usa vínculos mãe/pai dentro do mesmo tenant.

## Comercial e financeiro

- venda agrícola exige saldo e depósito de origem;
- venda animal exige animal ativo e sem carência;
- confirmação da venda baixa estoque ou muda status do animal, cria venda, recebível, auditoria, rastreabilidade e Outbox atomicamente;
- recebível nasce aberto e não pode receber valor maior que o total;
- cancelamento de venda deve estornar estoque/status e financeiro; nunca apaga registros;
- margem estimada do Command Center é recebíveis menos custos operacionais no escopo selecionado;
- rateios futuros devem guardar método, base, percentuais e versão para reprodução.

## AgroGraph

- cada nó referencia uma entidade real por `(tenant, type, id)`;
- aresta não pode ligar o nó a si próprio;
- duplicidade de relação é ignorada de forma idempotente;
- consulta pode percorrer nos dois sentidos com profundidade limitada;
- dados públicos para QR de origem ficam separados de dados internos;
- toda consulta respeita RLS e permissão do usuário.

## Segurança e LGPD

- JWT contém tenant, usuário e permissões; nome de perfil não autoriza sozinho;
- refresh token é aleatório, persistido apenas como SHA-256 e rotacionado;
- senha usa PBKDF2-SHA512 com salt individual e 210 mil iterações;
- logs não recebem senha, token, connection string ou conteúdo sensível;
- exportação, anonimização e retenção serão workflows auditados;
- dados de outro cliente nunca entram em benchmark sem autorização e anonimização.

## Integrações, IoT e IA

- adapter isola fornecedor externo (`IWeatherProvider`, `IRfidProvider`, `IStorageProvider` etc.);
- dispositivo grava telemetria bruta/validada; nunca altera tabela de negócio diretamente;
- fluxo IoT: Device → Gateway → Telemetry → Validation → Rules → Domain Event → Business Action;
- IA conhece usuário, permissão, fazenda, período e módulo;
- recomendação mostra motivo, dados, período, confiança e fonte;
- falha de integração usa retry limitado, backoff e dead-letter/outbox sem duplicar operação.
# Prontidão de implantação

- O diagnóstico é sempre calculado no backend e isolado pelo `tenant_id` autenticado.
- Usuário, perfil, módulo contratado, fazenda e checklist obrigatório contribuem para a prontidão; ausência de dados gera uma ação recomendada, não um valor fictício.
- Plano e módulo contratado não substituem permissão: o endpoint continua protegido por `deployment.read`.
- O Super Administrador mantém acesso global por política; demais usuários permanecem no tenant emitido no token.

## Comercial Agro 360 (sprint atual)

Consulte `docs/COMMERCIAL-AGRO.md` para fluxo, regras implementadas, modelo persistente e pendências reais de integração.

## Regras de acesso do shell

- A navegação é derivada das permissões devolvidas pelo login; o Super Administrador da plataforma é a exceção global.
- Links administrativos globais permanecem exclusivos do Super Administrador.
- A ausência de componentes do dashboard em uma página de módulo não pode interromper login, refresh token, menu ou scripts específicos do módulo.
