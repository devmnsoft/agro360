using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Services.Ai;
using Agro360.Multitenancy;
using Microsoft.Extensions.Logging;

namespace Agro360.Infrastructure.Services;

public interface IAiStockAssistant
{
    Task<AiStockAssistantResponse> AskAsync(string question, CancellationToken ct = default);
}

public record AiStockAssistantResponse(
    string Answer,
    List<StockBalanceDto>? Data,
    bool SuggestsReplenishment,
    ReplenishmentDraft? Draft,
    string SourceProvider);

public record ReplenishmentDraft(string Product, decimal Quantity, string Unit, string Justification);

public sealed class AiStockAssistant(
    IAiProviderFactory aiFactory,
    IInventoryService inventoryService,
    ITenantContext tenant,
    IAiQuotaService quotaService,
    IAiExecutionService executionService,
    ILogger<AiStockAssistant> logger) : IAiStockAssistant
{
    public async Task<AiStockAssistantResponse> AskAsync(string question, CancellationToken ct = default)
    {
        const string useCase = "stock_assistant";

        // 1. Validar Quota
        if (!await quotaService.HasQuotaAsync(tenant.TenantId, useCase, ct))
        {
            return new AiStockAssistantResponse(
                Answer: "Seu plano de IA atingiu o limite de consumo para este assistente. Por favor, contate o administrador.",
                Data: null,
                SuggestsReplenishment: false,
                Draft: null,
                SourceProvider: "None"
            );
        }

        // 2. Resolver provedor
        var provider = aiFactory.GetProvider();

        try
        {
            // 3. Identificar intenção e entidades
            var entityExtraction = await provider.GenerateCompletionAsync(new AiRequest(
                Prompt: $"Extraia apenas o nome do produto ou categoria da seguinte pergunta de estoque: \"{question}\". Se não houver, retorne 'null'. Responda apenas com o termo ou 'null'.",
                SystemPrompt: "Você é um extrator de entidades especializado em insumos agrícolas."
            ), ct);

            var searchTerm = entityExtraction.Content.Trim();
            if (string.Equals(searchTerm, "null", StringComparison.OrdinalIgnoreCase))
            {
                return new AiStockAssistantResponse(
                    Answer: "Não consegui identificar qual produto ou categoria você deseja consultar. Pode ser mais específico?",
                    Data: null,
                    SuggestsReplenishment: false,
                    Draft: null,
                    SourceProvider: provider.ProviderName
                );
            }

            // 4. Consulta Canônica
            var balances = await inventoryService.ListBalancesAsync(1, 50, searchTerm, ct);
            var data = balances.Items.ToList();

            if (data.Count == 0)
            {
                return new AiStockAssistantResponse(
                    Answer: $"Não encontrei registros de saldo para \"{searchTerm}\" na sua unidade.",
                    Data: null,
                    SuggestsReplenishment: false,
                    Draft: null,
                    SourceProvider: provider.ProviderName
                );
            }

            // 5. Sintetização da Resposta
            var context = string.Join("\n", data.Select(b =>
                $"- {b.ProductName} ({b.Sku}): {b.Available} {b.Unit} disponível / {b.Reserved} reservado. Mínimo: {b.Minimum} {b.Unit}. Custo Médio: {b.AverageCost}"));

            var finalResponse = await provider.GenerateCompletionAsync(new AiRequest(
                Prompt: $"Pergunta: {question}\n\nDados de Saldo:\n{context}\n\nResponda de forma executiva. Se o saldo estiver abaixo do mínimo, destaque a necessidade de reposição.",
                SystemPrompt: "Você é o Assistente de Estoque do Agro360. Use os dados fornecidos para responder. Não invente números. Seja preciso e profissional."
            ), ct);

            // 6. Registrar Execução
            await executionService.RegisterExecutionAsync(new AiExecutionRecord(
                TenantId: tenant.TenantId,
                UserId: tenant.UserId,
                Provider: finalResponse.Provider,
                Model: finalResponse.Model,
                PromptTokens: finalResponse.PromptTokens,
                CompletionTokens: finalResponse.CompletionTokens,
                Duration: finalResponse.Duration,
                UseCase: useCase,
                Success: true
            ), ct);

            // 7. Lógica de Reposição
            var criticalItem = data.FirstOrDefault(b => b.Available < b.Minimum);
            ReplenishmentDraft? draft = null;
            bool suggests = false;

            if (criticalItem != null)
            {
                suggests = true;
                draft = new ReplenishmentDraft(
                    Product: criticalItem.ProductName,
                    Quantity: criticalItem.Minimum - criticalItem.Available,
                    Unit: criticalItem.Unit,
                    Justification: $"Reposição automática: Saldo ({criticalItem.Available}) abaixo do mínimo ({criticalItem.Minimum})."
                );
            }

            return new AiStockAssistantResponse(
                Answer: finalResponse.Content,
                Data: data,
                SuggestsReplenishment: suggests,
                Draft: draft,
                SourceProvider: provider.ProviderName
            );
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Falha na execução do assistente de estoque para o tenant {TenantId}", tenant.TenantId);
            return new AiStockAssistantResponse(
                Answer: "Ocorreu um erro ao processar sua consulta. Por favor, tente novamente em instantes.",
                Data: null,
                SuggestsReplenishment: false,
                Draft: null,
                SourceProvider: "Error"
            );
        }
    }
}
