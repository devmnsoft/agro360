using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Agro360.Infrastructure.Services.Ai;

public sealed class GeminiProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IAiProvider
{
    public string ProviderName => "Gemini";
    public string DefaultModel => "gemini-1.5-pro";

    public async Task<AiResponse> GenerateCompletionAsync(AiRequest request, CancellationToken ct = default)
    {
        var apiKey = configuration["Ai:Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("Gemini API Key não configurada.");

        using var client = httpClientFactory.CreateClient();
        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{DefaultModel}:generateContent?key={apiKey}";

        var payload = new
        {
            contents = new[]
            {
                new {
                    role = "user",
                    parts = new[] { new { text = $"{request.SystemPrompt}\n\n{request.Prompt}" } }
                }
            },
            generationConfig = new
            {
                temperature = request.Temperature,
                maxOutputTokens = request.MaxTokens
            }
        };

        var startTime = DateTime.UtcNow;
        var response = await client.PostAsJsonAsync(url, payload, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"Gemini API Error ({response.StatusCode}): {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: ct);
        var duration = DateTime.UtcNow - startTime;

        return new AiResponse(
            Content: result?.Candidates[0].Content.Parts[0].Text ?? "",
            Provider: ProviderName,
            Model: DefaultModel,
            PromptTokens: result?.UsageMetadata?.PromptTokenCount ?? 0,
            CompletionTokens: result?.UsageMetadata?.CandidatesTokenCount ?? 0,
            Duration: duration
        );
    }

    private record GeminiResponse(List<Candidate> Candidates, UsageMetadata UsageMetadata);
    private record Candidate(Content Content);
    private record Content(List<Part> Parts);
    private record Part(string Text);
    private record UsageMetadata(int PromptTokenCount, int CandidatesTokenCount);
}
