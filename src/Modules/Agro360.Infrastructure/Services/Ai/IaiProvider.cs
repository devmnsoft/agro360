using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Agro360.Infrastructure.Services.Ai;

public record AiRequest(
    string Prompt,
    string SystemPrompt = "",
    int MaxTokens = 2048,
    double Temperature = 0.7,
    List<AiMessage>? History = null);

public record AiMessage(string Role, string Content);

public record AiResponse(
    string Content,
    string Provider,
    string Model,
    int PromptTokens,
    int CompletionTokens,
    TimeSpan Duration);

public interface IAiProvider
{
    string ProviderName { get; }
    string DefaultModel { get; }
    Task<AiResponse> GenerateCompletionAsync(AiRequest request, CancellationToken ct = default);
}
