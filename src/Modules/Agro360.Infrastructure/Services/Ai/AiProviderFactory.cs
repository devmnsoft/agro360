using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Agro360.Infrastructure.Services.Ai;

public interface IAiProviderFactory
{
    IAiProvider GetProvider(string? preferredProvider = null);
}

public sealed class AiProviderFactory(
    IServiceProvider serviceProvider) : IAiProviderFactory
{
    public IAiProvider GetProvider(string? preferredProvider = null)
    {
        var providers = serviceProvider.GetServices<IAiProvider>().ToList();

        if (!string.IsNullOrEmpty(preferredProvider))
        {
            var provider = providers.FirstOrDefault(p => p.ProviderName.Equals(preferredProvider, StringComparison.OrdinalIgnoreCase));
            if (provider != null) return provider;
        }

        return providers.FirstOrDefault() ?? throw new InvalidOperationException("Nenhum provedor de IA configurado.");
    }
}
