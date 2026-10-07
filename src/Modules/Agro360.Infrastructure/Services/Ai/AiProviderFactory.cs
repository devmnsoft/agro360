using System;
using System.Collections.Generic;
using System.Linq;
using Agro360.SharedKernel;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agro360.Infrastructure.Services.Ai;

public interface IAiProviderFactory
{
    IAiProvider GetProvider(string? preferredProvider = null);
    IReadOnlyList<string> GetAvailableProviders();
}

public sealed class AiProviderFactory(
    IServiceProvider serviceProvider,
    IConfiguration configuration) : IAiProviderFactory
{
    public IAiProvider GetProvider(string? preferredProvider = null)
    {
        var providers = serviceProvider.GetServices<IAiProvider>().Where(p => p.IsEnabled).ToList();

        if (!string.IsNullOrWhiteSpace(preferredProvider))
        {
            var preferred = providers.FirstOrDefault(p => p.ProviderName.Equals(preferredProvider.Trim(), StringComparison.OrdinalIgnoreCase));
            if (preferred != null) return preferred;
        }

        var defaultProviderName = configuration["Ai:DefaultProvider"] ?? "Groq";
        var defaultProvider = providers.FirstOrDefault(p => p.ProviderName.Equals(defaultProviderName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (defaultProvider != null) return defaultProvider;

        var any = providers.FirstOrDefault();
        if (any != null) return any;

        throw new DomainException(
            "Nenhum provedor de IA habilitado e configurado no momento.",
            "ai.provider.unavailable");
    }

    public IReadOnlyList<string> GetAvailableProviders()
    {
        return serviceProvider.GetServices<IAiProvider>()
            .Where(p => p.IsEnabled)
            .Select(p => p.ProviderName)
            .ToList();
    }
}
