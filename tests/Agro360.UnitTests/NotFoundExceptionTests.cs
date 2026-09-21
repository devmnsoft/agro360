using Agro360.SharedKernel;
using Xunit;

namespace Agro360.UnitTests;

public sealed class NotFoundExceptionTests
{
    [Fact]
    public void SupportsBusinessStringIdentifiers()
    {
        var exception = new NotFoundException("Lote", "LT-2026-001");

        Assert.Equal("resource_not_found", exception.Code);
        Assert.Contains("LT-2026-001", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PreservesGuidIdentifierContract()
    {
        var id = Guid.NewGuid();
        var exception = new NotFoundException("Safra", id);

        Assert.Equal("resource_not_found", exception.Code);
        Assert.Contains(id.ToString(), exception.Message, StringComparison.Ordinal);
    }
}
