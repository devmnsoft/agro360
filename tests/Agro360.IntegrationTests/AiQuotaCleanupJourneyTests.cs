using Agro360.Infrastructure.Persistence;
using Agro360.Infrastructure.Services.Ai;
using Agro360.Multitenancy;
using Dapper;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;

namespace Agro360.IntegrationTests;

/// <summary>
/// Varredura de reservas de IA abandonadas sobre PostgreSQL real: a baixa do saldo reservado usa
/// exatamente o quota_id que originou a reserva (nunca suposição de período), reservas jovens são
/// execuções em voo (intocáveis), o legado sem vínculo compensa com clamp honesto sem negativo, e a
 /// varredura de um tenant jamais toca o saldo de outro (isolamento RLS + filtro explícito).
/// </summary>
public sealed class AiQuotaCleanupJourneyTests
{
    [Fact]
    public async Task LiberaReservasAbandonadasPreservandoAsEmVooECompensandoLegadoSemVinculo()
    {
        await using var fixture = await CleanupFixture.CreateAsync(TestContext.Current.CancellationToken);

        // 1000 vinculada antiga + 1500 legada sem vínculo (antiga) devem ser liberadas; 500 jovem fica intacta.
        var released = await fixture.ExecutionService
            .CleanupAbandonedReservationsAsync(fixture.TenantId, TimeSpan.FromMinutes(30), TestContext.Current.CancellationToken);

        Assert.Equal(2, released);

        var quotaReserved = await fixture.ExecuteScalarAsync<long>(
            "select reserved_tokens from agro360.tenant_ai_quotas where id=@Id", new { fixture.QuotaId });
        Assert.Equal(500, quotaReserved); // só a reserva jovem permanece baixando o saldo

        var staleStatus = await fixture.ExecuteScalarAsync<string>(
            "select status from agro360.ai_executions where id=@Id", new { Id = fixture.StaleBoundExecution });
        Assert.Equal("RELEASED", staleStatus);

        var freshStatus = await fixture.ExecuteScalarAsync<string>(
            "select status from agro360.ai_executions where id=@Id", new { Id = fixture.FreshExecution });
        Assert.Equal("RESERVED", freshStatus);

        var legacyStatus = await fixture.ExecuteScalarAsync<string>(
            "select status from agro360.ai_executions where id=@Id", new { Id = fixture.StaleLegacyExecution });
        Assert.Equal("RELEASED", legacyStatus);
    }

    [Fact]
    public async Task VarreduraDeUmTenantNaoTocaReservasDeOutroTenant()
    {
        await using var fixture = await CleanupFixture.CreateAsync(TestContext.Current.CancellationToken);

        var released = await fixture.ExecutionService
            .CleanupAbandonedReservationsAsync(fixture.TenantId, TimeSpan.FromMinutes(30), TestContext.Current.CancellationToken);

        Assert.Equal(2, released);

        var otherReserved = await fixture.ExecuteScalarAsync<long>(
            "select reserved_tokens from agro360.tenant_ai_quotas where id=@Id", new { fixture.OtherTenantQuotaId });
        Assert.Equal(900, otherReserved); // intocável pela varredura do tenant alheio

        var otherStatus = await fixture.ExecuteScalarAsync<string>(
            "select status from agro360.ai_executions where id=@Id", new { Id = fixture.OtherTenantStaleExecution });
        Assert.Equal("RESERVED", otherStatus);
    }

    private sealed class CleanupFixture : IAsyncDisposable
    {
        private static string? ConnectionString => Environment.GetEnvironmentVariable("AGRO360_TEST_CONNECTION_STRING");

        private NpgsqlConnection connection = null!;

        public Guid TenantId { get; } = Guid.CreateVersion7();
        public Guid UserId { get; } = Guid.CreateVersion7();
        public Guid QuotaId { get; } = Guid.CreateVersion7();
        public Guid StaleBoundExecution { get; } = Guid.CreateVersion7();
        public Guid FreshExecution { get; } = Guid.CreateVersion7();
        public Guid StaleLegacyExecution { get; } = Guid.CreateVersion7();
        public Guid OtherTenantId { get; } = Guid.CreateVersion7();
        public Guid OtherTenantUserId { get; } = Guid.CreateVersion7();
        public Guid OtherTenantQuotaId { get; } = Guid.CreateVersion7();
        public Guid OtherTenantStaleExecution { get; } = Guid.CreateVersion7();
        public AiExecutionService ExecutionService { get; private set; } = null!;

        public static async Task<CleanupFixture> CreateAsync(CancellationToken ct)
        {
            var connectionString = ConnectionString;
            if (string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase))
                Assert.False(string.IsNullOrWhiteSpace(connectionString),
                    "AGRO360_TEST_CONNECTION_STRING é obrigatória no gate de integração do CI.");
            Assert.SkipWhen(string.IsNullOrWhiteSpace(connectionString),
                "Defina AGRO360_TEST_CONNECTION_STRING para executar a varredura de reservas de IA sobre PostgreSQL real.");

            var fixture = new CleanupFixture();
            fixture.connection = new NpgsqlConnection(connectionString!);
            await fixture.connection.OpenAsync(ct);

            await fixture.ExecuteAsync("""
                insert into agro360.tenancy_tenants(id,name,slug,status) values(@TenantId,'Varredura IA Teste',concat('ai-',replace(@TenantId::text,'-','')),1);
                insert into agro360.platform_tenants(id,legal_name,normalized_document,customer_type,primary_segment,primary_email,legal_contact,status)
                values(@TenantId,'Varredura IA Teste',@Document::text,'EMPRESA','AGRICULTURA',concat('ai-',replace(@TenantId::text,'-',''),'@teste.local'),'Responsavel Teste','ACTIVE');
                insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status)
                values(@UserId,@TenantId,'Operador IA',concat('ai-',replace(@TenantId::text,'-',''),'-u@teste.local'),'x','ACTIVE');

                insert into agro360.tenant_ai_quotas(id,tenant_id,use_case,period_start,period_end,max_tokens,reserved_tokens,consumed_tokens,active,created_by,updated_by)
                values(@QuotaId,@TenantId,'stock_assistant',current_date-5,current_date+25,100000,3000,0,true,@UserId,@UserId);
                -- Reservadas há 2 horas: abandonment inequívoco frente a olderThan=30min.
                insert into agro360.ai_executions(id,tenant_id,user_id,use_case,attempt_number,status,reserved_tokens,token_confidence,duration_ms,prompt_tokens,completion_tokens,total_tokens,quota_id,occurred_at,created_at,updated_at)
                values(@StaleBound,@TenantId,@UserId,'stock_assistant',1,'RESERVED',1000,'ESTIMATED',0,0,0,0,@QuotaId,now()-interval '2 hours',now()-interval '2 hours',now()-interval '2 hours');
                -- Legado anterior ao vínculo quota_id: compensação com clamp honesto na cota vigente.
                insert into agro360.ai_executions(id,tenant_id,user_id,use_case,attempt_number,status,reserved_tokens,token_confidence,duration_ms,prompt_tokens,completion_tokens,total_tokens,quota_id,occurred_at,created_at,updated_at)
                values(@StaleLegacy,@TenantId,@UserId,'stock_assistant',1,'RESERVED',1500,'ESTIMATED',0,0,0,0,null,now()-interval '2 hours',now()-interval '2 hours',now()-interval '2 hours');
                -- Reserva jovem: execução em voo, nunca é considerada abandono.
                insert into agro360.ai_executions(id,tenant_id,user_id,use_case,attempt_number,status,reserved_tokens,token_confidence,duration_ms,prompt_tokens,completion_tokens,total_tokens,quota_id,occurred_at,created_at,updated_at)
                values(@Fresh,@TenantId,@UserId,'stock_assistant',1,'RESERVED',500,'ESTIMATED',0,0,0,0,@QuotaId,now(),now(),now());

                -- Tenant vizinho com reserva antiga própria: deve permanecer intocável.
                insert into agro360.tenancy_tenants(id,name,slug,status) values(@OtherTenantId,'Varredura IA Vizinho',concat('aiv-',replace(@OtherTenantId::text,'-','')),1);
                insert into agro360.platform_tenants(id,legal_name,normalized_document,customer_type,primary_segment,primary_email,legal_contact,status)
                values(@OtherTenantId,'Varredura IA Vizinho',@OtherDocument::text,'EMPRESA','AGRICULTURA',concat('aiv-',replace(@OtherTenantId::text,'-',''),'@teste.local'),'Responsavel Teste','ACTIVE');
                insert into agro360.identity_users(id,tenant_id,name,email,password_hash,status)
                values(@OtherTenantUserId,@OtherTenantId,'Operador Vizinho',concat('aiv-',replace(@OtherTenantId::text,'-',''),'-u@teste.local'),'x','ACTIVE');
                insert into agro360.tenant_ai_quotas(id,tenant_id,use_case,period_start,period_end,max_tokens,reserved_tokens,consumed_tokens,active,created_by,updated_by)
                values(@OtherTenantQuotaId,@OtherTenantId,'stock_assistant',current_date-5,current_date+25,100000,900,0,true,@OtherTenantUserId,@OtherTenantUserId);
                insert into agro360.ai_executions(id,tenant_id,user_id,use_case,attempt_number,status,reserved_tokens,token_confidence,duration_ms,prompt_tokens,completion_tokens,total_tokens,quota_id,occurred_at,created_at,updated_at)
                values(@OtherTenantStale,@OtherTenantId,@OtherTenantUserId,'stock_assistant',1,'RESERVED',900,'ESTIMATED',0,0,0,0,@OtherTenantQuotaId,now()-interval '2 hours',now()-interval '2 hours',now()-interval '2 hours');
                """, new
            {
                fixture.TenantId,
                fixture.UserId,
                fixture.QuotaId,
                StaleBound = fixture.StaleBoundExecution,
                StaleLegacy = fixture.StaleLegacyExecution,
                Fresh = fixture.FreshExecution,
                OtherTenantId = fixture.OtherTenantId,
                OtherTenantUserId = fixture.OtherTenantUserId,
                OtherTenantQuotaId = fixture.OtherTenantQuotaId,
                OtherTenantStale = fixture.OtherTenantStaleExecution,
                Document = Random.Shared.NextInt64(10000000000000, 99999999999999),
                OtherDocument = Random.Shared.NextInt64(10000000000000, 99999999999999)
            });

            var connectionFactory = new NpgsqlConnectionFactory(
                new PostgreSqlConnectionConfiguration(connectionString!, "IntegrationTests", "integration-test", "Development", "password", "integration-test", false),
                NullLogger<NpgsqlConnectionFactory>.Instance);
            var tenant = new TenantContext();
            // Contexto de requisição vazio de propósito: a varredura recebe o tenant por parâmetro
            // explícito (contrato do worker), exatamente como rodará no host semHttpContext.
            tenant.SetScope(new TenantScope(Guid.Empty, Guid.Empty, null, null));
            fixture.ExecutionService = new AiExecutionService(
                new DatabaseExecutor(connectionFactory, tenant, NullLogger<DatabaseExecutor>.Instance));

            return fixture;
        }

        public async Task<T> ExecuteScalarAsync<T>(string sql, object parameters) =>
            (await connection.ExecuteScalarAsync<T>(sql, parameters))!;

        private async Task ExecuteAsync(string sql, object parameters) =>
            await connection.ExecuteAsync(new CommandDefinition(sql, parameters,
                cancellationToken: TestContext.Current.CancellationToken));

        public async ValueTask DisposeAsync() => await connection.DisposeAsync();
    }
}
