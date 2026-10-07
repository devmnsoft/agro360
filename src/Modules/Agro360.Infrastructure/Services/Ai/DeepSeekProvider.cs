using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Agro360.SharedKernel;
using Microsoft.Extensions.Configuration;

namespace Agro360.Infrastructure.Services.Ai;

public sealed class DeepSeekProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IAiProvider
{
    private static readonly HashSet<string> AllowedModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "deepseek-chat",
        "deepseek-reasoner"
    };

    public string ProviderName => "DeepSeek";

    public string DefaultModel
    {
        get
        {
            var configured = configuration["Ai:DeepSeek:Model"];
            if (!string.IsNullOrWhiteSpace(configured) && AllowedModels.Contains(configured.Trim()))
            {
                return configured.Trim();
            }
            return "deepseek-chat";
        }
    }

    public bool IsEnabled =>
        string.Equals(configuration["Ai:DeepSeek:Enabled"], "true", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(configuration["Ai:DeepSeek:ApiKey"]);

    public async Task<AiResponse> GenerateCompletionAsync(AiRequest request, CancellationToken ct = default)
    {
        var apiKey = configuration["Ai:DeepSeek:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new DomainException("Credencial da API DeepSeek não configurada.", "ai.deepseek.missing_key");

        var model = DefaultModel;
        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());

        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.SystemPrompt))
        {
            messages.Add(new { role = "system", content = request.SystemPrompt });
        }

        if (request.History != null)
        {
            foreach (var msg in request.History)
            {
                messages.Add(new { role = msg.Role.ToLowerInvariant(), content = msg.Content });
            }
        }

        messages.Add(new { role = "user", content = request.Prompt });

        var payload = new
        {
            model = model,
            messages = messages,
            temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
            max_tokens = Math.Clamp(request.MaxTokens, 1, 4096)
        };

        var startTime = DateTime.UtcNow;
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync("https://api.deepseek.com/chat/completions", payload, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DomainException($"Falha de conectividade com a API DeepSeek: {ex.Message}", "ai.deepseek.connectivity_error");
        }

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new DomainException("Taxa de requisições excedida no provedor DeepSeek (429).", "ai.deepseek.rate_limited");
            }
            if ((int)response.StatusCode >= 500)
            {
                throw new DomainException($"Indisponibilidade momentânea no serviço DeepSeek ({(int)response.StatusCode}).", "ai.deepseek.service_unavailable");
            }

            throw new DomainException($"Erro na requisição ao DeepSeek ({(int)response.StatusCode}).", "ai.deepseek.request_error");
        }

        var result = await response.Content.ReadFromJsonAsync<DeepSeekResponse>(cancellationToken: ct);
        var duration = DateTime.UtcNow - startTime;

        var text = result?.Choices?.FirstOrDefault()?.Message?.Content ?? string.Empty;
        var promptTokens = result?.Usage?.PromptTokens ?? 0;
        var completionTokens = result?.Usage?.CompletionTokens ?? 0;

        return new AiResponse(
            Content: text,
            Provider: ProviderName,
            Model: model,
            PromptTokens: promptTokens,
            CompletionTokens: completionTokens,
            Duration: duration,
            TokenConfidence: (promptTokens > 0 || completionTokens > 0) ? "EXACT" : "ESTIMATED"
        );
    }

    private sealed record DeepSeekResponse(List<Choice>? Choices, Usage? Usage);
    private sealed record Choice(Message? Message);
    private sealed record Message(string? Content);
    private sealed record Usage(int PromptTokens, int CompletionTokens);
}
