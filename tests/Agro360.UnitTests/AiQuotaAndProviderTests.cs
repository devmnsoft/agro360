using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Agro360.Infrastructure.Services.Ai;
using Agro360.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Agro360.UnitTests;

public sealed class AiQuotaAndProviderTests
{
    [Fact]
    public void FactoryThrowsWhenNoProviderIsEnabled()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();
        var provider = services.BuildServiceProvider();

        var factory = new AiProviderFactory(provider, config);

        var ex = Assert.Throws<DomainException>(() => factory.GetProvider());
        Assert.Equal("ai.provider.unavailable", ex.Code);
    }

    [Fact]
    public void FactoryReturnsPreferredProviderWhenEnabled()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder().Build();

        var mockGroq = new MockAiProvider("Groq", "llama-3.3-70b-versatile", true);
        var mockGemini = new MockAiProvider("Gemini", "gemini-1.5-flash", true);

        services.AddSingleton<IAiProvider>(mockGroq);
        services.AddSingleton<IAiProvider>(mockGemini);

        var serviceProvider = services.BuildServiceProvider();
        var factory = new AiProviderFactory(serviceProvider, config);

        var selected = factory.GetProvider("Gemini");
        Assert.Equal("Gemini", selected.ProviderName);
    }

    [Fact]
    public void FactoryFallsBackToDefaultWhenPreferredUnavailable()
    {
        var services = new ServiceCollection();
        var inMemory = new Dictionary<string, string?>
        {
            ["Ai:DefaultProvider"] = "Groq"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();

        var mockGroq = new MockAiProvider("Groq", "llama-3.3-70b-versatile", true);
        services.AddSingleton<IAiProvider>(mockGroq);

        var serviceProvider = services.BuildServiceProvider();
        var factory = new AiProviderFactory(serviceProvider, config);

        var selected = factory.GetProvider("NonExistentProvider");
        Assert.Equal("Groq", selected.ProviderName);
    }

    [Fact]
    public void GeminiProviderValidatesModelAllowlistAndDisabledWithoutKey()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Ai:Gemini:Enabled"] = "true",
            ["Ai:Gemini:ApiKey"] = "",
            ["Ai:Gemini:Model"] = "unauthorized-model"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var services = new ServiceCollection();
        services.AddHttpClient();
        var sp = services.BuildServiceProvider();
        var clientFactory = sp.GetRequiredService<IHttpClientFactory>();

        var provider = new GeminiProvider(clientFactory, config);

        Assert.False(provider.IsEnabled);
        Assert.Equal("gemini-1.5-flash", provider.DefaultModel); // Fallback to safe default
    }

    [Fact]
    public void GroqProviderValidatesModelAllowlist()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["Ai:Groq:Enabled"] = "true",
            ["Ai:Groq:ApiKey"] = "gsk_test_key",
            ["Ai:Groq:Model"] = "llama-3.1-8b-instant"
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var services = new ServiceCollection();
        services.AddHttpClient();
        var sp = services.BuildServiceProvider();
        var clientFactory = sp.GetRequiredService<IHttpClientFactory>();

        var provider = new GroqProvider(clientFactory, config);

        Assert.True(provider.IsEnabled);
        Assert.Equal("llama-3.1-8b-instant", provider.DefaultModel);
    }

    private sealed class MockAiProvider(string name, string model, bool enabled) : IAiProvider
    {
        public string ProviderName => name;
        public string DefaultModel => model;
        public bool IsEnabled => enabled;

        public Task<AiResponse> GenerateCompletionAsync(AiRequest request, CancellationToken ct = default)
        {
            return Task.FromResult(new AiResponse("Mock Content", name, model, 10, 20, TimeSpan.FromMilliseconds(50)));
        }
    }
}
