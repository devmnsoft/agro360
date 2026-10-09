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
/// Jornada real de devolução ao fornecedor sobre PostgreSQL (exigência da homologação): recebimento
/// íntegro → devolução parcial pendente → aprovação separa funções, baixa o estoque físico pelo
/// ledger canônico (RETURN_TO_SUPPLIER), reabre o saldo do pedido (RECEIVED → PARTIALLY_RECEIVED) e
/// abre crédito OPEN ao fornecedor sem compensar títulos automaticamente. Cobre as guardas honestas:
/// idempotência por chave com verificação de conteúdo, elegível = aceito menos devoluções já
/// reivindicadas (pendentes contam, para não descontar a mesma saída duas vezes), segregação entre
/// quem registra e quem decide, e reprovação sem motivo e sem efeito.
/// </summary>
public sealed class SupplierReturnJourneyTests
{
    static SupplierReturnJourneyTests() =>
        // Reproduz o registro global que AddAgro360Infrastructure faz no aplicativo; sem ele o
        // Dapper rejeita DateOnly como parâmetro (delivery_on/first_due_on/expires_on da jornada).
        SqlMapper.AddTypeHandler(new Agro360.Infrastructure.DateOnlyTypeHandler());

    [Fact]
    public async Task AprovacaoDaDevolucaoBaixaOEstequeReabreOPedidoEAbreCreditoOpen()
    {
        await using var journey = await Journey.CreateAsync(TestContext.Current.CancellationToken);
        var received = await journey.ReceiveFullOrderAsync();

        Assert.Equal(10m, await Journey.ScalarAsync<decimal>(
            "select available from agro360.inventory_stock_balances where tenant_id=@TenantId and product_id=@Product and warehouse_id=@Warehouse",
            new { journey.TenantId, Product = journey.ProductId, Warehouse = journey.WarehouseId }));

        // O item recebido sem inspeção está integralmente elegível; nada devolvido ainda.
        var returnable = await journey.Buyer.ReturnableItemsAsync(received.ReceiptId, TestContext.Current.CancellationToken);
        var eligibleItem = (IDictionary<string, object>)returnable.Single();
        Assert.Equal(10m, (decimal)eligibleItem["accepted_quantity"]);
        Assert.Equal(0m, (decimal)eligibleItem["returned_quantity"]);

        // Registro por quem recebeu: nasce PENDING_APPROVAL com número DEV e preço unitário congelado.
        var returnId = await journey.Buyer.CreateSupplierReturnAsync(received.ReceiptId,
            new SupplierReturnCommand("Sacos avariados com produto exposto ao tempo durante a entrega",
                [new SupplierReturnLineCommand(received.ReceiptItemId, 4m)], "devolucao-journey-aprov-0001"),
            TestContext.Current.CancellationToken);
        var headerRow = await Journey.QuerySingleAsync<(string Status, string Number)>(
            "select status,number from agro360.procurement_supplier_returns where tenant_id=@TenantId and id=@Id",
            new { journey.TenantId, Id = returnId });
        Assert.Equal("PENDING_APPROVAL", headerRow.Status);
        Assert.StartsWith("DEV-", headerRow.Number);
        Assert.Equal(4m, await Journey.ScalarAsync<decimal>(
            "select quantity*unit_cost from agro360.procurement_supplier_return_items where tenant_id=@TenantId and supplier_return_id=@Id",
            new { journey.TenantId, Id = returnId }) / 50m);

        // A pendência já consome o elegível: impedir duas devoluções de reivindicar a mesma saída.
        var afterPending = (IDictionary<string, object>)(await journey.Buyer.ReturnableItemsAsync(received.ReceiptId, TestContext.Current.CancellationToken)).Single();
        Assert.Equal(4m, (decimal)afterPending["returned_quantity"]);

        // Decisão de quem registrou é recusada (segregação); a aprovação do gestor executa tudo.
        await Assert.ThrowsAnyAsync<ConflictException>(() => journey.Buyer.DecideSupplierReturnAsync(returnId,
            new SupplierReturnDecisionCommand(true, null), TestContext.Current.CancellationToken));
        await journey.Manager.DecideSupplierReturnAsync(returnId,
            new SupplierReturnDecisionCommand(true, "Conferida na doca com o transportador"), TestContext.Current.CancellationToken);

        // Ledger canônico: uma saída RETURN_TO_SUPPLIER de 4 sc ligada à linha da devolução.
        var movement = await Journey.QuerySingleAsync<(Guid Id, string Type, decimal Quantity, Guid ReferenceId)>(
            "select id Id,movement_type Type,quantity Quantity,reference_id ReferenceId from agro360.inventory_stock_movements where tenant_id=@TenantId and reference_type='RETURN_TO_SUPPLIER'",
            new { journey.TenantId });
        Assert.Equal("RETURN_TO_SUPPLIER", movement.Type);
        Assert.Equal(4m, movement.Quantity);
        // A referência do lançamento é a linha da devolução, e o item fica vinculado ao lançamento.
        Assert.Equal(await Journey.ScalarAsync<Guid>(
            "select id from agro360.procurement_supplier_return_items where tenant_id=@TenantId and supplier_return_id=@Id",
            new { journey.TenantId, Id = returnId }), movement.ReferenceId);
        Assert.Equal(6m, await Journey.ScalarAsync<decimal>(
            "select available from agro360.inventory_stock_balances where tenant_id=@TenantId and product_id=@Product and warehouse_id=@Warehouse",
            new { journey.TenantId, Product = journey.ProductId, Warehouse = journey.WarehouseId }));
        Assert.Equal(movement.Id, await Journey.ScalarAsync<Guid>(
            "select stock_movement_id from agro360.procurement_supplier_return_items where tenant_id=@TenantId and supplier_return_id=@Id",
            new { journey.TenantId, Id = returnId }));

        // Pedido espelhado: 4 de 10 voltaram a estar pendentes de recebimento.
        Assert.Equal(6m, await Journey.ScalarAsync<decimal>(
            "select received_quantity from agro360.procurement_purchase_order_items where tenant_id=@TenantId and id=@Id",
            new { journey.TenantId, Id = received.OrderItemId }));
        Assert.Equal("PARTIALLY_RECEIVED", await Journey.ScalarAsync<string>(
            "select status from agro360.procurement_purchase_orders where tenant_id=@TenantId and id=@Id",
            new { journey.TenantId, Id = received.OrderId }));

        // Crédito OPEN de 4 x R$ 50 — nenhum título é compensado automaticamente pela devolução.
        var credit = await Journey.QuerySingleAsync<(string Status, decimal Amount, string Number)>(
            "select status,amount,number from agro360.procurement_supplier_credits where tenant_id=@TenantId and supplier_return_id=@Id",
            new { journey.TenantId, Id = returnId });
        Assert.Equal("OPEN", credit.Status);
        Assert.Equal(200m, credit.Amount);
        Assert.StartsWith("CRED-", credit.Number);
        Assert.Equal(500m, await Journey.ScalarAsync<decimal>(
            "select sum(balance) from agro360.finance_payables where tenant_id=@TenantId", new { journey.TenantId }));

        Assert.Contains("SUPPLIER_RETURN_APPROVED", await Journey.QueryAsync<string>(
            "select event_type from agro360.procurement_purchase_order_events where tenant_id=@TenantId and purchase_order_id=@Order",
            new { journey.TenantId, Order = received.OrderId }));
        Assert.Equal("APPROVED", await Journey.ScalarAsync<string>(
            "select status from agro360.procurement_supplier_returns where tenant_id=@TenantId and id=@Id",
            new { journey.TenantId, Id = returnId }));

        // As leituras canônicas (lista e detalhe) executam sobre o banco real e refletem a aprovação.
        var listed = (IDictionary<string, object>)(await journey.Manager.SupplierReturnsAsync(new ProcurementQuery(), TestContext.Current.CancellationToken)).Single();
        Assert.Equal("APPROVED", listed["status"]);
        // O detalhe é um objeto anônimo interno (header + items): lido por reflexão e os registros
        // internos são linhas Dapper acessíveis como dicionário.
        var detailObject = (object)await journey.Manager.SupplierReturnAsync(returnId, TestContext.Current.CancellationToken);
        var detailHeader = (IDictionary<string, object>)detailObject.GetType().GetProperty("header")!.GetValue(detailObject)!;
        Assert.Equal("APPROVED", detailHeader["status"]);
        Assert.Equal(200m, (decimal)detailHeader["credit_amount"]);
        var detailItems = (System.Collections.IEnumerable)detailObject.GetType().GetProperty("items")!.GetValue(detailObject)!;
        Assert.Single(detailItems.Cast<object>());
    }

    [Fact]
    public async Task IdempotenciaSaldoElegivelEReprovacaoNaoTocamEstoqueNemPedido()
    {
        await using var journey = await Journey.CreateAsync(TestContext.Current.CancellationToken);
        var received = await journey.ReceiveFullOrderAsync();

        var returnId = await journey.Buyer.CreateSupplierReturnAsync(received.ReceiptId,
            new SupplierReturnCommand("Umidade acima do contrato detectada na conferência interna",
                [new SupplierReturnLineCommand(received.ReceiptItemId, 4m)], "devolucao-journey-guardas-01"),
            TestContext.Current.CancellationToken);

        // Replay com a MESMA chave e conteúdo devolve a mesma devolução sem duplicar.
        var replay = await journey.Buyer.CreateSupplierReturnAsync(received.ReceiptId,
            new SupplierReturnCommand("Umidade acima do contrato detectada na conferência interna",
                [new SupplierReturnLineCommand(received.ReceiptItemId, 4m)], "devolucao-journey-guardas-01"),
            TestContext.Current.CancellationToken);
        Assert.Equal(returnId, replay);
        Assert.Equal(1, await Journey.ScalarAsync<int>(
            "select count(*)::int from agro360.procurement_supplier_returns where tenant_id=@TenantId", new { journey.TenantId }));

        // Mesma chave com outro conteúdo é conflito explícito, nunca segunda devolução.
        await Assert.ThrowsAnyAsync<ConflictException>(() => journey.Buyer.CreateSupplierReturnAsync(received.ReceiptId,
            new SupplierReturnCommand("Umidade acima do contrato detectada na conferência interna",
                [new SupplierReturnLineCommand(received.ReceiptItemId, 3m)], "devolucao-journey-guardas-01"),
            TestContext.Current.CancellationToken));

        // A pendência consome o elegível: 4 pendentes + 7 novas excederiam as 10 aceitas.
        await Assert.ThrowsAnyAsync<ConflictException>(() => journey.Buyer.CreateSupplierReturnAsync(received.ReceiptId,
            new SupplierReturnCommand("Segunda tentativa acima do saldo aceito restante",
                [new SupplierReturnLineCommand(received.ReceiptItemId, 7m)], "devolucao-journey-guardas-02"),
            TestContext.Current.CancellationToken));

        // Reprovação exige motivo; o próprio criador segue impedido de decidir.
        await Assert.ThrowsAnyAsync<DomainException>(() => journey.Manager.DecideSupplierReturnAsync(returnId,
            new SupplierReturnDecisionCommand(false, null), TestContext.Current.CancellationToken));
        await Assert.ThrowsAnyAsync<ConflictException>(() => journey.Buyer.DecideSupplierReturnAsync(returnId,
            new SupplierReturnDecisionCommand(false, "Motivo válido, mas decidida por quem registrou"), TestContext.Current.CancellationToken));

        await journey.Manager.DecideSupplierReturnAsync(returnId,
            new SupplierReturnDecisionCommand(false, "Produto recondicionado permanece em estoque para novo uso"),
            TestContext.Current.CancellationToken);
        Assert.Equal("REJECTED", await Journey.ScalarAsync<string>(
            "select status from agro360.procurement_supplier_returns where tenant_id=@TenantId and id=@Id",
            new { journey.TenantId, Id = returnId }));

        // Reprovada: nenhum efeito colateral — estoque, pedido e financeiro permanecem intocados.
        Assert.Equal(10m, await Journey.ScalarAsync<decimal>(
            "select available from agro360.inventory_stock_balances where tenant_id=@TenantId and product_id=@Product and warehouse_id=@Warehouse",
            new { journey.TenantId, Product = journey.ProductId, Warehouse = journey.WarehouseId }));
        Assert.Equal(0, await Journey.ScalarAsync<int>(
            "select count(*)::int from agro360.inventory_stock_movements where tenant_id=@TenantId and reference_type='RETURN_TO_SUPPLIER'", new { journey.TenantId }));
        Assert.Equal(10m, await Journey.ScalarAsync<decimal>(
            "select received_quantity from agro360.procurement_purchase_order_items where tenant_id=@TenantId and id=@Id",
            new { journey.TenantId, Id = received.OrderItemId }));
        Assert.Equal("RECEIVED", await Journey.ScalarAsync<string>(
            "select status from agro360.procurement_purchase_orders where tenant_id=@TenantId and id=@Id",
            new { journey.TenantId, Id = received.OrderId }));
        Assert.Equal(0, await Journey.ScalarAsync<int>(
            "select count(*)::int from agro360.procurement_supplier_credits where tenant_id=@TenantId", new { journey.TenantId }));

        // Decisão dupla é recusa honesta de estado, e a elegibilidade volta integral.
        await Assert.ThrowsAnyAsync<ConflictException>(() => journey.Manager.DecideSupplierReturnAsync(returnId,
            new SupplierReturnDecisionCommand(true, null), TestContext.Current.CancellationToken));
        var afterReject = (IDictionary<string, object>)(await journey.Buyer.ReturnableItemsAsync(received.ReceiptId, TestContext.Current.CancellationToken)).Single();
        Assert.Equal(0m, (decimal)afterReject["returned_quantity"]);
    }

    private readonly record struct ReceivedOrder(Guid OrderId, Guid OrderItemId, Guid ReceiptId, Guid ReceiptItemId);

    /// <summary>Tenant isolado com comprador (quem recebe/registra) e gestor (quem decide), produto
    /// vinculado ao item de catálogo na mesma unidade base (SC) para o recebimento lançar estoque
    /// diretamente sem inspeção. Os dois serviços reais rodam sobre o executor canônico.</summary>
    private sealed class Journey : IAsyncDisposable
    {
        private readonly NpgsqlConnectionFactory connectionFactory;

        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid BuyerId { get; } = Guid.CreateVersion7();
        public Guid ManagerId { get; } = Guid.CreateVersion7();
        public Guid OrganizationId { get; } = Guid.CreateVersion7();
        public Guid FarmId { get; } = Guid.CreateVersion7();
        public Guid WarehouseId { get; } = Guid.CreateVersion7();
        public Guid ProductId { get; } = Guid.CreateVersion7();
        public Guid CatalogItemId { get; } = Guid.CreateVersion7();
        public Guid SupplierId { get; } = Guid.CreateVersion7();
        public Guid FinanceAccountId { get; } = Guid.CreateVersion7();
        public ProcurementService Buyer { get; }
        public ProcurementService Manager { get; }

        public static string ConnectionString
        {
            get
            {
                var connectionString = Environment.GetEnvironmentVariable("AGRO360_TEST_CONNECTION_STRING");
                if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
                    Assert.False(string.IsNullOrWhiteSpace(connectionString),
                        "AGRO360_TEST_CONNECTION_STRING é obrigatória no gate de integração do CI.");
                Assert.SkipWhen(string.IsNullOrWhiteSpace(connectionString),
                    "Defina AGRO360_TEST_CONNECTION_STRING para executar a jornada de devolução ao fornecedor sobre PostgreSQL real.");
                return connectionString!;
            }
        }

        private Journey()
        {
            connectionFactory = new NpgsqlConnectionFactory(
                new PostgreSqlConnectionConfiguration(ConnectionString, "IntegrationTests", "integration-test", "Development", "password", "integration-test", false),
                NullLogger<NpgsqlConnectionFactory>.Instance);
            Buyer = BuildService(BuyerId);
            Manager = BuildService(ManagerId);
        }

        private ProcurementService BuildService(Guid userId)
        {
            var tenant = new TenantContext();
            tenant.SetScope(new TenantScope(TenantId, userId, null, null));
            var database = new DatabaseExecutor(connectionFactory, tenant, NullLogger<DatabaseExecutor>.Instance);
            return new ProcurementService(database, tenant, NullLogger<ProcurementService>.Instance, new NoopInspectionTrigger(), new NoopPostingService());
        }

        public static async Task<Journey> CreateAsync(CancellationToken ct)
        {
            var journey = new Journey();
            var code = Random.Shared.NextInt64(100000, 999999);
            await Journey.ExecuteAsync("""
                insert into agro360.tenancy_tenants(id,name,slug,status) values(@TenantId,'Devolução Fornecedor Teste',concat('dv-',replace(@TenantId::text,'-','')),1);
                insert into agro360.platform_tenants(id,legal_name,normalized_document,customer_type,primary_segment,primary_email,legal_contact,status)
                values(@TenantId,'Devolução Fornecedor Teste',@Document::text,'EMPRESA','AGRICULTURA',concat('dv-',replace(@TenantId::text,'-',''),'@teste.local'),'Responsável Teste','ACTIVE');
                insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status)
                values(@BuyerId,@TenantId,'Comprador Teste',concat('dv-',replace(@TenantId::text,'-',''),'-buyer@teste.local'),'x','ACTIVE'),
                       (@ManagerId,@TenantId,'Gestor Teste',concat('dv-',replace(@TenantId::text,'-',''),'-mgr@teste.local'),'x','ACTIVE');
                insert into agro360.identity_user_unit_scopes(id,tenant_id,user_id,scope_type)
                values(gen_random_uuid(),@TenantId,@BuyerId,'ALL'),(gen_random_uuid(),@TenantId,@ManagerId,'ALL');
                insert into agro360.organization_organizations(id,tenant_id,type,name,created_by,updated_by)
                values(@OrganizationId,@TenantId,'COMPANY','Agro Devolução Org',@BuyerId,@BuyerId);
                insert into agro360.geo_farms(id,tenant_id,organization_id,name,state,total_area_ha,created_by,updated_by)
                values(@FarmId,@TenantId,@OrganizationId,'Fazenda Devolução','SP',100,@BuyerId,@BuyerId);
                insert into agro360.inventory_products(id,tenant_id,sku,name,category,base_unit,created_by,updated_by)
                values(@ProductId,@TenantId,'SKU-DV'||@Code,'Ração Devolução Teste','INSUMOS','sc',@BuyerId,@BuyerId);
                insert into agro360.inventory_warehouses(id,tenant_id,farm_id,code,name,type,created_by,updated_by)
                values(@WarehouseId,@TenantId,@FarmId,'DEP-DV'||@Code,'Depósito Devolução','FEED',@BuyerId,@BuyerId);
                insert into agro360.procurement_item_catalog(id,tenant_id,name,internal_code,category,unit,item_type,active,related_product_id,created_by,updated_by)
                values(@CatalogItemId,@TenantId,'Ração Devolução SC','CAT-DV'||@Code,'NUTRICAO','sc','MATERIAL',true,@ProductId,@BuyerId,@BuyerId);
                insert into agro360.finance_chart_of_accounts(id,tenant_id,code,name,type,nature,active,display_order,created_by)
                values(@FinanceAccountId,@TenantId,'PC-DV'||@Code,'Fornecedores Nacionais','EXPENSE','DEBIT',true,1,@BuyerId);
                insert into agro360.procurement_suppliers(id,tenant_id,legal_name,supplier_type,main_category,average_delivery_days,status,created_by,updated_by)
                values(@SupplierId,@TenantId,'Nutrição Delta Alimentos Ltda','FORNECEDOR','INSUMOS',5,'ACTIVE',@BuyerId,@BuyerId);
                """, new
            {
                journey.TenantId,
                journey.BuyerId,
                journey.ManagerId,
                journey.OrganizationId,
                journey.FarmId,
                journey.WarehouseId,
                journey.ProductId,
                journey.CatalogItemId,
                journey.SupplierId,
                journey.FinanceAccountId,
                Document = Random.Shared.NextInt64(10000000000000, 99999999999999),
                Code = code
            });
            return journey;
        }

        /// <summary>Pedido aprovado e recebido integralmente (10 SC × R$ 50) pelo comprador: estoque
        /// lançado, pedido RECEIVED e previsão financeira criada — a linha de partida da devolução.</summary>
        public async Task<ReceivedOrder> ReceiveFullOrderAsync()
        {
            var ct = TestContext.Current.CancellationToken;
            var orderId = await Buyer.CreateOrderAsync(new PurchaseOrderCommand(SupplierId, null, null, null, null,
                "30 DIAS", DateOnly.FromDateTime(DateTime.UtcNow).AddDays(3), "Galpão Central", 0m, 0m,
                [new PurchaseOrderLineCommand(CatalogItemId, 10m, "sc", 50m, 0m)]), ct);
            await Manager.ApproveOrderAsync(orderId, "Aprovado na jornada de devolução", ct);
            var orderItemId = await Journey.ScalarAsync<Guid>(
                "select id from agro360.procurement_purchase_order_items where tenant_id=@TenantId and purchase_order_id=@Order",
                new { TenantId, Order = orderId });
            await Buyer.ReceiveAsync(new ProcurementReceiptCommand(orderId, DateTimeOffset.UtcNow, $"NF-DV{Random.Shared.NextInt64(100000, 999999)}",
                false, null, [new ReceiptLineCommand(orderItemId, 10m, null, null, null)], "recebimento-journey-dev-001",
                WarehouseId, FinanceAccountId, DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30), 1), ct);
            var receiptId = await Journey.ScalarAsync<Guid>(
                "select id from agro360.procurement_receipts where tenant_id=@TenantId and purchase_order_id=@Order",
                new { TenantId, Order = orderId });
            var receiptItemId = await Journey.ScalarAsync<Guid>(
                "select id from agro360.procurement_receipt_items where tenant_id=@TenantId and receipt_id=@Receipt",
                new { TenantId, Receipt = receiptId });
            return new ReceivedOrder(orderId, orderItemId, receiptId, receiptItemId);
        }

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
