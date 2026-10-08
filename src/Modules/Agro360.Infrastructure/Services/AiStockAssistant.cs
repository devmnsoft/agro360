using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Agro360.Application;
using Agro360.Application.Abstractions;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Infrastructure.Security;
using Agro360.Infrastructure.Services.Ai;
using Agro360.Multitenancy;
using Agro360.SharedKernel;
using Dapper;
using Microsoft.Extensions.Logging;

namespace Agro360.Infrastructure.Services;

public interface IAiStockAssistant
{
    Task<AiStockAssistantResponse> AskAsync(string question, CancellationToken ct = default);
}

public sealed record AiStockAssistantResponse(
    string Answer,
    IReadOnlyList<StockBalanceDto>? Data,
    bool SuggestsReplenishment,
    ReplenishmentDraft? Draft,
    string SourceProvider,
    string Model,
    int TotalTokens,
    TimeSpan Duration,
    long TotalRecords);

public sealed record ReplenishmentDraft(
    Guid ProductId,
    Guid? CatalogItemId,
    string Product,
    string Sku,
    decimal Available,
    decimal Minimum,
    decimal Quantity,
    string Unit,
    string Justification);

public sealed class AiStockAssistant(
    DatabaseExecutor database,
    IAiProviderFactory aiFactory,
    IInventoryService inventoryService,
    ITenantContext tenant,
    IAiQuotaService quotaService,
    ILogger<AiStockAssistant> logger) : IAiStockAssistant
{
    private const string UseCase = "stock_assistant";

    public async Task<AiStockAssistantResponse> AskAsync(string question, CancellationToken ct = default)
    {
        // 1. Validação de pergunta conforme regras canônicas
        var normalizedQuestion = AssistantQueryRules.NormalizeQuestion(question);

        if (!tenant.IsAvailable || tenant.TenantId == Guid.Empty || tenant.UserId == Guid.Empty)
        {
            throw new ForbiddenException("Sessão expirada ou contexto de organização ausente.");
        }

        // 2. Autorização estrita de acesso a IA, Estoque, Entitlement e Escopo de Unidade
        var access = await AuthorizeStockAssistantAccessAsync(ct);

        // 3. Reserva Atômica de Quota com idempotência e payloadHash
        var idempotencyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{tenant.TenantId}:{tenant.UserId}:{normalizedQuestion}")))[..32];
        var reservation = await quotaService.ReserveQuotaAsync(
            tenant.TenantId,
            tenant.UserId,
            UseCase,
            estimatedTokens: 1500,
            idempotencyKey: idempotencyHash,
            payloadHash: idempotencyHash,
            ct: ct);

        // Se for repetição idempotente concluída anteriormente, retorna sem reexecutar
        if (reservation.IsReplay && reservation.ReplayRecord != null)
        {
            var replay = reservation.ReplayRecord;
            return new AiStockAssistantResponse(
                Answer: replay.ErrorMessage ?? "Consulta recuperada do histórico recente.",
                Data: null,
                SuggestsReplenishment: false,
                Draft: null,
                SourceProvider: replay.Provider ?? "Cache",
                Model: replay.Model ?? "",
                TotalTokens: replay.TotalTokens,
                Duration: replay.Duration,
                TotalRecords: 0
            );
        }

        var provider = aiFactory.GetProvider();
        var startTime = DateTime.UtcNow;
        var totalPromptTokens = 0;
        var totalCompletionTokens = 0;

        try
        {
            // 4. Extração segura da entidade / termo de busca
            var extractionResponse = await provider.GenerateCompletionAsync(new AiRequest(
                Prompt: $"Extraia apenas o nome do produto ou categoria da seguinte pergunta de estoque: \"{normalizedQuestion}\". Se não for possível identificar, responda 'null'. Responda em no máximo 50 caracteres contendo apenas o termo.",
                SystemPrompt: "Você é um extrator de termos de estoque estritamente seguro. Retorne apenas o nome do produto ou categoria pesquisado, ou 'null'.",
                MaxTokens: 60,
                Temperature: 0.1
            ), ct);

            totalPromptTokens += extractionResponse.PromptTokens;
            totalCompletionTokens += extractionResponse.CompletionTokens;

            var rawTerm = extractionResponse.Content.Trim().Replace("\"", "").Replace("'", "");
            var searchTerm = string.Equals(rawTerm, "null", StringComparison.OrdinalIgnoreCase) || rawTerm.Length > 80
                ? null
                : rawTerm;

            // 5. Consulta Canônica de Saldos
            var balancesResult = await inventoryService.ListBalancesAsync(1, 50, searchTerm, ct);
            var balances = balancesResult.Items.ToList();

            if (balances.Count == 0 && !string.IsNullOrWhiteSpace(searchTerm))
            {
                // Fallback para listagem geral caso termo extraído tenha sido muito específico
                balancesResult = await inventoryService.ListBalancesAsync(1, 50, null, ct);
                balances = balancesResult.Items.ToList();
            }

            // 6. Preparação de Contexto com Proteção de Dados Financeiros
            // Se o usuário não possui permissão de leitura financeira, o custo médio é omitido da LLM
            var contextLines = balances.Select(b =>
            {
                var costInfo = access.CanViewFinance ? $", Custo Médio: R$ {b.AverageCost:N2}" : "";
                return $"- Produto: {b.ProductName} (SKU: {b.Sku}), Disponível: {b.Available} {b.Unit}, Reservado: {b.Reserved} {b.Unit}, Mínimo: {b.Minimum} {b.Unit}{costInfo}";
            });

            var contextText = balances.Count > 0
                ? string.Join("\n", contextLines)
                : "Nenhum produto em estoque encontrado para esta consulta.";

            // 7. Síntese Executiva via Provedor de IA
            var synthesisPrompt = $"""
                Pergunta do usuário: {normalizedQuestion}

                Dados de Saldo de Estoque ({balances.Count} de {balancesResult.Total} registros):
                {contextText}

                Instruções:
                - Responda objetivamente à pergunta em português com base estritamente nos dados de saldo fornecidos.
                - Se houver produtos com saldo disponível abaixo do estoque mínimo, aponte claramente a necessidade de reposição.
                - Não invente números ou produtos não listados.
                """;

            var synthesisResponse = await provider.GenerateCompletionAsync(new AiRequest(
                Prompt: synthesisPrompt,
                SystemPrompt: "Você é o assistente inteligente de estoque do Agro360. Seja conciso, técnico e execute análises precisas.",
                MaxTokens: 800,
                Temperature: 0.2
            ), ct);

            totalPromptTokens += synthesisResponse.PromptTokens;
            totalCompletionTokens += synthesisResponse.CompletionTokens;

            var duration = DateTime.UtcNow - startTime;

            // 8. Reconciliação Atômica da Quota (todas as chamadas somadas)
            await quotaService.ReconcileAndCompleteAsync(
                executionId: reservation.ExecutionId,
                provider: provider.ProviderName,
                model: synthesisResponse.Model,
                promptTokens: totalPromptTokens,
                completionTokens: totalCompletionTokens,
                duration: duration,
                tokenConfidence: synthesisResponse.TokenConfidence,
                success: true,
                errorMessage: null,
                ct: ct);

            // 9. Lógica Estruturada de Rascunho de Reposição
            var criticalItem = balances.FirstOrDefault(b => b.Available < b.Minimum);
            ReplenishmentDraft? draft = null;
            bool suggests = false;

            if (criticalItem != null)
            {
                suggests = true;
                var neededQty = Math.Max(0, criticalItem.Minimum - criticalItem.Available);

                // Consulta ID canônico do catálogo se existir
                var catalogItemId = await database.InTenantTransactionAsync(async (c, t) =>
                {
                    return await c.ExecuteScalarAsync<Guid?>(new CommandDefinition(
                        """
                        select id from agro360.procurement_item_catalog
                        where tenant_id = @TenantId and related_product_id = @ProductId and active = true and deleted_at is null
                        limit 1
                        """,
                        new { tenant.TenantId, criticalItem.ProductId },
                        t,
                        cancellationToken: ct));
                }, ct);

                draft = new ReplenishmentDraft(
                    ProductId: criticalItem.ProductId,
                    CatalogItemId: catalogItemId,
                    Product: criticalItem.ProductName,
                    Sku: criticalItem.Sku,
                    Available: criticalItem.Available,
                    Minimum: criticalItem.Minimum,
                    Quantity: neededQty,
                    Unit: criticalItem.Unit,
                    Justification: $"Reposição sugerida: saldo disponível ({criticalItem.Available} {criticalItem.Unit}) abaixo do estoque mínimo ({criticalItem.Minimum} {criticalItem.Unit})."
                );
            }

            return new AiStockAssistantResponse(
                Answer: synthesisResponse.Content,
                Data: balances,
                SuggestsReplenishment: suggests,
                Draft: draft,
                SourceProvider: provider.ProviderName,
                Model: synthesisResponse.Model,
                TotalTokens: totalPromptTokens + totalCompletionTokens,
                Duration: duration,
                TotalRecords: balancesResult.Total
            );
        }
        catch (OperationCanceledException)
        {
            if (totalPromptTokens + totalCompletionTokens > 0)
            {
                await quotaService.ReconcileAndCompleteAsync(
                    executionId: reservation.ExecutionId,
                    provider: provider.ProviderName,
                    model: provider.DefaultModel,
                    promptTokens: totalPromptTokens,
                    completionTokens: totalCompletionTokens,
                    duration: DateTime.UtcNow - startTime,
                    tokenConfidence: "ESTIMATED",
                    success: false,
                    errorMessage: "Cancelado pelo usuário durante processamento",
                    ct: CancellationToken.None);
            }
            else
            {
                await quotaService.ReleaseReservationAsync(reservation.ExecutionId, "Cancelado pelo usuário", CancellationToken.None);
            }

            throw;
        }
        catch (Exception ex)
        {
            InfrastructureLogMessages.AiStockAssistantFailed(logger, tenant.TenantId, ex);

            if (totalPromptTokens + totalCompletionTokens > 0)
            {
                // Se já houve chamada externa no provedor, reconcilia os tokens consumidos
                await quotaService.ReconcileAndCompleteAsync(
                    executionId: reservation.ExecutionId,
                    provider: provider.ProviderName,
                    model: provider.DefaultModel,
                    promptTokens: totalPromptTokens,
                    completionTokens: totalCompletionTokens,
                    duration: DateTime.UtcNow - startTime,
                    tokenConfidence: "ESTIMATED",
                    success: false,
                    errorMessage: ex.Message,
                    ct: CancellationToken.None);
            }
            else
            {
                await quotaService.ReleaseReservationAsync(reservation.ExecutionId, ex.Message, CancellationToken.None);
            }

            throw;
        }
    }

    private async Task<AssistantSecurityCheck> AuthorizeStockAssistantAccessAsync(CancellationToken ct)
    {
        return await database.InTenantTransactionAsync(async (conn, tx) =>
        {
            var result = await conn.QuerySingleAsync<AssistantSecurityCheck>(new CommandDefinition(
                $"""
                select
                    -- Permissão de Inteligência
                    exists(
                        select 1 from agro360.identity_users u
                        join agro360.identity_user_roles ur on ur.tenant_id=u.tenant_id and ur.user_id=u.id
                        join agro360.identity_role_permissions rp on rp.tenant_id=ur.tenant_id and rp.role_id=ur.role_id
                        join agro360.identity_permissions p on p.id=rp.permission_id
                        where u.tenant_id=@TenantId and u.id=@UserId and u.status='ACTIVE' and u.deleted_at is null
                          and p.code in ('intelligence.view', 'assistant.use')
                    ) HasAiPermission,

                    -- Permissão de Estoque
                    exists(
                        select 1 from agro360.identity_users u
                        join agro360.identity_user_roles ur on ur.tenant_id=u.tenant_id and ur.user_id=u.id
                        join agro360.identity_role_permissions rp on rp.tenant_id=ur.tenant_id and rp.role_id=ur.role_id
                        join agro360.identity_permissions p on p.id=rp.permission_id
                        where u.tenant_id=@TenantId and u.id=@UserId and u.status='ACTIVE' and u.deleted_at is null
                          and p.code in ('inventory.view', 'inventory.balances', 'inventory.movements')
                    ) HasInventoryPermission,

                    -- Entitlement de Inteligência
                    exists(
                        select 1 from ({EntitlementQueries.ModuleCodeSelect}) em
                        where em.module_code in ('intelligence', 'ai')
                    ) HasAiContract,

                    -- Entitlement de Estoque
                    exists(
                        select 1 from ({EntitlementQueries.ModuleCodeSelect}) em
                        where em.module_code in ('inventory', 'storage')
                    ) HasInventoryContract,

                    -- Permissão para visualizar custo financeiro
                    exists(
                        select 1 from agro360.identity_users u
                        join agro360.identity_user_roles ur on ur.tenant_id=u.tenant_id and ur.user_id=u.id
                        join agro360.identity_role_permissions rp on rp.tenant_id=ur.tenant_id and rp.role_id=ur.role_id
                        join agro360.identity_permissions p on p.id=rp.permission_id
                        where u.tenant_id=@TenantId and u.id=@UserId and u.status='ACTIVE' and u.deleted_at is null
                          and p.code in ('finance.view', 'finance.payables', 'finance.reports')
                    ) CanViewFinance
                """,
                new { tenant.TenantId, tenant.UserId },
                tx,
                cancellationToken: ct));

            if (!result.HasAiPermission)
                throw new ForbiddenException("Seu perfil de usuário não possui permissão para utilizar o assistente de IA.");

            if (!result.HasInventoryPermission)
                throw new ForbiddenException("Seu perfil de usuário não possui permissão para consultar saldos de estoque.");

            if (!result.HasAiContract)
                throw new ForbiddenException("O módulo de Inteligência / IA não está contratado ou vigente para esta organização.");

            if (!result.HasInventoryContract)
                throw new ForbiddenException("O módulo de Estoque não está contratado ou vigente para esta organização.");

            return result;
        }, ct);
    }

    private sealed class AssistantSecurityCheck
    {
        public bool HasAiPermission { get; init; }
        public bool HasInventoryPermission { get; init; }
        public bool HasAiContract { get; init; }
        public bool HasInventoryContract { get; init; }
        public bool CanViewFinance { get; init; }
    }
}
