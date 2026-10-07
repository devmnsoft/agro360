using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Agro360.Infrastructure.Services.Ai;

public sealed class DeepSeekProvider(IHttpClientFactory httpClientFactory, IConfiguration configuration) : IAiProvider
{
    public string ProviderName => "DeepSeek";
    public string DefaultModel => "deepseek-chat";

    public async Task<AiResponse> GenerateCompletionAsync(AiRequest request, CancellationToken ct = default)
    {
        var apiKey = configuration["Ai:DeepSeek:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("DeepSeek API Key não configurada.");

        using var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var payload = new
        {
            model = DefaultModel,
            messages = new List<object>
            {
                new { role = "system", content = request.SystemPrompt },
                (request.History?.Select(m => new { role = m.Role, content = m.Content }) ?? Enumerable.Empty<object>()).Cast<object>().ToList(),
                new { role = "user", content = request.Prompt }
            },
            temperature = request.Temperature,
            max_tokens = request.MaxTokens
        };

        var startTime = DateTime.UtcNow;
        var response = await client.PostAsJsonAsync("https://api.deepseek.com/chat/completions", payload, ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync(ct);
            throw new HttpRequestException($"DeepSeek API Error ({response.StatusCode}): {error}");
        }

        var result = await response.Content.ReadFromJsonAsync<DeepSeekResponse>(cancellationToken: ct);
        var duration = DateTime.UtcNow - startTime;

        return new AiResponse(
            Content: result?.Choices[0].Message.Content ?? "",
            Provider: ProviderName,
            Model: DefaultModel,
            PromptTokens: result?.Usage.PromptTokens ?? 0,
            CompletionTokens: result?.Usage.CompletionTokens ?? 0,
            Duration: duration
        );
    }

    private record DeepSeekResponse(List<Choice> Choices, Usage Usage);
    private record Choice(Message Message);
    private record Message(string Content);
    private record Usage(int PromptTokens, int CompletionTokens);
}
