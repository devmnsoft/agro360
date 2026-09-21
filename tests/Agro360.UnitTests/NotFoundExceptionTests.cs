using Agro360.SharedKernel;
using Xunit;

namespace Agro360.UnitTests;

public sealed class NotFoundExceptionTests
{
    [Fact]
    public void KeepsGuidIdentifierContract()
    {
        var constructors = typeof(NotFoundException).GetConstructors();

        var constructor = Assert.Single(constructors);
        Assert.Equal(
            [typeof(string), typeof(Guid)],
            constructor.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
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
