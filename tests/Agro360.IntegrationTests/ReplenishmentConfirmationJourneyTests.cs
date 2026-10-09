using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Infrastructure.Services;
using Agro360.Multitenancy;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Agro360.IntegrationTests;

/// <summary>
/// Jornada real de confirmação de necessidade de reposição sobre PostgreSQL: a confirmação não cria
/// mais requisição "aberta" à margem da esteira — ela atravessa o núcleo canônico de requisições e a
/// necessidade só sai FORWARDED com uma requisição AWAITING_APPROVAL auditada (evento SUBMITTED),
/// mantendo revisão humana e separação de funções antes de cotar. Idempotência por chave (re-tentativa
/// devolve a mesma requisição sem duplicar) e recusa honesta quando a necessidade mudou de estado.
/// </summary>
public sealed class ReplenishmentConfirmationJourneyTests
{
    static ReplenishmentConfirmationJourneyTests() =>
        // Reproduz o registro global que AddAgro360Infrastructure faz no aplicativo; sem ele o
        // Dapper rejeita DateOnly como parâmetro (needed_on no analisador e na requisição).
        SqlMapper.AddTypeHandler(new Agro360.Infrastructure.DateOnlyTypeHandler());

    [Fact]
    public async Task ConfirmacaoEncaminhaPelaEsteiraAprovavelAuditaESoIdempotentePorChave()
    {
        await using var fixture = await ReplenishmentFixture.CreateAsync(TestContext.Current.CancellationToken);

        var neededOn = DateOnly.FromDateTime(DateTime.UtcNow);
        var needId = await fixture.Replenishment.AnalyzeAsync(
            new AnalyzeMaterialNeedCommand(fixture.PolicyId, neededOn, neededOn.AddDays(14)),
            TestContext.Current.CancellationToken);

        var requisitionId = await fixture.Replenishment.ConfirmPurchaseAsync(needId,
            new ConfirmMaterialNeedCommand(1, null, null, "confirm-reposicao-01"),
            TestContext.Current.CancellationToken);

        // A requisição nasce no fluxo canônico aprovável — nunca em status paralelo fora da fila.
        var requisitionRow = (await fixture.QueryAsync(
            "select status||'|'||origin from agro360.procurement_requisitions where tenant_id=@TenantId and id=@Id",
            new { fixture.TenantId, Id = requisitionId }))[0].Split('|');
        Assert.Equal("AWAITING_APPROVAL", requisitionRow[0]);
        Assert.Equal("REPLENISHMENT", requisitionRow[1]);

        // Auditoria do passo: SUBMITTED é o mesmo evento que a criação manual via API produz.
        var events = await fixture.QueryAsync(
            "select event_type from agro360.procurement_requisition_events where tenant_id=@TenantId and requisition_id=@Id order by created_at",
            new { fixture.TenantId, Id = requisitionId });
        Assert.Equal(["SUBMITTED"], events);

        // A necessidade só sai do Identification quando a requisição aprovável existiu de fato.
        var needRow = (await fixture.QueryAsync(
            "select status||'|'||coalesce(requisition_id::text,'')||'|'||version from agro360.inventory_material_needs where tenant_id=@TenantId and id=@Id",
            new { fixture.TenantId, Id = needId }))[0].Split('|');
        Assert.Equal("FORWARDED", needRow[0]);
        Assert.Equal(requisitionId, Guid.Parse(needRow[1]));
        Assert.Equal("2", needRow[2]);

        // Re-tentativa com a MESMA chave devolve a mesma requisição sem duplicar nada.
        var replay = await fixture.Replenishment.ConfirmPurchaseAsync(needId,
            new ConfirmMaterialNeedCommand(2, null, null, "confirm-reposicao-01"),
            TestContext.Current.CancellationToken);
        Assert.Equal(requisitionId, replay);
        Assert.Equal(1, await fixture.ExecuteScalarAsync<int>(
            "select count(*)::int from agro360.procurement_requisitions where tenant_id=@TenantId and origin='REPLENISHMENT'",
            new { fixture.TenantId }));

        // Chave diferente para necessidade já encaminhada é conflito explícito, nunca segunda requisição.
        await Assert.ThrowsAsync<Agro360.SharedKernel.ConflictException>(() => fixture.Replenishment.ConfirmPurchaseAsync(needId,
            new ConfirmMaterialNeedCommand(2, null, null, "confirm-reposicao-02"),
            TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task AjusteSemJustificativaEEstadoForaIdentificacaoRecusamEncaminhamento()
    {
        await using var fixture = await ReplenishmentFixture.CreateAsync(TestContext.Current.CancellationToken);

        var neededOn = DateOnly.FromDateTime(DateTime.UtcNow);
        var needId = await fixture.Replenishment.AnalyzeAsync(
            new AnalyzeMaterialNeedCommand(fixture.PolicyId, neededOn, neededOn.AddDays(14)),
            TestContext.Current.CancellationToken);

        // Quantidade divergente da sugerida sem justificativa é recusada antes de qualquer gravação.
        await Assert.ThrowsAsync<Agro360.SharedKernel.DomainException>(() => fixture.Replenishment.ConfirmPurchaseAsync(needId,
            new ConfirmMaterialNeedCommand(1, 999m, null, "confirm-reposicao-ajuste"),
            TestContext.Current.CancellationToken));
        Assert.Equal(0, await fixture.ExecuteScalarAsync<int>(
            "select count(*)::int from agro360.procurement_requisitions where tenant_id=@TenantId",
            new { fixture.TenantId }));

        // Necessidade dispensada não pode ser encaminhada (guarda de estado honesta).
        await fixture.Replenishment.DismissAsync(needId, 1, "Coberto por transferência interna", TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<Agro360.SharedKernel.ConflictException>(() => fixture.Replenishment.ConfirmPurchaseAsync(needId,
            new ConfirmMaterialNeedCommand(2, null, null, "confirm-reposicao-descartada"),
            TestContext.Current.CancellationToken));
    }

    private sealed class ReplenishmentFixture : IAsyncDisposable
    {
        private NpgsqlConnection connection = null!;

        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid UserId { get; } = Guid.CreateVersion7();
        public Guid OrganizationId { get; } = Guid.CreateVersion7();
        public Guid FarmId { get; } = Guid.CreateVersion7();
        public Guid WarehouseId { get; } = Guid.CreateVersion7();
        public Guid ProductId { get; } = Guid.CreateVersion7();
        public Guid CatalogItemId { get; } = Guid.CreateVersion7();
        public Guid PolicyId { get; } = Guid.CreateVersion7();
        public ReplenishmentService Replenishment { get; private set; } = null!;

        public static async Task<ReplenishmentFixture> CreateAsync(CancellationToken ct)
        {
            var connectionString = Environment.GetEnvironmentVariable("AGRO360_TEST_CONNECTION_STRING");
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
                Assert.False(string.IsNullOrWhiteSpace(connectionString),
                    "AGRO360_TEST_CONNECTION_STRING é obrigatória no gate de integração do CI.");
            Assert.SkipWhen(string.IsNullOrWhiteSpace(connectionString),
                "Defina AGRO360_TEST_CONNECTION_STRING para executar a jornada de reposição sobre PostgreSQL real.");

            var fixture = new ReplenishmentFixture();
            fixture.connection = new NpgsqlConnection(connectionString!);
            await fixture.connection.OpenAsync(ct);

            await fixture.ExecuteAsync("""
                insert into agro360.tenancy_tenants(id,name,slug,status) values(@TenantId,'Reposição Canônica Teste',concat('rc-',replace(@TenantId::text,'-','')),1);
                insert into agro360.platform_tenants(id,legal_name,normalized_document,customer_type,primary_segment,primary_email,legal_contact,status)
                values(@TenantId,'Reposição Canônica Teste',@Document::text,'EMPRESA','AGRICULTURA',concat('rc-',replace(@TenantId::text,'-',''),'@teste.local'),'Responsavel Teste','ACTIVE');
                insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status)
                values(@UserId,@TenantId,'Almoxarife Teste',concat('rc-',replace(@TenantId::text,'-',''),'-u@teste.local'),'x','ACTIVE');
                insert into agro360.identity_user_unit_scopes(id,tenant_id,user_id,scope_type) values(gen_random_uuid(),@TenantId,@UserId,'ALL');
                insert into agro360.organization_organizations(id,tenant_id,type,name,created_by,updated_by)
                values(@OrganizationId,@TenantId,'COMPANY','Agro Teste Org',@UserId,@UserId);
                insert into agro360.geo_farms(id,tenant_id,organization_id,name,state,total_area_ha,created_by,updated_by)
                values(@FarmId,@TenantId,@OrganizationId,'Fazenda Teste','SP',100,@UserId,@UserId);
                insert into agro360.inventory_products(id,tenant_id,sku,name,category,base_unit,created_by,updated_by)
                values(@ProductId,@TenantId,'SKU-RC-1','Ração Teste','INSUMOS','kg',@UserId,@UserId);
                insert into agro360.inventory_warehouses(id,tenant_id,farm_id,code,name,type,created_by,updated_by)
                values(@WarehouseId,@TenantId,@FarmId,'DEP-RC','Depósito Teste','FEED',@UserId,@UserId);
                -- Item comprável na unidade de compra (SC) com conversão explícita pela política (30 KG/saco).
                insert into agro360.procurement_item_catalog(id,tenant_id,name,internal_code,category,unit,item_type,active,related_product_id,created_by,updated_by)
                values(@CatalogItemId,@TenantId,'Ração Teste SC','CAT-RC-1','NUTRICAO','sc','MATERIAL',true,@ProductId,@UserId,@UserId);
                insert into agro360.inventory_replenishment_policies(id,tenant_id,product_id,warehouse_id,minimum_stock,target_stock,replenishment_days,minimum_purchase,purchase_multiple,purchase_unit,stock_per_purchase_unit,responsible_id,status,created_by,updated_by)
                values(@PolicyId,@TenantId,@ProductId,@WarehouseId,60,300,7,null,null,'sc',30,@UserId,'ACTIVE',@UserId,@UserId);
                """, new
            {
                fixture.TenantId,
                fixture.UserId,
                fixture.OrganizationId,
                fixture.FarmId,
                fixture.WarehouseId,
                fixture.ProductId,
                fixture.CatalogItemId,
                fixture.PolicyId,
                Document = Random.Shared.NextInt64(10000000000000, 99999999999999)
            });

            var connectionFactory = new NpgsqlConnectionFactory(
                new PostgreSqlConnectionConfiguration(connectionString!, "IntegrationTests", "integration-test", "Development", "password", "integration-test", false),
                NullLogger<NpgsqlConnectionFactory>.Instance);
            var tenant = new TenantContext();
            tenant.SetScope(new TenantScope(fixture.TenantId, fixture.UserId, null, null));
            var database = new DatabaseExecutor(connectionFactory, tenant, NullLogger<DatabaseExecutor>.Instance);
            var procurement = new ProcurementService(database, tenant, NullLogger<ProcurementService>.Instance,
                new NoopInspectionTrigger(), new NoopPostingService());
            fixture.Replenishment = new ReplenishmentService(database, tenant, procurement);
            return fixture;
        }

        public async Task<T> ExecuteScalarAsync<T>(string sql, object parameters) =>
            (await connection.ExecuteScalarAsync<T>(new CommandDefinition(sql, parameters,
                cancellationToken: TestContext.Current.CancellationToken)))!;

        public async Task<IReadOnlyList<string>> QueryAsync(string sql, object parameters) =>
            (await connection.QueryAsync<string>(new CommandDefinition(sql, parameters,
                cancellationToken: TestContext.Current.CancellationToken))).AsList();

        private async Task ExecuteAsync(string sql, object parameters) =>
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters,
                cancellationToken: TestContext.Current.CancellationToken));

        public async ValueTask DisposeAsync() => await connection.DisposeAsync();
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
