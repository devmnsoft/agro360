# Inteligência Agro360 — Sprint 31

A Central de Inteligência transforma dados operacionais do próprio tenant em recomendações, scores, anomalias e prioridades. O funcionamento padrão é determinístico: consultas parametrizadas, regras versionadas e fatores persistidos. Não depende de provider de IA e não apresenta inferências como fatos.

## Operação

1. Acesse **Inteligência Agro360** e use a Central de Decisão.
2. Confira motivo, módulo, severidade e origem antes de aceitar uma recomendação.
3. Aceite, recuse com motivo ou arquive. A decisão é auditada e nunca altera o registro de origem automaticamente.
4. Em **Prioridades do Dia**, abra o item real pelo atalho. Itens concluídos não permanecem na fila aberta.
5. Em **Anomalias**, compare valor observado, referência e critério. Anomalia significa desvio/risco, não fraude.

Scores vão de 0 a 100: `LOW` (baixo), `ATTENTION` (atenção), `HIGH` (alto) e `CRITICAL` (crítico). A tela informa versão da fórmula e fatores; integrações devem recalcular após eventos relevantes.

## Segurança e homologação

Todas as tabelas possuem `tenant_id`, RLS forçada e índices de fila. A API também inclui `tenant_id=@TenantId` em cada consulta Dapper. Homologue com dois tenants, usuários apenas de leitura e de escrita, tentando acessar UUID conhecido do outro tenant. Valide desktop, tablet, celular, teclado, foco, loading, erro e empty state.

O PostgreSQL é externo. Defina `ConnectionStrings__Agro360`, execute `psql "$ConnectionStrings__Agro360" -v ON_ERROR_STOP=1 -f database/agro360-postgres-full.sql` e inicie API/Web com `dotnet run --project ...`; Docker não é necessário.
