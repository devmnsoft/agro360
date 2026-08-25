using Agro360.Application.Contracts;
using Agro360.Domain.Tenancy;

namespace Agro360.ArchitectureTests;

public sealed class DependencyRulesTests
{
    [Fact]
    public void DomainMustNotReferenceInfrastructureOrHosts()
    {
        var references = typeof(Tenant).Assembly.GetReferencedAssemblies().Select(item => item.Name).ToArray();

        Assert.DoesNotContain("Agro360.Infrastructure", references);
        Assert.DoesNotContain("Agro360.Api", references);
        Assert.DoesNotContain("Agro360.Web", references);
        Assert.DoesNotContain("Dapper", references);
        Assert.DoesNotContain("Npgsql", references);
    }

    [Fact]
    public void ApplicationMustNotReferenceInfrastructureOrWeb()
    {
        var references = typeof(IIdentityService).Assembly.GetReferencedAssemblies().Select(item => item.Name).ToArray();

        Assert.DoesNotContain("Agro360.Infrastructure", references);
        Assert.DoesNotContain("Agro360.Api", references);
        Assert.DoesNotContain("Agro360.Web", references);
    }

    [Fact]
    public void DomainTypesMustNotExposeDatabaseOrHttpNamespaces()
    {
        var invalid = typeof(Tenant).Assembly.GetTypes()
            .SelectMany(type => type.GetProperties().Select(property => property.PropertyType)
                .Concat(type.GetMethods().Select(method => method.ReturnType)))
            .Select(type => type.Namespace)
            .OfType<string>()
            .Where(namespaceName => namespaceName.StartsWith("Npgsql", StringComparison.Ordinal)
                || namespaceName.StartsWith("Dapper", StringComparison.Ordinal)
                || namespaceName.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(invalid);
    }
}
