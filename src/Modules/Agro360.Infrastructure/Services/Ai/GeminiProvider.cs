using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Agro360.SharedKernel;
using Microsoft.Extensions.Configuration;

namespace Agro360.Infrastructure.Services.Ai;

public sealed class GeminiProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IAiProvider
{
    private static readonly HashSet<string> AllowedModels = new(StringComparer.OrdinalIgnoreCase)
    {
        "gemini-1.5-pro",
        "gemini-1.5-flash",
        "gemini-2.0-flash",
        "gemini-2.5-flash"
    };

    public string ProviderName => "Gemini";

    public string DefaultModel
    {
        get
        {
            var configured = configuration["Ai:Gemini:Model"];
            if (!string.IsNullOrWhiteSpace(configured) && AllowedModels.Contains(configured.Trim()))
            {
                return configured.Trim();
            }
            return "gemini-1.5-flash";
        }
    }

    public bool IsEnabled =>
        string.Equals(configuration["Ai:Gemini:Enabled"], "true", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(configuration["Ai:Gemini:ApiKey"]);

    public async Task<AiResponse> GenerateCompletionAsync(AiRequest request, CancellationToken ct = default)
    {
        var apiKey = configuration["Ai:Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new DomainException("Credencial da API Gemini não configurada.", "ai.gemini.missing_key");

        var model = DefaultModel;
        using var client = httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(30);

        // Uso do header oficial x-goog-api-key para proteger a chave contra vazamento em logs/URLs
        client.DefaultRequestHeaders.Add("x-goog-api-key", apiKey.Trim());

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent";

        var payload = new
        {
            contents = new[]
            {
                new {
                    role = "user",
                    parts = new[] { new { text = string.IsNullOrWhiteSpace(request.SystemPrompt) ? request.Prompt : $"{request.SystemPrompt}\n\n{request.Prompt}" } }
                }
            },
            generationConfig = new
            {
                temperature = Math.Clamp(request.Temperature, 0.0, 1.0),
                maxOutputTokens = Math.Clamp(request.MaxTokens, 1, 4096)
            }
        };

        var startTime = DateTime.UtcNow;
        HttpResponseMessage response;
        try
        {
            response = await client.PostAsJsonAsync(url, payload, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new DomainException($"Falha de conectividade com a API Gemini: {ex.Message}", "ai.gemini.connectivity_error");
        }

        if (!response.IsSuccessStatusCode)
        {
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                throw new DomainException("Taxa de requisições excedida no provedor Gemini (429).", "ai.gemini.rate_limited");
            }
            if ((int)response.StatusCode >= 500)
            {
                throw new DomainException($"Indisponibilidade momentânea no serviço Gemini ({(int)response.StatusCode}).", "ai.gemini.service_unavailable");
            }

            throw new DomainException($"Erro na requisição ao Gemini ({(int)response.StatusCode}).", "ai.gemini.request_error");
        }

        var result = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: ct);
        var duration = DateTime.UtcNow - startTime;

        var text = result?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text ?? string.Empty;
        var promptTokens = result?.UsageMetadata?.PromptTokenCount ?? 0;
        var completionTokens = result?.UsageMetadata?.CandidatesTokenCount ?? 0;

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

    private sealed record GeminiResponse(List<Candidate>? Candidates, UsageMetadata? UsageMetadata);
    private sealed record Candidate(Content? Content);
    private sealed record Content(List<Part>? Parts);
    private sealed record Part(string? Text);
    private sealed record UsageMetadata(int PromptTokenCount, int CandidatesTokenCount);
}
