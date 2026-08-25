using Agro360.Application.Abstractions;
using Agro360.Application.Contracts;
using Agro360.Infrastructure.Persistence;
using Agro360.Infrastructure.Security;
using Agro360.Infrastructure.Services;
using Agro360.Multitenancy;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Agro360.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAgro360Infrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Agro360")
            ?? throw new InvalidOperationException("ConnectionStrings:Agro360 não foi configurada. Defina ConnectionStrings__Agro360 ou use um secret manager.");

        services.AddSingleton<IDbConnectionFactory>(_ => new NpgsqlConnectionFactory(connectionString));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<ITokenService, JwtTokenService>();

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TenantContext>());
        services.AddScoped<IMutableTenantContext>(provider => provider.GetRequiredService<TenantContext>());
        services.AddScoped<DatabaseExecutor>();

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IPropertyService, PropertyService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IAgricultureService, AgricultureService>();
        services.AddScoped<ILivestockService, LivestockService>();
        services.AddScoped<ICommercialService, CommercialService>();
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddScoped<IOperationsService, OperationsService>();
        services.AddScoped<IGlobalSearchService, GlobalSearchService>();
        services.AddScoped<ITraceabilityService, TraceabilityService>();
        return services;
    }
}
