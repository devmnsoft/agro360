using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Infrastructure.Services;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Agro360.IntegrationTests;

/// <summary>
/// Jornada real de suprimentos sobre PostgreSQL (exigência da homologação): requisição aprovada →
/// cotação com dois fornecedores → decisão/conversão. Cobre os contratos endurecidos da conversão:
/// menor preço é recomendação (confirmção explícita exigida antes de qualquer gravação), um pedido
/// por fornecedor vencedor em transação única (falha de um fornecedor nunca deixa pedido parcial),
/// replay idempotente inclusive com pedidos cancelados, e auditoria start/passo/conclusão/falha sem
/// dados sensíveis. Os serviços reais rodam sobre oexecutor canônico (DatabaseExecutor), com tenant
/// próprio por teste para garantir isolamento e repetibilidade.
/// </summary>
public sealed class ProcurementConversionJourneyTests
{
    static ProcurementConversionJourneyTests() =>
        // Reproduz o registro global que AddAgro360Infrastructure faz no aplicativo; sem ele o
        // Dapper rejeita DateOnly como parâmetro (a jornada grava delivery_on/needed_on).
        Dapper.SqlMapper.AddTypeHandler(new Agro360.Infrastructure.DateOnlyTypeHandler());

    private static readonly string[] RequisitionConsumedStatuses = ["PARTIALLY_FULFILLED", "CONVERTED"];

    [Fact]
    public async Task RecomendacaoDeMenorPrecoExigeConfirmacaoExplícitaAntesDeGravar()
    {
        await using var journey = await Journey.CreateAsync(TestContext.Current.CancellationToken);

        // 1) Sem decisão explícita a conversão recusa e não grava absolutamente nada.
        var refusal = await Assert.ThrowsAnyAsync<DomainException>(
            () => journey.Quotations.ConvertToOrderAsync(journey.QuotationId, false, TestContext.Current.CancellationToken));
        Assert.Equal("agro360.quotation.confirmation_required", refusal.Code);
        Assert.Equal(0, await Journey.ScalarAsync<int>("select count(*) from agro360.procurement_purchase_orders where quotation_id=@Id", new { Id = journey.QuotationId }));
        Assert.Equal(0, await Journey.ScalarAsync<int>("select count(*) from agro360.procurement_quotation_decisions where quotation_id=@Id", new { Id = journey.QuotationId }));
        Assert.Equal(0, await Journey.ScalarAsync<int>("select count(*) from agro360.procurement_audit_events where entity_id=@Id and action in('CONVERSION_STARTED','CONVERSION_FAILED')", new { Id = journey.QuotationId }));
        Assert.Equal("ANALYSIS", await Journey.ScalarAsync<string>("select status from agro360.procurement_quotations where id=@Id", new { Id = journey.QuotationId }));

        // 2) Decisão explícita em apenas um item não silencia a guarda: o outro item ainda seria
        // decidido silenciosamente pelo menor preço.
        await Journey.ExecuteAsync("""
            insert into agro360.procurement_quotation_decisions(id,tenant_id,quotation_id,quotation_item_id,quotation_supplier_id,selected_total,lowest_total,justification,decided_by,created_by,updated_by)
            values(gen_random_uuid(),@TenantId,@Quote,@Item1,@RowA,120,100,'Fornecedor histórico com melhor atendimento técnico',@UserId,@UserId,@UserId)
            """, new { journey.TenantId, Quote = journey.QuotationId, journey.Item1, journey.RowA, journey.UserId });
        var partialGuard = await Assert.ThrowsAnyAsync<DomainException>(
            () => journey.Quotations.ConvertToOrderAsync(journey.QuotationId, false, TestContext.Current.CancellationToken));
        // Recusa esperada de negócio: não é falha de conversão e não pode sujar a auditoria.
        Assert.IsType<DomainException>(partialGuard);
        Assert.Equal("agro360.quotation.confirmation_required", partialGuard.Code);
        Assert.Equal(0, await Journey.ScalarAsync<int>("select count(*) from agro360.procurement_purchase_orders where quotation_id=@Id", new { Id = journey.QuotationId }));

        // 3) Conversão confirmada: decide o item restante pelo menor preço, um pedido por fornecedor
        // vencedor (A fica com o item 1 decidido; B vence o item 2) e registro auditável da confirmação.
        var orderIds = await journey.Quotations.ConvertToOrderAsync(journey.QuotationId, true, TestContext.Current.CancellationToken);
        Assert.Equal(2, orderIds.Count);
        var winners = await Journey.QueryAsync<Guid>("select supplier_id from agro360.procurement_purchase_orders where quotation_id=@Id", new { Id = journey.QuotationId });
        Assert.Equal(new[] { journey.SupplierA, journey.SupplierB }.OrderBy(x => x).ToArray(), winners.OrderBy(x => x).ToArray());

        var autoDecision = await Journey.QuerySingleAsync<(decimal Selected, decimal Lowest, string Justification)>(
            "select d.selected_total Selected,d.lowest_total Lowest,d.justification Justification from agro360.procurement_quotation_decisions d where d.quotation_id=@Id and d.quotation_item_id=@Item2",
            new { Id = journey.QuotationId, journey.Item2 });
        Assert.Equal(400m, autoDecision.Selected);
        Assert.Equal(400m, autoDecision.Lowest);
        Assert.Equal("Conversão confirmada sobre a recomendação de menor preço", autoDecision.Justification);

        Assert.Equal("APPROVED", await Journey.ScalarAsync<string>("select status from agro360.procurement_quotations where id=@Id", new { Id = journey.QuotationId }));
        Assert.Contains(await Journey.ScalarAsync<string>("select status from agro360.procurement_requisitions where id=@Id", new { Id = journey.RequisitionId }), RequisitionConsumedStatuses);
        // Saldo autorizado integralmente consumido nas duas linhas.
        Assert.Equal(0m, await Journey.ScalarAsync<decimal>("""
            select coalesce(sum(ri.quantity-(select coalesce(sum(oi.quantity),0) from agro360.procurement_purchase_order_items oi
                join agro360.procurement_purchase_orders o on o.tenant_id=oi.tenant_id and o.id=oi.purchase_order_id
                where oi.tenant_id=ri.tenant_id and oi.requisition_item_id=ri.id and o.status<>'CANCELLED')),0)
            from agro360.procurement_requisition_items ri where ri.requisition_id=@Id
            """, new { Id = journey.RequisitionId }));

        // Auditoria completa da conversão bem-sucedida.
        var actions = await Journey.QueryAsync<string>("select distinct action from agro360.procurement_audit_events where entity_id=@Id", new { Id = journey.QuotationId });
        Assert.Contains("CONVERSION_STARTED", actions);
        Assert.Contains("CONVERSION_ORDER_CREATED", actions);
        Assert.Contains("CONVERTED", actions);

        // 4) Replay devolve exatamente os mesmos pedidos, sem duplicar e sem novo evento de criação.
        var replay = await journey.Quotations.ConvertToOrderAsync(journey.QuotationId, true, TestContext.Current.CancellationToken);
        Assert.Equal(orderIds.OrderBy(x => x).ToArray(), replay.OrderBy(x => x).ToArray());
        Assert.Equal(2, await Journey.ScalarAsync<int>("select count(*) from agro360.procurement_purchase_orders where quotation_id=@Id", new { Id = journey.QuotationId }));
    }

    [Fact]
    public async Task FornecedorBloqueadoDerrubaTodaAConversaoESemRecriacaoAposRegularizacao()
    {
        await using var journey = await Journey.CreateAsync(TestContext.Current.CancellationToken);
        // Decisão explícita leva o item 1 para A (acima do menor preço); o item 2 vai para B pelo
        // menor preço. Bloqueando B, a conversão inteira falha — inclusive o pedido já inserido de A.
        await journey.DecideItemOneForSupplierAAsync();
        await Journey.ExecuteAsync("update agro360.procurement_suppliers set status='BLOCKED' where id=@Id", new { Id = journey.SupplierB });

        var failure = await Assert.ThrowsAnyAsync<DomainException>(
            () => journey.Quotations.ConvertToOrderAsync(journey.QuotationId, true, TestContext.Current.CancellationToken));
        Assert.Equal("agro360.procurement_supplier_unavailable", failure.Code);

        // Rollback total: nem mesmo o pedido do fornecedor saudável sobrevive à falha do outro.
        Assert.Equal(0, await Journey.ScalarAsync<int>("select count(*) from agro360.procurement_purchase_orders where quotation_id=@Id", new { Id = journey.QuotationId }));
        Assert.Equal("ANALYSIS", await Journey.ScalarAsync<string>("select status from agro360.procurement_quotations where id=@Id", new { Id = journey.QuotationId }));
        // A falha é auditada fora da transação com código higienizado (sem mensagem de negócio/SQL).
        Assert.Equal(1, await Journey.ScalarAsync<int>("""
            select count(*) from agro360.procurement_audit_events
            where entity_id=@Id and action='CONVERSION_FAILED' and changed_fields->>'Code'='agro360.procurement_supplier_unavailable'
            """, new { Id = journey.QuotationId }));

        // Regularizado o fornecedor, a conversão segue normalmente — sem recriação duplicada.
        await Journey.ExecuteAsync("update agro360.procurement_suppliers set status='ACTIVE' where id=@Id", new { Id = journey.SupplierB });
        var orderIds = await journey.Quotations.ConvertToOrderAsync(journey.QuotationId, true, TestContext.Current.CancellationToken);
        Assert.Equal(2, orderIds.Count);
        Assert.Equal(2, await Journey.ScalarAsync<int>("select count(*) from agro360.procurement_purchase_orders where quotation_id=@Id", new { Id = journey.QuotationId }));
    }

    [Fact]
    public async Task ConversoesConcorrentesSaoSerializadasEEvolverMesmosPedidos()
    {
        await using var journey = await Journey.CreateAsync(TestContext.Current.CancellationToken);
        // Dois fornecedores vencedores (A decidido, B por menor preço) para o replay concorrente ter
        // um conjunto não trivial de pedidos a reconvergir.
        await journey.DecideItemOneForSupplierAAsync();

        var results = await Task.WhenAll(
            journey.Quotations.ConvertToOrderAsync(journey.QuotationId, true, TestContext.Current.CancellationToken),
            journey.Quotations.ConvertToOrderAsync(journey.QuotationId, true, TestContext.Current.CancellationToken));

        var first = results[0].OrderBy(x => x).ToArray();
        var second = results[1].OrderBy(x => x).ToArray();
        Assert.Equal(first, second);
        Assert.Equal(2, await Journey.ScalarAsync<int>("select count(*) from agro360.procurement_purchase_orders where quotation_id=@Id", new { Id = journey.QuotationId }));
        Assert.Equal(2, await Journey.ScalarAsync<int>("select count(*) from agro360.procurement_purchase_order_items where tenant_id=@TenantId and requisition_item_id is not null", new { TenantId = journey.TenantId }));
    }

    [Fact]
    public async Task PedidoCanceladoPertenceAoReplayECancelamentoReabreSaldoSemRecriacaoAutomatica()
    {
        await using var journey = await Journey.CreateAsync(TestContext.Current.CancellationToken);
        await journey.DecideItemOneForSupplierAAsync();
        var orderIds = await journey.Quotations.ConvertToOrderAsync(journey.QuotationId, true, TestContext.Current.CancellationToken);
        var cancelledOrderId = await Journey.ScalarAsync<Guid>("select o.id from agro360.procurement_purchase_orders o where o.quotation_id=@Id and o.supplier_id=@Supplier", new { Id = journey.QuotationId, Supplier = journey.SupplierA });

        var version = await Journey.ScalarAsync<long>("select version from agro360.procurement_purchase_orders where id=@Id", new { Id = cancelledOrderId });
        await journey.Procurement.CancelOrderAsync(cancelledOrderId, new RequisitionTransitionCommand(version, "Cancelamento de teste da jornada"), TestContext.Current.CancellationToken);

        // Replay inclui o pedido cancelado: reabastecimento vem de novo pedido na requisição (comando
        // explícito), nunca de recriação automática embutida na conversão.
        var replay = await journey.Quotations.ConvertToOrderAsync(journey.QuotationId, true, TestContext.Current.CancellationToken);
        Assert.Equal(orderIds.OrderBy(x => x).ToArray(), replay.OrderBy(x => x).ToArray());
        Assert.Equal(2, await Journey.ScalarAsync<int>("select count(*) from agro360.procurement_purchase_orders where quotation_id=@Id", new { Id = journey.QuotationId }));

        // O cancelamento reabre o saldo da linha correspondente e devolve a requisição para aprovação.
        Assert.True(await Journey.ScalarAsync<decimal>("""
            select ri.quantity-coalesce((select sum(oi.quantity) from agro360.procurement_purchase_order_items oi
                join agro360.procurement_purchase_orders o on o.tenant_id=oi.tenant_id and o.id=oi.purchase_order_id
                where oi.tenant_id=ri.tenant_id and oi.requisition_item_id=ri.id and o.status<>'CANCELLED'),0)
            from agro360.procurement_requisition_items ri
            join agro360.procurement_purchase_order_items poi on poi.tenant_id=ri.tenant_id and poi.requisition_item_id=ri.id
            where poi.purchase_order_id=@Order
            """, new { Order = cancelledOrderId }) > 0);
        Assert.Equal("APPROVED", await Journey.ScalarAsync<string>("select status from agro360.procurement_requisitions where id=@Id", new { Id = journey.RequisitionId }));
    }

    /// <summary>Tenant isolado + jornada semeada (requisição aprovada, cotação ANALYSIS com dois
    /// fornecedores respondendo dois itens; os dois menores preços caem em B, então os testes que
    /// precisam de dois vencedores registram a decisão explícita do item 1 para A).</summary>
    private sealed class Journey : IAsyncDisposable
    {
        private readonly NpgsqlConnectionFactory connectionFactory;

        public Guid TenantId { get; private set; }
        public Guid UserId { get; private set; }
        public Guid RequisitionId { get; private set; }
        public Guid QuotationId { get; private set; }
        public Guid SupplierA { get; private set; }
        public Guid SupplierB { get; private set; }
        public Guid RowA { get; private set; }
        public Guid Item1 { get; private set; }
        public Guid Item2 { get; private set; }
        public TenantContext Tenant { get; private set; }
        public ProcurementService Procurement { get; private set; }
        public QuotationService Quotations { get; private set; }
        public static string ConnectionString
        {
            get
            {
                var connectionString = Environment.GetEnvironmentVariable("AGRO360_TEST_CONNECTION_STRING");
                if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
                    Assert.False(string.IsNullOrWhiteSpace(connectionString),
                        "AGRO360_TEST_CONNECTION_STRING é obrigatória no gate de integração do CI.");
                // Sem banco descartável configurado a jornada pula (skip) em vez de falhar: a ausência
                // do ambiente não é defeito do produto; no CI o Assert acima transforma em falha real.
                Assert.SkipWhen(
                    string.IsNullOrWhiteSpace(connectionString),
                    "Defina AGRO360_TEST_CONNECTION_STRING para executar a jornada de suprimentos sobre PostgreSQL real.");
                return connectionString!;
            }
        }

        private Journey()
        {
            TenantId = Guid.CreateVersion7();
            UserId = Guid.CreateVersion7();
            RequisitionId = Guid.CreateVersion7();
            QuotationId = Guid.CreateVersion7();
            SupplierA = Guid.CreateVersion7();
            SupplierB = Guid.CreateVersion7();
            RowA = Guid.CreateVersion7();
            Item1 = Guid.CreateVersion7();
            Item2 = Guid.CreateVersion7();
            connectionFactory = new NpgsqlConnectionFactory(
                new PostgreSqlConnectionConfiguration(ConnectionString, "IntegrationTests", "integration-test", "Development", "password", "integration-test", false),
                NullLogger<NpgsqlConnectionFactory>.Instance);
            Tenant = new TenantContext();
            Tenant.SetScope(new TenantScope(TenantId, UserId, null, null));
            var database = new DatabaseExecutor(connectionFactory, Tenant, NullLogger<DatabaseExecutor>.Instance);
            Procurement = new ProcurementService(database, Tenant, NullLogger<ProcurementService>.Instance, new NoopInspectionTrigger(), new NoopPostingService());
            Quotations = new QuotationService(database, Tenant, Procurement);
        }

        public static async Task<Journey> CreateAsync(CancellationToken ct)
        {
            var journey = new Journey();

            // Códigos numéricos evitam colisões de unique(tenant_id,number) entre execuções.
            var code = Random.Shared.NextInt64(100000, 999999);
            await Journey.ExecuteAsync("""
                insert into agro360.tenancy_tenants(id,name,slug,status) values(@TenantId,'Jornada Suprimentos Teste',concat('jt-',replace(@TenantId::text,'-','')),1);
                insert into agro360.platform_tenants(id,legal_name,normalized_document,customer_type,primary_segment,primary_email,legal_contact,status)
                values(@TenantId,'Jornada Suprimentos Teste',@Document::text,'EMPRESA','AGRICULTURA',concat('jt-',replace(@TenantId::text,'-',''),'@teste.local'),'Responsável Teste','ACTIVE');
                insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status)
                values(@UserId,@TenantId,'Comprador Teste',concat('jt-',replace(@TenantId::text,'-',''),'-user@teste.local'),'x','ACTIVE');
                insert into agro360.identity_user_unit_scopes(id,tenant_id,user_id,scope_type) values(gen_random_uuid(),@TenantId,@UserId,'ALL');
                insert into agro360.procurement_suppliers(id,tenant_id,legal_name,supplier_type,main_category,average_delivery_days,status,created_by,updated_by)
                values(@SupplierA,@TenantId,'Agroinsumos Alfa Ltda','FORNECEDOR','INSUMOS',5,'ACTIVE',@UserId,@UserId),
                       (@SupplierB,@TenantId,'Cooperativa Beta Insumos','FORNECEDOR','INSUMOS',7,'ACTIVE',@UserId,@UserId);
                insert into agro360.procurement_item_catalog(id,tenant_id,name,internal_code,category,unit,item_type,active,created_by,updated_by)
                values(@Cat1,@TenantId,'Semente de teste','SK'||@Code||'-1','INSUMOS','SC','MATERIAL',true,@UserId,@UserId),
                       (@Cat2,@TenantId,'Defensivo de teste','SK'||@Code||'-2','DEFENSIVOS','SC','MATERIAL',true,@UserId,@UserId);
                insert into agro360.procurement_requisitions(id,tenant_id,number,requester_id,origin,justification,priority,needed_on,status,approved_at,approved_by,created_by,updated_by)
                values(@Req,@TenantId,'RQ'||@Code,@UserId,'MANUAL','Requisição da jornada de homologação de suprimentos','MEDIUM',current_date+15,'APPROVED',now(),@UserId,@UserId,@UserId);
                insert into agro360.procurement_requisition_items(id,tenant_id,requisition_id,catalog_item_id,quantity,unit,created_by,updated_by)
                values(@ReqItem1,@TenantId,@Req,@Cat1,10,'SC',@UserId,@UserId),
                       (@ReqItem2,@TenantId,@Req,@Cat2,10,'SC',@UserId,@UserId);
                insert into agro360.procurement_quotations(id,tenant_id,number,requisition_id,status,valid_until,created_by,updated_by)
                values(@Quote,@TenantId,'COT'||@Code,@Req,'ANALYSIS',current_date+30,@UserId,@UserId);
                insert into agro360.procurement_quotation_suppliers(id,tenant_id,quotation_id,supplier_id,status,delivery_days,payment_terms,created_by,updated_by)
                values(@RowA,@TenantId,@Quote,@SupplierA,'RESPONDED',5,'28 DIAS APÓS ENTREGA',@UserId,@UserId),
                       (@RowB,@TenantId,@Quote,@SupplierB,'RESPONDED',7,'30 DIAS APÓS ENTREGA',@UserId,@UserId);
                insert into agro360.procurement_quotation_items(id,tenant_id,quotation_id,catalog_item_id,quantity,unit,requisition_item_id,created_by,updated_by)
                values(@Item1,@TenantId,@Quote,@Cat1,10,'SC',@ReqItem1,@UserId,@UserId),
                       (@Item2,@TenantId,@Quote,@Cat2,10,'SC',@ReqItem2,@UserId,@UserId);
                insert into agro360.procurement_quotation_responses(id,tenant_id,quotation_item_id,quotation_supplier_id,unit_price,total,available,created_by,updated_by)
                values(gen_random_uuid(),@TenantId,@Item1,@RowA,12,120,true,@UserId,@UserId),
                       (gen_random_uuid(),@TenantId,@Item1,@RowB,10,100,true,@UserId,@UserId),
                       (gen_random_uuid(),@TenantId,@Item2,@RowA,45,450,true,@UserId,@UserId),
                       (gen_random_uuid(),@TenantId,@Item2,@RowB,40,400,true,@UserId,@UserId);
                """, new
            {
                journey.TenantId,
                journey.UserId,
                Document = Random.Shared.NextInt64(10000000000000, 99999999999999),
                journey.SupplierA,
                journey.SupplierB,
                Cat1 = Guid.CreateVersion7(),
                Cat2 = Guid.CreateVersion7(),
                Code = code,
                Req = journey.RequisitionId,
                ReqItem1 = Guid.CreateVersion7(),
                ReqItem2 = Guid.CreateVersion7(),
                Quote = journey.QuotationId,
                journey.RowA,
                RowB = Guid.CreateVersion7(),
                journey.Item1,
                journey.Item2,
            });
            return journey;
        }

        /// <summary>Decisão comercial explícita (fora do menor preço) para o item 1 no fornecedor A —
        /// semeia dois vencedores distintos sem passar pela guarda de confirmação da conversão.</summary>
        public Task DecideItemOneForSupplierAAsync() => Journey.ExecuteAsync("""
            insert into agro360.procurement_quotation_decisions(id,tenant_id,quotation_id,quotation_item_id,quotation_supplier_id,selected_total,lowest_total,justification,decided_by,created_by,updated_by)
            values(gen_random_uuid(),@TenantId,@Quote,@Item1,@RowA,120,100,'Decisão explícita da jornada de homologação',@UserId,@UserId,@UserId)
            """, new { TenantId, Quote = QuotationId, Item1, RowA, UserId });

        public static async Task ExecuteAsync(string sql, object parameters)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters, cancellationToken: CancellationToken.None));
        }

        public static async Task<T> ScalarAsync<T>(string sql, object parameters)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            return (await connection.ExecuteScalarAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: CancellationToken.None)))!;
        }

        public static async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object parameters)
        {
            await using var connection = new NpgsqlConnection(ConnectionString);
            await connection.OpenAsync(CancellationToken.None);
            return (await connection.QueryAsync<T>(new CommandDefinition(sql, parameters, cancellationToken: CancellationToken.None))).ToList();
        }

        public static async Task<T> QuerySingleAsync<T>(string sql, object parameters) => (await QueryAsync<T>(sql, parameters)).Single()!;

        public async ValueTask DisposeAsync() => await connectionFactory.DisposeAsync();
    }

    private sealed class NoopInspectionTrigger : IOperationalInspectionTrigger
    {
        public Task<OperationalInspectionEventResult> TryStartFromOriginAsync(OperationalInspectionEventRequest request, CancellationToken ct)
            => Task.FromResult(new OperationalInspectionEventResult(Guid.Empty, request.ProcessCode, request.OriginType, request.OriginId, "NOT_REQUIRED", null, "inspeções não são exercidas nesta jornada"));

        public Task<IReadOnlyList<InspectionEventIntentListItem>> ListEventIntentsAsync(string? process, string? status, int? limit, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<InspectionEventIntentListItem>>([]);
    }

    private sealed class NoopPostingService : IPostingService
    {
        public Task PostMaterialCostAsync(Guid tenantId, Guid farmId, Guid materialId, decimal quantity, decimal unitCost, string sourceDocument, string description, DateTimeOffset competenceDate, CancellationToken ct) => Task.CompletedTask;

        public Task PostActivityCostAsync(Guid tenantId, Guid farmId, Guid orderId, string description, decimal amount, DateTimeOffset competenceDate, CancellationToken ct) => Task.CompletedTask;
    }
}
